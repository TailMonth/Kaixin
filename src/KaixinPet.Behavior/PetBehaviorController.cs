using System.Diagnostics;
using KaixinPet.Core.Logging;

namespace KaixinPet.Behavior;

public sealed class PetBehaviorController
{
    private readonly IPetAnimationSink _animations;
    private readonly BehaviorConfig _config;
    private readonly BehaviorOptions _options;
    private readonly Random _random;
    private readonly YarnBallSimulator _ball = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, TimeSpan> _nextActionAllowed = new(StringComparer.OrdinalIgnoreCase);

    private TimeSpan _lastInteraction;
    private TimeSpan _nextBlink;
    private TimeSpan _nextMicro;
    private TimeSpan _nextTwitch;
    private TimeSpan _nextSpontaneous;

    private double _catOffsetX;
    private bool _pendingPlay;
    private double _playElapsed;
    private double _playLimitSeconds;
    private double _batCooldown;
    private double _batClipUntil;
    private bool _focusMode;
    private int _focusQuietMinutes = 2;
    private string? _pendingTimerReaction;

    public PetBehaviorController(
        IPetAnimationSink animations,
        BehaviorConfig config,
        BehaviorOptions options,
        int randomSeed = 20261028)
    {
        _animations = animations;
        _config = config;
        _options = options;
        _random = new Random(randomSeed);
    }

    public PetBehaviorState State { get; private set; } = PetBehaviorState.Idle;

    public double CatOffsetXDip => _catOffsetX;

    public BallState? Ball => _ball.Active ? new BallState(_ball.X, _ball.Y, _ball.FrameIndex) : null;

    public bool IsPlaying => State == PetBehaviorState.Play;

    public bool IsSleeping => State == PetBehaviorState.Sleep;

    public event Action? StateChanged;

    public void Start()
    {
        _lastInteraction = Now;
        ScheduleIdle(Now);
        _animations.PlayBase("idle.breath");
        Log.Info("behavior: idle");
    }

    public void Tick()
    {
        var now = Now;

        if (State == PetBehaviorState.Idle && _animations.CurrentBaseClip == "idle.breath")
        {
            if (now >= _nextBlink)
            {
                _animations.PlayOverlay("idle.blink");
                _nextBlink = now + Seconds(Range(_config.Idle.BlinkMinSeconds, _config.Idle.BlinkMaxSeconds));
            }

            var sleepAfter = _focusMode
                ? TimeSpan.FromMinutes(Math.Max(1, _focusQuietMinutes))
                : TimeSpan.FromMinutes(Math.Max(1, _options.SleepAfterIdleMinutes));
            if (now - _lastInteraction >= sleepAfter)
            {
                Sleep();
                return;
            }

            if (!_focusMode &&
                _options.SpontaneousPlay &&
                now >= _nextSpontaneous)
            {
                _nextSpontaneous = now + Seconds(Range(
                    _config.Play.SpontaneousMinSeconds, _config.Play.SpontaneousMaxSeconds));
                if (_random.NextDouble() < _config.Play.SpontaneousChance)
                {
                    StartPlay();
                    return;
                }
            }

            if (now >= _nextMicro)
            {
                _nextMicro = now + Seconds(Range(_config.Idle.MicroMinSeconds, _config.Idle.MicroMaxSeconds));
                if (TryChooseIdleAction(now, out var clip, out var cooldown))
                {
                    _nextActionAllowed[clip] = now + Seconds(cooldown);
                    _animations.PlayBase(clip, "idle.breath");
                    Log.Info($"behavior: idle action {clip}");
                }
            }
        }
        else if (State == PetBehaviorState.Sleep &&
                 _animations.CurrentBaseClip == "sleep.breath" &&
                 now >= _nextTwitch)
        {
            _animations.PlayBase("sleep.twitch", "sleep.breath");
            _nextTwitch = now + Seconds(Range(
                _config.Sleep.TwitchMinSeconds, _config.Sleep.TwitchMaxSeconds));
        }
    }

    public void OnClipFinished(string clip)
    {
        if (clip == "sleep.wake" && _pendingPlay)
        {
            StartPlayNow();
            return;
        }

        if (clip == "drag.land" && _pendingTimerReaction is { } pending)
        {
            _pendingTimerReaction = null;
            _animations.PlayBase(pending, "idle.breath");
        }
    }

    public void SetFocusMode(bool focus, int quietMinutes)
    {
        _focusMode = focus;
        _focusQuietMinutes = Math.Clamp(quietMinutes, 1, 60);
        _lastInteraction = Now;
        if (focus)
        {
            _pendingPlay = false;
        }
    }

    public void PlayTimerReaction(string clip)
    {
        if (State is PetBehaviorState.Drag or PetBehaviorState.Fall)
        {
            _pendingTimerReaction = clip;
            return;
        }

        PlayTransient(clip);
    }

    private void PlayTransient(string clip)
    {
        _pendingPlay = false;
        _ball.Stop();
        _catOffsetX = 0;
        _animations.SetLoopOverlay("zzz", false);
        SetState(PetBehaviorState.Idle);
        _animations.PlayBase(clip, "idle.breath");
        _lastInteraction = Now;
        ScheduleIdle(Now);
        Log.Info($"behavior: timer reaction {clip}");
    }

    public void Pet()
    {
        if (State is PetBehaviorState.Drag or PetBehaviorState.Fall)
        {
            return;
        }

        _lastInteraction = Now;

        if (State == PetBehaviorState.Sleep)
        {
            Wake();
            return;
        }

        var next = State == PetBehaviorState.Play ? "play.chase" : "idle.breath";
        _animations.PlayBase("react.pet", next);
        _animations.PlayOverlay("hearts");
        Log.Info("behavior: pet");
    }

    public void StartPlay()
    {
        if (State is PetBehaviorState.Drag or PetBehaviorState.Fall or PetBehaviorState.Play)
        {
            return;
        }

        _lastInteraction = Now;

        if (State == PetBehaviorState.Sleep)
        {
            _pendingPlay = true;
            Wake();
            return;
        }

        StartPlayNow();
    }

    public void ToggleSleep()
    {
        switch (State)
        {
            case PetBehaviorState.Sleep:
                Wake();
                break;
            case PetBehaviorState.Idle:
            case PetBehaviorState.Play:
                Sleep();
                break;
        }
    }

    public void StopPlay()
    {
        if (State != PetBehaviorState.Play)
        {
            return;
        }

        EndPlay("user");
    }

    public void BeginDrag()
    {
        _pendingPlay = false;
        _ball.Stop();
        _catOffsetX = 0;
        _animations.SetLoopOverlay("zzz", false);
        SetState(PetBehaviorState.Drag);
        _animations.PlayBase("drag.grab", "drag.hold");
        _lastInteraction = Now;
        Log.Info("behavior: drag");
    }

    public void EndDrag()
    {
        if (State != PetBehaviorState.Drag)
        {
            return;
        }

        SetState(PetBehaviorState.Fall);
        _animations.PlayBase("drag.fall");
        Log.Info("behavior: fall");
    }

    public void Landed()
    {
        SetState(PetBehaviorState.Idle);
        _animations.PlayBase("drag.land", "idle.breath");
        _lastInteraction = Now;
        ScheduleIdle(Now);
        Log.Info("behavior: landed");
    }

    public void UpdatePhysics(double dt)
    {
        if (State != PetBehaviorState.Play)
        {
            return;
        }

        _playElapsed += dt;
        var catCenter = _options.CatCenterXDip + _catOffsetX;
        _ball.Step(dt, _options.GroundYDip, _options.WindowWidthDip);

        if (!_ball.Active)
        {
            EndPlay("rest");
            return;
        }

        var current = _animations.CurrentBaseClip;

        if (current == "play.chase")
        {
            var target = Math.Clamp(_ball.X - _options.CatCenterXDip, -40, 100);
            var step = 180 * dt;
            _catOffsetX += Math.Clamp(target - _catOffsetX, -step, step);
            catCenter = _options.CatCenterXDip + _catOffsetX;
        }

        var nearPaw = Math.Abs(_ball.X - (catCenter + 24)) < 70;
        if (current is "play.chase" or "play.bat" &&
            _playElapsed >= _batCooldown &&
            nearPaw)
        {
            var direction = _ball.X >= catCenter ? 1 : -1;
            _ball.Bat(direction, _random);
            _batCooldown = _playElapsed + 0.8;
            _batClipUntil = _playElapsed + 0.45;
            if (current != "play.bat")
            {
                _animations.PlayBase("play.bat");
            }
        }

        current = _animations.CurrentBaseClip;
        if (current is "play.chase" or "play.bat" &&
            _playElapsed > _batClipUntil &&
            _playElapsed >= _batCooldown - 0.35)
        {
            var distance = Math.Abs(_ball.X - catCenter);
            if (distance > 62 && current != "play.chase")
            {
                _animations.PlayBase("play.chase");
            }
            else if (distance <= 62 && current != "play.bat")
            {
                _animations.PlayBase("play.bat");
            }
        }

        if (_playElapsed >= _playLimitSeconds)
        {
            EndPlay("timeout");
        }
    }

    private void StartPlayNow()
    {
        _pendingPlay = false;
        SetState(PetBehaviorState.Play);
        _catOffsetX = 0;
        _playElapsed = 0;
        _batCooldown = 0.6;
        _batClipUntil = 0;

        var maxSeconds = Math.Clamp(_options.PlaySessionMaxSeconds, 10, 300);
        var minSeconds = Math.Min(_config.Play.MinSeconds, maxSeconds);
        _playLimitSeconds = minSeconds + _random.NextDouble() * (maxSeconds - minSeconds);

        _ball.Start(_random, _options.CatCenterXDip, _options.GroundYDip, _options.WindowWidthDip);
        _animations.PlayBase("play.notice", "play.chase");
        Log.Info($"behavior: play start ({_playLimitSeconds:0}s)");
    }

    private void EndPlay(string reason)
    {
        _ball.Stop();
        _catOffsetX = 0;
        SetState(PetBehaviorState.Idle);
        _animations.PlayBase("play.flop", "idle.breath");
        _lastInteraction = Now;
        ScheduleIdle(Now);
        Log.Info($"behavior: play ended ({reason})");
    }

    private void Sleep()
    {
        if (State is PetBehaviorState.Drag or PetBehaviorState.Fall)
        {
            return;
        }

        _pendingPlay = false;
        _ball.Stop();
        _catOffsetX = 0;
        SetState(PetBehaviorState.Sleep);
        _animations.SetLoopOverlay("zzz", true);
        _animations.PlayBase("sleep.curl", "sleep.breath");
        _nextTwitch = Now + Seconds(Range(
            _config.Sleep.TwitchMinSeconds, _config.Sleep.TwitchMaxSeconds));
        _lastInteraction = Now;
        Log.Info("behavior: sleep");
    }

    private void Wake()
    {
        if (State != PetBehaviorState.Sleep)
        {
            return;
        }

        _animations.SetLoopOverlay("zzz", false);
        _animations.PlayBase("sleep.wake", "idle.breath");
        SetState(PetBehaviorState.Idle);
        _lastInteraction = Now;
        ScheduleIdle(Now);
        Log.Info("behavior: wake");
    }

    private bool TryChooseIdleAction(TimeSpan now, out string clip, out double cooldown)
    {
        var candidates = _config.Idle.Actions
            .Where(action => !string.IsNullOrWhiteSpace(action.Clip))
            .Where(action =>
                !_nextActionAllowed.TryGetValue(action.Clip, out var availableAt) || now >= availableAt)
            .Where(action => Math.Max(0.01, action.Weight) > 0)
            .ToList();

        if (candidates.Count == 0)
        {
            clip = string.Empty;
            cooldown = 0;
            return false;
        }

        var total = candidates.Sum(action => Math.Max(0.01, action.Weight));
        var pick = _random.NextDouble() * total;
        foreach (var candidate in candidates)
        {
            pick -= Math.Max(0.01, candidate.Weight);
            if (pick <= 0)
            {
                clip = candidate.Clip;
                cooldown = candidate.CooldownSeconds;
                return true;
            }
        }

        var last = candidates[^1];
        clip = last.Clip;
        cooldown = last.CooldownSeconds;
        return true;
    }

    private void ScheduleIdle(TimeSpan now)
    {
        _nextBlink = now + Seconds(Range(_config.Idle.BlinkMinSeconds, _config.Idle.BlinkMaxSeconds));
        _nextMicro = now + Seconds(Range(_config.Idle.MicroMinSeconds, _config.Idle.MicroMaxSeconds));
        _nextSpontaneous = now + Seconds(Range(
            _config.Play.SpontaneousMinSeconds, _config.Play.SpontaneousMaxSeconds));
    }

    private void SetState(PetBehaviorState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke();
    }

    private TimeSpan Now => _clock.Elapsed;

    private double Range(double min, double max) => min + _random.NextDouble() * (max - min);

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);
}
