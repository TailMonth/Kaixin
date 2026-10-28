namespace KaixinPet.Pomodoro;

public enum PomodoroPhase
{
    Idle,
    Focus,
    ShortBreak,
    LongBreak
}

public enum PomodoroRunState
{
    Stopped,
    Running,
    Paused
}

public enum PhaseTransitionReason
{
    UserStarted,
    Completed,
    Skipped,
    Reset
}

public sealed record PomodoroSnapshot(
    PomodoroPhase Phase,
    PomodoroRunState RunState,
    TimeSpan Remaining,
    TimeSpan Total,
    int CompletedFocusInCycle,
    int RoundsPerLongBreak,
    string? PauseReason)
{
    public bool IsActive => Phase != PomodoroPhase.Idle;

    public bool IsFocus => Phase == PomodoroPhase.Focus;

    public bool IsBreak => Phase is PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak;
}

public sealed record PhaseTransition(
    PomodoroPhase From,
    PomodoroPhase To,
    PomodoroRunState RunState,
    PhaseTransitionReason Reason,
    int CompletedFocusInCycle);

public sealed class PomodoroOptions
{
    public int FocusMinutes { get; set; } = 25;

    public int ShortBreakMinutes { get; set; } = 5;

    public int LongBreakMinutes { get; set; } = 15;

    public int RoundsPerLongBreak { get; set; } = 4;

    public bool AutoStartBreaks { get; set; } = true;

    public bool AutoStartFocus { get; set; }

    public PomodoroOptions Clone() => new()
    {
        FocusMinutes = FocusMinutes,
        ShortBreakMinutes = ShortBreakMinutes,
        LongBreakMinutes = LongBreakMinutes,
        RoundsPerLongBreak = RoundsPerLongBreak,
        AutoStartBreaks = AutoStartBreaks,
        AutoStartFocus = AutoStartFocus
    };
}

public interface IPomodoroService
{
    PomodoroSnapshot Snapshot { get; }

    event EventHandler<PomodoroSnapshot>? StateChanged;

    event EventHandler<PhaseTransition>? PhaseChanged;

    void StartFocus();

    void StartBreak(bool isLong);

    void Pause(string reason = "User");

    void Resume();

    void Skip();

    void Reset();
}
