using System.Diagnostics;
using KaixinPet.Core.Logging;

namespace KaixinPet.Behavior;

public sealed class PetBehaviorController
{
    private readonly IPetAnimationSink _animations;
    private readonly BehaviorConfig _config;
    private BehaviorOptions _options;
    private readonly Random _random;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, TimeSpan> _nextActionAllowed = new(StringComparer.OrdinalIgnoreCase);

    private TimeSpan _lastInteraction;
    private TimeSpan _nextBlink;
    private TimeSpan _nextMicro;
    private TimeSpan _nextTwitch;
    private TimeSpan _nextReadAction;
    private TimeSpan _nextPageFlip;
    private TimeSpan _pageFlipStart;
    private bool _pageFlipActive;
    private bool _focusMode;
    private bool _readingPaused;
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

    public bool IsReading => State == PetBehaviorState.Reading;

    public bool IsSleeping => State == PetBehaviorState.Sleep;

    public int BookFrame
    {
        get
        {
            if (State != PetBehaviorState.Reading || _readingPaused || !_pageFlipActive)
            {
                return 0;
            }

            var elapsed = (Now - _pageFlipStart).TotalSeconds;
            if (elapsed < 0.14)
            {
                return 1;
            }

            if (elapsed < 0.34)
            {
                return 2;
            }

            return elapsed < 0.52 ? 3 : 0;
        }
    }

    public event Action? StateChanged;

    public void UpdateOptions(BehaviorOptions options)
    {
        _options = options;
        _animations.SetLoopOverlay(
            "zzz",
            _options.EffectsEnabled && State == PetBehaviorState.Sleep);
        if (State == PetBehaviorState.Idle)
        {
            ScheduleIdle(Now);
        }
    }

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

        if (_pageFlipActive && (now - _pageFlipStart).TotalSeconds > 0.7)
        {
            _pageFlipActive = false;
        }

        if (State == PetBehaviorState.Idle && _animations.CurrentBaseClip == "idle.breath")
        {
            if (now >= _nextBlink)
            {
                _animations.PlayOverlay("idle.blink");
                _nextBlink = now + Seconds(Range(_config.Idle.BlinkMinSeconds, _config.Idle.BlinkMaxSeconds));
            }

            var sleepAfter = TimeSpan.FromMinutes(Math.Max(1, _options.SleepAfterIdleMinutes));
            if (now - _lastInteraction >= sleepAfter)
            {
                Sleep();
                return;
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
        else if (State == PetBehaviorState.Reading &&
                 !_readingPaused &&
                 _animations.CurrentBaseClip == "read.breath")
        {
            if (now >= _nextReadAction)
            {
                _nextReadAction = now + Seconds(Range(
                    _config.Reading.MicroMinSeconds, _config.Reading.MicroMaxSeconds));
                _animations.PlayBase("read.glance", "read.breath");
                Log.Info("behavior: reading glance");
            }

            if (now >= _nextPageFlip)
            {
                _nextPageFlip = now + Seconds(Range(
                    _config.Reading.PageMinSeconds, _config.Reading.PageMaxSeconds));
                _pageFlipStart = now;
                _pageFlipActive = true;
                Log.Info("behavior: page turn");
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
        if (clip == "drag.land" && _pendingTimerReaction is { } pending)
        {
            _pendingTimerReaction = null;
            _animations.PlayBase(pending, CurrentRestClip());
        }
    }

    public void SetFocusMode(bool focus)
    {
        _focusMode = focus;
        _lastInteraction = Now;

        if (focus)
        {
            if (State == PetBehaviorState.Sleep)
            {
                Wake();
            }
            else if (State == PetBehaviorState.Idle)
            {
                StartReading();
            }
        }
        else if (State == PetBehaviorState.Reading)
        {
            StopReading();
        }
    }

    public void SetReadingPaused(bool paused)
    {
        _readingPaused = paused;
        if (State != PetBehaviorState.Reading)
        {
            return;
        }

        _lastInteraction = Now;
        _pageFlipActive = false;
        _animations.PlayBase(paused ? "read.pause" : "read.breath");
        if (!paused)
        {
            ScheduleReading(Now);
        }

        Log.Info(paused ? "behavior: reading paused" : "behavior: reading resumed");
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
        _animations.SetLoopOverlay("zzz", false);
        if (_focusMode)
        {
            SetState(PetBehaviorState.Reading);
            _pageFlipActive = false;
            _animations.PlayBase(clip, CurrentRestClip());
            if (!_readingPaused)
            {
                ScheduleReading(Now);
            }
        }
        else
        {
            SetState(PetBehaviorState.Idle);
            _animations.PlayBase(clip, "idle.breath");
            ScheduleIdle(Now);
        }

        _lastInteraction = Now;
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

        _animations.PlayBase("react.pet", CurrentRestClip());
        if (_options.EffectsEnabled)
        {
            _animations.PlayOverlay("hearts");
        }

        Log.Info("behavior: pet");
    }

    public void ToggleSleep()
    {
        switch (State)
        {
            case PetBehaviorState.Sleep:
                Wake();
                break;
            case PetBehaviorState.Idle:
            case PetBehaviorState.Reading:
                Sleep();
                break;
        }
    }

    public void BeginDrag()
    {
        _animations.SetLoopOverlay("zzz", false);
        SetState(PetBehaviorState.Drag);
        _pageFlipActive = false;
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
        _pageFlipActive = false;
        _lastInteraction = Now;
        if (_focusMode)
        {
            SetState(PetBehaviorState.Reading);
            _animations.PlayBase("drag.land", CurrentRestClip());
            if (!_readingPaused)
            {
                ScheduleReading(Now);
            }

            Log.Info("behavior: landed (reading)");
            return;
        }

        SetState(PetBehaviorState.Idle);
        _animations.PlayBase("drag.land", "idle.breath");
        ScheduleIdle(Now);
        Log.Info("behavior: landed");
    }

    private void StartReading()
    {
        _animations.SetLoopOverlay("zzz", false);
        SetState(PetBehaviorState.Reading);
        _pageFlipActive = false;
        _animations.PlayBase(CurrentRestClip());
        if (!_readingPaused)
        {
            ScheduleReading(Now);
        }

        _lastInteraction = Now;
        Log.Info("behavior: reading");
    }

    private void StopReading()
    {
        if (State != PetBehaviorState.Reading)
        {
            return;
        }

        _pageFlipActive = false;
        SetState(PetBehaviorState.Idle);
        _animations.PlayBase("idle.breath");
        _lastInteraction = Now;
        ScheduleIdle(Now);
        Log.Info("behavior: reading stopped");
    }

    private void Sleep()
    {
        if (State is PetBehaviorState.Drag or PetBehaviorState.Fall)
        {
            return;
        }

        _pageFlipActive = false;
        SetState(PetBehaviorState.Sleep);
        _animations.SetLoopOverlay("zzz", _options.EffectsEnabled);
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
        if (_focusMode)
        {
            SetState(PetBehaviorState.Reading);
            _animations.PlayBase("sleep.wake", CurrentRestClip());
            if (!_readingPaused)
            {
                ScheduleReading(Now);
            }

            Log.Info("behavior: wake (reading)");
        }
        else
        {
            SetState(PetBehaviorState.Idle);
            _animations.PlayBase("sleep.wake", "idle.breath");
            ScheduleIdle(Now);
            Log.Info("behavior: wake");
        }

        _lastInteraction = Now;
    }

    private string CurrentRestClip() =>
        State == PetBehaviorState.Reading || _focusMode
            ? _readingPaused ? "read.pause" : "read.breath"
            : "idle.breath";

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
    }

    private void ScheduleReading(TimeSpan now)
    {
        _nextReadAction = now + Seconds(Range(
            _config.Reading.MicroMinSeconds, _config.Reading.MicroMaxSeconds));
        _nextPageFlip = now + Seconds(Range(
            _config.Reading.PageMinSeconds, _config.Reading.PageMaxSeconds));
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
