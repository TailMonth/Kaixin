using KaixinPet.Behavior;
using KaixinPet.Pet.Assets;

namespace KaixinPet.Pet.Animation;

public readonly record struct OverlayFrame(string Clip, int Frame);

public sealed record AnimationFrameSet(
    string BaseClip,
    int BaseFrame,
    IReadOnlyList<OverlayFrame> Overlays);

public sealed class PetAnimationPlayer : IPetAnimationSink
{
    private readonly Dictionary<string, ClipDef> _clips;
    private readonly Dictionary<string, OverlayState> _overlays = new(StringComparer.OrdinalIgnoreCase);

    private string _baseClip;
    private TimeSpan _baseStart;
    private bool _baseStartPending;
    private string? _baseNext;
    private bool _baseFinishedRaised;
    private int _baseVersion;
    private TimeSpan _now;

    public PetAnimationPlayer(CharacterManifest manifest)
    {
        _clips = manifest.Clips;
        _baseClip = manifest.Clips.ContainsKey("idle.breath") ? "idle.breath" : manifest.Clips.Keys.First();
    }

    public event Action<string>? BaseClipFinished;

    public string? CurrentBaseClip => _baseClip;

    public void PlayBase(string clip, string? next = null)
    {
        if (!_clips.TryGetValue(clip, out var definition))
        {
            throw new InvalidOperationException($"未知动画片段：{clip}");
        }

        if (next is not null && !_clips.ContainsKey(next))
        {
            throw new InvalidOperationException($"未知后续片段：{next}（来自 {clip}）");
        }

        _baseClip = clip;
        _baseNext = next;
        _baseStartPending = true;
        _baseFinishedRaised = false;
        _baseVersion++;
        _ = definition;
    }

    public void PlayOverlay(string clip)
    {
        if (!_clips.ContainsKey(clip))
        {
            throw new InvalidOperationException($"未知叠加片段：{clip}");
        }

        _overlays[clip] = new OverlayState
        {
            Start = _now,
            PendingStart = true,
            Loop = false
        };
    }

    public void SetLoopOverlay(string clip, bool enabled)
    {
        if (enabled)
        {
            if (_overlays.TryGetValue(clip, out var overlay) && overlay.Loop)
            {
                return;
            }

            _overlays[clip] = new OverlayState
            {
                Start = _now,
                PendingStart = true,
                Loop = true
            };
        }
        else
        {
            _overlays.Remove(clip);
        }
    }

    public AnimationFrameSet Advance(TimeSpan now)
    {
        _now = now;

        if (_baseStartPending)
        {
            _baseStart = now;
            _baseStartPending = false;
        }

        var baseDefinition = _clips[_baseClip];
        var elapsed = now - _baseStart;
        var baseFrame = GetFrameIndex(baseDefinition, elapsed);

        if (!baseDefinition.Loop && !_baseFinishedRaised && elapsed >= ClipDuration(baseDefinition))
        {
            _baseFinishedRaised = true;
            var finishedClip = _baseClip;
            var versionBeforeEvent = _baseVersion;
            BaseClipFinished?.Invoke(finishedClip);

            if (_baseVersion == versionBeforeEvent && _baseNext is not null)
            {
                var next = _baseNext;
                _baseClip = next;
                _baseNext = null;
                _baseStart = now;
                _baseStartPending = false;
                _baseFinishedRaised = false;
                _baseVersion++;
            }
        }

        if (_baseStartPending)
        {
            _baseStart = now;
            _baseStartPending = false;
        }

        baseDefinition = _clips[_baseClip];
        baseFrame = GetFrameIndex(baseDefinition, now - _baseStart);
        if (!baseDefinition.Loop && now - _baseStart >= ClipDuration(baseDefinition))
        {
            baseFrame = baseDefinition.Frames.Length - 1;
        }

        var overlays = new List<OverlayFrame>(_overlays.Count);
        List<string>? finishedOverlays = null;

        foreach (var pair in _overlays)
        {
            var overlay = pair.Value;
            if (overlay.PendingStart)
            {
                overlay.Start = now;
                overlay.PendingStart = false;
            }

            var definition = _clips[pair.Key];
            var overlayElapsed = now - overlay.Start;
            var overlayDuration = ClipDuration(definition);
            if (!overlay.Loop && overlayElapsed >= overlayDuration)
            {
                finishedOverlays ??= new List<string>();
                finishedOverlays.Add(pair.Key);
                continue;
            }

            var frame = GetFrameIndex(definition, overlayElapsed);
            overlays.Add(new OverlayFrame(pair.Key, frame));
        }

        if (finishedOverlays is not null)
        {
            foreach (var clip in finishedOverlays)
            {
                _overlays.Remove(clip);
            }
        }

        return new AnimationFrameSet(_baseClip, baseFrame, overlays);
    }

    private static int GetFrameIndex(ClipDef definition, TimeSpan elapsed)
    {
        var count = definition.Frames.Length;
        if (count == 0)
        {
            return 0;
        }

        var raw = (int)(elapsed.TotalSeconds * Math.Max(1, definition.Fps));
        if (definition.Loop)
        {
            return ((raw % count) + count) % count;
        }

        return Math.Clamp(raw, 0, count - 1);
    }

    private static TimeSpan ClipDuration(ClipDef definition) =>
        TimeSpan.FromSeconds(definition.Frames.Length / Math.Max(1, definition.Fps));

    private sealed class OverlayState
    {
        public TimeSpan Start;
        public bool PendingStart;
        public bool Loop;
    }
}
