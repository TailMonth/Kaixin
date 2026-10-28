using System.IO;
using KaixinPet.Pet.Assets;

namespace KaixinPet.Pet.Animation;

public readonly record struct AnimationFrameState(int BreathFrame, int BlinkFrame);

public sealed class PetAnimator
{
    private readonly ClipDef _breath;
    private readonly ClipDef _blink;
    private readonly Random _random;

    private bool _epochSet;
    private TimeSpan _epoch;
    private TimeSpan _nextBlinkAt;
    private TimeSpan? _blinkStartedAt;

    public bool BenchmarkMode { get; set; }

    public PetAnimator(CharacterManifest manifest, int randomSeed = 20261028)
    {
        _breath = GetClip(manifest, "idle.breath");
        _blink = GetClip(manifest, "idle.blink");
        _random = new Random(randomSeed);
    }

    public AnimationFrameState Advance(TimeSpan now)
    {
        if (!_epochSet)
        {
            _epoch = now;
            _epochSet = true;
            ScheduleNextBlink(TimeSpan.Zero);
        }

        var elapsed = now - _epoch;
        var breathFps = BenchmarkMode ? 24.0 : _breath.Fps;
        var breathFrame = (int)(elapsed.TotalSeconds * breathFps) % Math.Max(1, _breath.Frames.Length);

        var blinkFrame = -1;
        if (_blinkStartedAt is null && elapsed >= _nextBlinkAt)
        {
            _blinkStartedAt = elapsed;
        }

        if (_blinkStartedAt is { } startedAt)
        {
            var frame = (int)((elapsed - startedAt).TotalSeconds * _blink.Fps);
            if (frame >= _blink.Frames.Length)
            {
                _blinkStartedAt = null;
                ScheduleNextBlink(elapsed);
            }
            else
            {
                blinkFrame = frame;
            }
        }

        return new AnimationFrameState(breathFrame, blinkFrame);
    }

    private void ScheduleNextBlink(TimeSpan from)
    {
        _nextBlinkAt = from + TimeSpan.FromSeconds(3 + _random.NextDouble() * 6);
    }

    private static ClipDef GetClip(CharacterManifest manifest, string name)
    {
        if (!manifest.Clips.TryGetValue(name, out var clip))
        {
            throw new InvalidDataException($"角色清单缺少动画片段：{name}");
        }

        if (clip.Frames.Length == 0)
        {
            throw new InvalidDataException($"动画片段没有帧：{name}");
        }

        return clip;
    }
}
