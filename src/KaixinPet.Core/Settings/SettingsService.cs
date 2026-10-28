using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using KaixinPet.Core.Logging;

namespace KaixinPet.Core.Settings;

public sealed class SettingsService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _settingsPath;
    private readonly string _backupPath;
    private Timer? _saveTimer;
    private bool _dirty;
    private bool _disposed;

    public SettingsService(string dataRoot)
    {
        _directory = dataRoot;
        _settingsPath = Path.Combine(dataRoot, "settings.json");
        _backupPath = Path.Combine(dataRoot, "settings.bak.json");
    }

    public AppSettings Current { get; private set; } = new();

    public string SettingsPath => _settingsPath;

    public string? LoadIssue { get; private set; }

    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_settingsPath))
            {
                Current = new AppSettings();
                WriteLocked();
                Log.Info($"settings created with defaults: {_settingsPath}");
                return;
            }

            try
            {
                var json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                var migrated = loaded.SchemaVersion < AppSettings.CurrentSchemaVersion;
                Current = Normalize(loaded);
                Current.SchemaVersion = AppSettings.CurrentSchemaVersion;

                Log.Info(
                    $"settings loaded: path={_settingsPath}, schema={loaded.SchemaVersion}, " +
                    $"migrated={migrated}, portable={AppPaths.IsPortable}");
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException)
            {
                LoadIssue = exception.Message;
                var corruptPath = Path.Combine(
                    _directory, $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                try
                {
                    File.Move(_settingsPath, corruptPath, overwrite: true);
                    Log.Warn($"settings corrupted; backed up to {corruptPath}");
                }
                catch (Exception moveException)
                {
                    Log.Warn($"settings corrupted; backup failed: {moveException.Message}");
                }

                Log.Error("settings load failed; falling back to defaults", exception);
                Current = new AppSettings();
                WriteLocked();
            }
        }
    }

    public void SaveDebounced()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _dirty = true;
            _saveTimer ??= new Timer(_ => FlushFromTimer(), null, Timeout.Infinite, Timeout.Infinite);
            _saveTimer.Change(500, Timeout.Infinite);
        }
    }

    public void SaveNow()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            WriteLocked();
        }
    }

    private void FlushFromTimer()
    {
        lock (_gate)
        {
            if (_disposed || !_dirty)
            {
                return;
            }

            WriteLocked();
        }
    }

    private void WriteLocked()
    {
        try
        {
            Directory.CreateDirectory(_directory);
            Current.SchemaVersion = AppSettings.CurrentSchemaVersion;

            var json = JsonSerializer.Serialize(Current, JsonOptions);
            var tempPath = _settingsPath + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));

            if (File.Exists(_settingsPath))
            {
                File.Replace(tempPath, _settingsPath, _backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _settingsPath);
            }

            _dirty = false;
            Log.Info($"settings saved: {_settingsPath}");
        }
        catch (Exception exception)
        {
            Log.Error("settings save failed", exception);
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.General ??= new GeneralSettings();
        settings.Pomodoro ??= new PomodoroSettings();
        settings.Audio ??= new AudioSettings();
        settings.Behavior ??= new BehaviorSettings();

        var general = settings.General;
        general.PetName = string.IsNullOrWhiteSpace(general.PetName) ? "开心" : general.PetName.Trim();
        if (general.PetName.Length > 24)
        {
            general.PetName = general.PetName[..24];
        }

        general.Scale = general.Scale switch
        {
            <= 0.9 => 0.8,
            <= 1.1 => 1.0,
            _ => 1.2
        };

        general.Theme = general.Theme is "light" or "dark" ? general.Theme : "system";

        if (general.LastPosition is { } position &&
            (Math.Abs(position.X) > 100_000 || Math.Abs(position.Y) > 100_000))
        {
            general.LastPosition = null;
        }

        var pomodoro = settings.Pomodoro;
        pomodoro.FocusMinutes = Math.Clamp(pomodoro.FocusMinutes, 1, 180);
        pomodoro.ShortBreakMinutes = Math.Clamp(pomodoro.ShortBreakMinutes, 1, 60);
        pomodoro.LongBreakMinutes = Math.Clamp(pomodoro.LongBreakMinutes, 1, 60);
        pomodoro.RoundsPerLongBreak = Math.Clamp(pomodoro.RoundsPerLongBreak, 1, 12);
        pomodoro.Hotkey = string.IsNullOrWhiteSpace(pomodoro.Hotkey) ? "Ctrl+Alt+P" : pomodoro.Hotkey.Trim();

        var audio = settings.Audio;
        audio.MasterVolume = Math.Clamp(audio.MasterVolume, 0, 1);
        audio.EffectsVolume = Math.Clamp(audio.EffectsVolume, 0, 1);
        audio.AmbientVolume = Math.Clamp(audio.AmbientVolume, 0, 1);
        audio.CustomSounds ??= new Dictionary<string, string>();

        var behavior = settings.Behavior;
        behavior.SleepAfterIdleMinutes = Math.Clamp(behavior.SleepAfterIdleMinutes, 1, 240);
        behavior.FocusQuietAfterMinutes = Math.Clamp(behavior.FocusQuietAfterMinutes, 0, 60);
        behavior.PlaySessionMaxSeconds = Math.Clamp(behavior.PlaySessionMaxSeconds, 10, 300);
        return settings;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _saveTimer?.Dispose();
            _saveTimer = null;

            if (_dirty)
            {
                WriteLocked();
            }

            _disposed = true;
        }
    }
}
