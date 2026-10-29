using KaixinPet.Core.Logging;
using KaixinPet.Core.Settings;
using NAudio.Wave;

namespace KaixinPet.Audio;

public sealed record SoundSourceInfo(bool HasCustom, string DisplayName, bool CustomMissing);

public sealed class AudioService : IDisposable
{
    private const int MaxConcurrentEffects = 2;
    private const long MaxCacheBytes = 8L * 1024 * 1024;
    private const int FadeStepMilliseconds = 30;

    private readonly object _gate = new();
    private readonly string _soundsRoot;
    private readonly string _dataRoot;
    private readonly SoundCatalog _catalog;
    private readonly Dictionary<string, DateTime> _cooldownUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ActiveEffect> _active = [];
    private readonly HashSet<string> _missingCustomLogged = new(StringComparer.OrdinalIgnoreCase);

    private AudioSettings _settings = new();
    private long _cacheBytes;
    private bool _ambientActive;
    private WaveOutEvent? _ambientOutput;
    private LoopStream? _ambientLoop;
    private WaveFileReader? _ambientReader;
    private MemoryStream? _ambientStream;
    private System.Threading.Timer? _fadeTimer;
    private float _ambientTarget;
    private bool _ambientStopPending;
    private bool _deviceErrorLogged;
    private bool _disposed;

    public AudioService(string soundsRoot, string dataRoot)
    {
        _soundsRoot = soundsRoot;
        _dataRoot = dataRoot;
        _catalog = SoundCatalog.Load(soundsRoot);
    }

    public IReadOnlyList<SoundEventDefinition> Events => _catalog.Events;

    public bool IsMuted => _settings.Muted;

    public void ApplySettings(AudioSettings settings)
    {
        lock (_gate)
        {
            _settings = settings;
            _cache.Clear();
            _cacheBytes = 0;
            var target = ComputeAmbientVolume();
            _ambientTarget = target;
            foreach (var effect in _active)
            {
                effect.Output.Volume = ComputeEffectVolume(effect.Definition, effect.Gain);
            }

            if (settings.Muted)
            {
                StopAmbientLocked();
                StopAllEffectsLocked();
            }
            else
            {
                ReconcileAmbientLocked();
                if (_ambientOutput is not null)
                {
                    EnsureFadeTimerLocked();
                }
            }
        }
    }

    public void SetAmbientActive(bool active)
    {
        lock (_gate)
        {
            _ambientActive = active;
            ReconcileAmbientLocked();
        }
    }

    public void UpdateCustomSounds(Dictionary<string, string> customSounds)
    {
        lock (_gate)
        {
            _settings.CustomSounds = customSounds;
            _cache.Clear();
            _cacheBytes = 0;
            if (_ambientOutput is not null)
            {
                StopAmbientLocked();
            }

            ReconcileAmbientLocked();
        }
    }

    public bool Play(string eventId, double gain = 1.0)
    {
        lock (_gate)
        {
            if (_disposed || _settings.Muted)
            {
                return false;
            }

            var definition = _catalog.Find(eventId);
            if (definition is null || definition.Loop)
            {
                return false;
            }

            var now = DateTime.UtcNow;
            if (_cooldownUntil.TryGetValue(eventId, out var until) && now < until)
            {
                return false;
            }

            _cooldownUntil[eventId] = now.AddSeconds(definition.CooldownSeconds);
            var path = ResolvePath(definition);
            return path is not null && TryPlayEffectLocked(definition, path, gain);
        }
    }

    public bool Preview(string eventId)
    {
        lock (_gate)
        {
            if (_disposed || _settings.Muted)
            {
                return false;
            }

            var definition = _catalog.Find(eventId);
            if (definition is null)
            {
                return false;
            }

            var path = ResolvePath(definition);
            return path is not null && TryPlayEffectLocked(definition, path, 1.0);
        }
    }

    public SoundSourceInfo GetSourceInfo(string eventId)
    {
        lock (_gate)
        {
            var custom = TryResolveCustomPath(eventId, out var customPath);
            if (custom)
            {
                var missing = !File.Exists(customPath);
                return new SoundSourceInfo(
                    true,
                    missing ? "自定义文件缺失" : Path.GetFileName(customPath),
                    missing);
            }

            return new SoundSourceInfo(false, "默认", false);
        }
    }

    private bool TryPlayEffectLocked(SoundEventDefinition definition, string path, double gain)
    {
        byte[] bytes;
        try
        {
            bytes = LoadBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"audio read failed: event={definition.Id}, error={exception.GetType().Name}");
            return false;
        }

        if (bytes.Length == 0)
        {
            return false;
        }

        Log.Info($"audio: play {definition.Id} ({(IsCustomActive(definition.Id) ? "custom" : "default")})");

        while (_active.Count >= MaxConcurrentEffects)
        {
            var oldest = _active[0];
            _active.RemoveAt(0);
            DisposeEffect(oldest);
        }

        try
        {
            var stream = new MemoryStream(bytes, writable: false);
            var reader = new WaveFileReader(stream);
            var output = new WaveOutEvent
            {
                DesiredLatency = 80,
                NumberOfBuffers = 2
            };
            var effect = new ActiveEffect(definition, gain, output, reader, stream);
            output.PlaybackStopped += (_, _) => OnPlaybackStopped(effect);
            output.Init(reader);
            output.Volume = ComputeEffectVolume(definition, gain);
            output.Play();
            _active.Add(effect);
            return true;
        }
        catch (Exception exception)
        {
            if (!_deviceErrorLogged)
            {
                _deviceErrorLogged = true;
                Log.Warn($"audio output unavailable; continuing silently: {exception.GetType().Name}: {exception.Message}");
            }

            return false;
        }
    }

    private void OnPlaybackStopped(ActiveEffect effect)
    {
        lock (_gate)
        {
            if (effect.Disposed)
            {
                return;
            }

            _active.Remove(effect);
            DisposeEffect(effect);
        }
    }

    private void DisposeEffect(ActiveEffect effect)
    {
        if (effect.Disposed)
        {
            return;
        }

        effect.Disposed = true;
        try
        {
            effect.Output.Stop();
        }
        catch
        {
            // The device may already be gone; playback is best effort.
        }

        effect.Output.Dispose();
        effect.Reader.Dispose();
        effect.Stream.Dispose();
    }

    private void StopAllEffectsLocked()
    {
        foreach (var effect in _active.ToArray())
        {
            DisposeEffect(effect);
        }

        _active.Clear();
    }

    private float ComputeEffectVolume(SoundEventDefinition definition, double gain)
    {
        var volume = _settings.MasterVolume * _settings.EffectsVolume * definition.Volume * gain;
        return (float)Math.Clamp(volume, 0, 1);
    }

    private float ComputeAmbientVolume()
    {
        var purr = _catalog.Find("ambient.purr");
        var baseVolume = purr?.Volume ?? 0.25;
        var volume = _settings.MasterVolume * _settings.AmbientVolume * baseVolume;
        return (float)Math.Clamp(volume, 0, 1);
    }

    private void ReconcileAmbientLocked()
    {
        var shouldPlay = _ambientActive &&
                         _settings.AmbientPurrDuringFocus &&
                         !_settings.Muted &&
                         _settings.MasterVolume > 0 &&
                         _settings.AmbientVolume > 0;
        if (shouldPlay)
        {
            if (_ambientOutput is null)
            {
                StartAmbientLocked();
            }

            if (_ambientOutput is not null)
            {
                _ambientStopPending = false;
                _ambientTarget = ComputeAmbientVolume();
                EnsureFadeTimerLocked();
            }
        }
        else if (_ambientOutput is not null)
        {
            _ambientStopPending = true;
            _ambientTarget = 0;
            EnsureFadeTimerLocked();
        }
    }

    private void StartAmbientLocked()
    {
        var definition = _catalog.Find("ambient.purr");
        if (definition is null)
        {
            return;
        }

        var path = ResolvePath(definition);
        if (path is null)
        {
            return;
        }

        try
        {
            var bytes = LoadBytes(path);
            var stream = new MemoryStream(bytes, writable: false);
            var reader = new WaveFileReader(stream);
            var loop = new LoopStream(reader);
            var output = new WaveOutEvent
            {
                DesiredLatency = 100,
                NumberOfBuffers = 2
            };
            output.Init(loop);
            output.Volume = 0;
            output.Play();
            _ambientReader = reader;
            _ambientStream = stream;
            _ambientLoop = loop;
            _ambientOutput = output;
            Log.Info("audio: ambient purr started");
        }
        catch (Exception exception)
        {
            if (!_deviceErrorLogged)
            {
                _deviceErrorLogged = true;
                Log.Warn($"audio ambient unavailable: {exception.GetType().Name}: {exception.Message}");
            }

            StopAmbientLocked();
        }
    }

    private void EnsureFadeTimerLocked()
    {
        _fadeTimer ??= new System.Threading.Timer(
            _ => OnFadeTick(), null, Timeout.Infinite, Timeout.Infinite);
        _fadeTimer.Change(FadeStepMilliseconds, FadeStepMilliseconds);
    }

    private void OnFadeTick()
    {
        lock (_gate)
        {
            if (_ambientOutput is null)
            {
                _fadeTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            var current = _ambientOutput.Volume;
            var step = 0.1f;
            var next = Math.Abs(_ambientTarget - current) <= step
                ? _ambientTarget
                : current + Math.Sign(_ambientTarget - current) * step;
            _ambientOutput.Volume = Math.Clamp(next, 0, 1);

            if (_ambientStopPending && _ambientOutput.Volume <= 0.001f)
            {
                StopAmbientLocked();
                return;
            }

            if (!_ambientStopPending && Math.Abs(_ambientOutput.Volume - _ambientTarget) < 0.001f)
            {
                _fadeTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
    }

    private void StopAmbientLocked()
    {
        var wasRunning = _ambientOutput is not null;
        _ambientStopPending = false;
        _ambientTarget = 0;
        _fadeTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        if (_ambientOutput is not null)
        {
            try
            {
                _ambientOutput.Stop();
            }
            catch
            {
                // Best effort during shutdown or device removal.
            }

            _ambientOutput.Dispose();
            _ambientOutput = null;
        }

        _ambientLoop?.Dispose();
        _ambientLoop = null;
        _ambientReader?.Dispose();
        _ambientReader = null;
        _ambientStream?.Dispose();
        _ambientStream = null;

        if (wasRunning)
        {
            Log.Info("audio: ambient purr stopped");
        }
    }

    private string? ResolvePath(SoundEventDefinition definition)
    {
        if (TryResolveCustomPath(definition.Id, out var customPath) && File.Exists(customPath))
        {
            return customPath;
        }

        var path = Path.Combine(_soundsRoot, definition.File);
        if (File.Exists(path))
        {
            return path;
        }

        Log.Warn($"audio default missing: event={definition.Id}, file={definition.File}");
        return null;
    }

    private bool TryResolveCustomPath(string eventId, out string path)
    {
        path = string.Empty;
        if (_settings.CustomSounds.TryGetValue(eventId, out var relative) &&
            !string.IsNullOrWhiteSpace(relative))
        {
            path = Path.IsPathRooted(relative) ? relative : Path.Combine(_dataRoot, relative);
            if (!File.Exists(path) && _missingCustomLogged.Add(eventId))
            {
                Log.Warn($"audio custom missing: event={eventId}; falling back to default");
            }

            return true;
        }

        return false;
    }

    private bool IsCustomActive(string eventId) =>
        TryResolveCustomPath(eventId, out var path) && File.Exists(path);

    private byte[] LoadBytes(string path)
    {
        if (_cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length <= MaxCacheBytes)
        {
            if (_cacheBytes + bytes.Length > MaxCacheBytes)
            {
                _cache.Clear();
                _cacheBytes = 0;
            }

            _cache[path] = bytes;
            _cacheBytes += bytes.Length;
        }

        return bytes;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopAllEffectsLocked();
            StopAmbientLocked();
            _fadeTimer?.Dispose();
            _fadeTimer = null;
            _cache.Clear();
            _cacheBytes = 0;
        }
    }

    private sealed class ActiveEffect(
        SoundEventDefinition definition,
        double gain,
        WaveOutEvent output,
        WaveFileReader reader,
        MemoryStream stream)
    {
        public SoundEventDefinition Definition { get; } = definition;

        public double Gain { get; } = gain;

        public WaveOutEvent Output { get; } = output;

        public WaveFileReader Reader { get; } = reader;

        public MemoryStream Stream { get; } = stream;

        public bool Disposed { get; set; }
    }

    private sealed class LoopStream(WaveStream source) : WaveStream
    {
        public override WaveFormat WaveFormat => source.WaveFormat;

        public override long Length => long.MaxValue;

        public override long Position
        {
            get => source.Position;
            set => source.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = 0;
            while (read < count)
            {
                var current = source.Read(buffer, offset + read, count - read);
                if (current == 0)
                {
                    if (source.Position == 0)
                    {
                        break;
                    }

                    source.Position = 0;
                    continue;
                }

                read += current;
            }

            return read;
        }
    }
}
