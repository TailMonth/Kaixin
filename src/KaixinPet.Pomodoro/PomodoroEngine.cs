using System.Diagnostics;

namespace KaixinPet.Pomodoro;

public sealed class PomodoroEngine : IPomodoroService
{
    private PomodoroOptions _options;
    private readonly Func<TimeSpan> _clock;
    private readonly Stopwatch _stopwatch = new();

    private PomodoroPhase _phase = PomodoroPhase.Idle;
    private PomodoroRunState _runState = PomodoroRunState.Stopped;
    private TimeSpan _total;
    private TimeSpan _remaining;
    private TimeSpan _remainingAtAnchor;
    private TimeSpan _anchor;
    private int _completedFocusInCycle;
    private string? _pauseReason;
    private bool _completing;

    public PomodoroEngine(PomodoroOptions options, Func<TimeSpan>? clock = null)
    {
        _options = Normalize(options);
        _clock = clock ?? DefaultClock;
    }

    public PomodoroSnapshot Snapshot => new(
        _phase,
        _runState,
        _remaining,
        _total,
        _completedFocusInCycle,
        _options.RoundsPerLongBreak,
        _pauseReason);

    public event EventHandler<PomodoroSnapshot>? StateChanged;

    public event EventHandler<PhaseTransition>? PhaseChanged;

    public void UpdateOptions(PomodoroOptions options)
    {
        _options = Normalize(options);
        RaiseStateChanged();
    }

    public void StartFocus()
    {
        if (_phase != PomodoroPhase.Idle)
        {
            return;
        }

        BeginPhase(PomodoroPhase.Focus, FocusDuration(), PhaseTransitionReason.UserStarted);
    }

    public void StartBreak(bool isLong)
    {
        if (_phase != PomodoroPhase.Idle)
        {
            return;
        }

        BeginPhase(
            isLong ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak,
            isLong ? LongBreakDuration() : ShortBreakDuration(),
            PhaseTransitionReason.UserStarted);
    }

    public void Pause(string reason = "User")
    {
        if (_runState != PomodoroRunState.Running)
        {
            return;
        }

        var remaining = GetRemainingAtNow();
        if (remaining <= TimeSpan.Zero)
        {
            CompletePhase();
            return;
        }

        _remaining = remaining;
        _remainingAtAnchor = remaining;
        _runState = PomodoroRunState.Paused;
        _pauseReason = reason;
        RaiseStateChanged();
    }

    public void Resume()
    {
        if (_runState != PomodoroRunState.Paused)
        {
            return;
        }

        _anchor = _clock();
        _remainingAtAnchor = _remaining;
        _runState = PomodoroRunState.Running;
        _pauseReason = null;
        RaiseStateChanged();
    }

    public void Skip()
    {
        if (_phase == PomodoroPhase.Idle)
        {
            return;
        }

        if (_runState == PomodoroRunState.Running)
        {
            var remaining = GetRemainingAtNow();
            if (remaining <= TimeSpan.Zero)
            {
                CompletePhase();
                return;
            }
        }

        // 跳过专注不计入完成数；跳过休息不自动开始专注。
        ToIdle(_phase, PhaseTransitionReason.Skipped);
    }

    public void Reset()
    {
        var from = _phase;
        _completedFocusInCycle = 0;
        _phase = PomodoroPhase.Idle;
        _runState = PomodoroRunState.Stopped;
        _total = TimeSpan.Zero;
        _remaining = TimeSpan.Zero;
        _remainingAtAnchor = TimeSpan.Zero;
        _pauseReason = null;
        RaisePhaseChanged(from, PomodoroPhase.Idle, PomodoroRunState.Stopped, PhaseTransitionReason.Reset);
        RaiseStateChanged();
    }

    public void Tick()
    {
        if (_runState != PomodoroRunState.Running || _phase == PomodoroPhase.Idle)
        {
            return;
        }

        var remaining = GetRemainingAtNow();
        if (remaining > TimeSpan.Zero)
        {
            _remaining = remaining;
            return;
        }

        CompletePhase();
    }

    private void BeginPhase(PomodoroPhase phase, TimeSpan total, PhaseTransitionReason reason)
    {
        var from = _phase;
        _phase = phase;
        _total = total;
        _remaining = total;
        _remainingAtAnchor = total;
        _anchor = _clock();
        _runState = PomodoroRunState.Running;
        _pauseReason = null;

        RaisePhaseChanged(from, phase, PomodoroRunState.Running, reason);
        RaiseStateChanged();
    }

    private void CompletePhase()
    {
        if (_completing)
        {
            return;
        }

        _completing = true;
        try
        {
            var from = _phase;
            _remaining = TimeSpan.Zero;

            if (from == PomodoroPhase.Focus)
            {
                _completedFocusInCycle++;
                var isLongBreak = _completedFocusInCycle % _options.RoundsPerLongBreak == 0;
                if (isLongBreak)
                {
                    _completedFocusInCycle = 0;
                }

                if (_options.AutoStartBreaks)
                {
                    var to = isLongBreak ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
                    BeginPhase(to, DurationFor(to), PhaseTransitionReason.Completed);
                }
                else
                {
                    ToIdle(from, PhaseTransitionReason.Completed);
                }
            }
            else if (from is PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak)
            {
                if (_options.AutoStartFocus)
                {
                    BeginPhase(PomodoroPhase.Focus, FocusDuration(), PhaseTransitionReason.Completed);
                }
                else
                {
                    ToIdle(from, PhaseTransitionReason.Completed);
                }
            }
        }
        finally
        {
            _completing = false;
        }
    }

    private void ToIdle(PomodoroPhase from, PhaseTransitionReason reason)
    {
        _phase = PomodoroPhase.Idle;
        _runState = PomodoroRunState.Stopped;
        _total = TimeSpan.Zero;
        _remaining = TimeSpan.Zero;
        _remainingAtAnchor = TimeSpan.Zero;
        _pauseReason = null;
        RaisePhaseChanged(from, PomodoroPhase.Idle, PomodoroRunState.Stopped, reason);
        RaiseStateChanged();
    }

    private TimeSpan GetRemainingAtNow()
    {
        if (_runState != PomodoroRunState.Running)
        {
            return _remaining;
        }

        var remaining = _remainingAtAnchor - (_clock() - _anchor);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, Snapshot);

    private void RaisePhaseChanged(
        PomodoroPhase from, PomodoroPhase to, PomodoroRunState runState, PhaseTransitionReason reason)
    {
        PhaseChanged?.Invoke(
            this,
            new PhaseTransition(from, to, runState, reason, _completedFocusInCycle));
    }

    private TimeSpan FocusDuration() => TimeSpan.FromMinutes(_options.FocusMinutes);

    private TimeSpan ShortBreakDuration() => TimeSpan.FromMinutes(_options.ShortBreakMinutes);

    private TimeSpan LongBreakDuration() => TimeSpan.FromMinutes(_options.LongBreakMinutes);

    private TimeSpan DurationFor(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Focus => FocusDuration(),
        PomodoroPhase.ShortBreak => ShortBreakDuration(),
        PomodoroPhase.LongBreak => LongBreakDuration(),
        _ => TimeSpan.Zero
    };

    private TimeSpan DefaultClock()
    {
        if (!_stopwatch.IsRunning)
        {
            _stopwatch.Start();
        }

        return _stopwatch.Elapsed;
    }

    private static PomodoroOptions Normalize(PomodoroOptions options)
    {
        var normalized = options.Clone();
        normalized.FocusMinutes = Math.Clamp(normalized.FocusMinutes, 1, 180);
        normalized.ShortBreakMinutes = Math.Clamp(normalized.ShortBreakMinutes, 1, 60);
        normalized.LongBreakMinutes = Math.Clamp(normalized.LongBreakMinutes, 1, 60);
        normalized.RoundsPerLongBreak = Math.Clamp(normalized.RoundsPerLongBreak, 1, 12);
        return normalized;
    }
}
