using KaixinPet.Pomodoro;

var tests = new List<(string Name, Action Body)>
{
    ("start and countdown", StartAndCountdown),
    ("pause and resume", PauseAndResume),
    ("focus completes into short break", FocusIntoShortBreak),
    ("auto focus off returns to idle", AutoFocusOffReturnsIdle),
    ("auto focus on resumes focus", AutoFocusOnResumesFocus),
    ("fourth focus enters long break and resets cycle", LongBreakCycle),
    ("skip focus does not count", SkipFocusDoesNotCount),
    ("skip break ignores auto focus", SkipBreakIgnoresAutoFocus),
    ("reset clears cycle count", ResetClearsCycle),
    ("suspend pause reason", SuspendPauseReason),
    ("update options takes effect next phase", UpdateOptionsNextPhase)
};

var failures = 0;

foreach (var (name, body) in tests)
{
    try
    {
        body();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"FAIL  {name}: {exception.Message}");
    }
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

static void StartAndCountdown()
{
    var (engine, clock) = Create(autoStartBreaks: true, autoStartFocus: false);
    engine.StartFocus();
    Assert(engine.Snapshot.Phase == PomodoroPhase.Focus, "phase focus");
    Assert(engine.Snapshot.RunState == PomodoroRunState.Running, "running");
    Assert(engine.Snapshot.Remaining == TimeSpan.FromMinutes(25), "25 minutes remaining");

    clock.Advance(TimeSpan.FromMinutes(10));
    engine.Tick();
    Assert(engine.Snapshot.Remaining == TimeSpan.FromMinutes(15), "15 minutes remaining");
}

static void PauseAndResume()
{
    var (engine, clock) = Create(autoStartBreaks: true, autoStartFocus: false);
    engine.StartFocus();
    clock.Advance(TimeSpan.FromMinutes(10));
    engine.Tick();

    engine.Pause();
    Assert(engine.Snapshot.RunState == PomodoroRunState.Paused, "paused");
    Assert(engine.Snapshot.PauseReason == "User", "pause reason user");

    clock.Advance(TimeSpan.FromMinutes(30));
    engine.Tick();
    Assert(engine.Snapshot.Remaining == TimeSpan.FromMinutes(15), "remaining frozen while paused");

    engine.Resume();
    Assert(engine.Snapshot.RunState == PomodoroRunState.Running, "resumed");
    clock.Advance(TimeSpan.FromMinutes(15));
    engine.Tick();
    Assert(engine.Snapshot.Phase == PomodoroPhase.ShortBreak, "short break after resume");
}

static void FocusIntoShortBreak()
{
    var (engine, clock) = Create(autoStartBreaks: true, autoStartFocus: false);
    engine.StartFocus();
    clock.Advance(TimeSpan.FromMinutes(25));
    engine.Tick();

    Assert(engine.Snapshot.Phase == PomodoroPhase.ShortBreak, "short break");
    Assert(engine.Snapshot.RunState == PomodoroRunState.Running, "break auto started");
    Assert(engine.Snapshot.CompletedFocusInCycle == 1, "one focus completed");

    clock.Advance(TimeSpan.FromMinutes(5));
    engine.Tick();
    Assert(engine.Snapshot.Phase == PomodoroPhase.Idle, "idle after break");
    Assert(engine.Snapshot.RunState == PomodoroRunState.Stopped, "stopped after break");
}

static void AutoFocusOffReturnsIdle()
{
    var (engine, clock) = Create(autoStartBreaks: true, autoStartFocus: false);
    engine.StartFocus();
    clock.Advance(TimeSpan.FromMinutes(25));
    engine.Tick();
    clock.Advance(TimeSpan.FromMinutes(5));
    engine.Tick();
    Assert(engine.Snapshot.Phase == PomodoroPhase.Idle, "auto focus off -> idle");
}

static void AutoFocusOnResumesFocus()
{
    var (engine, clock) = Create(autoStartBreaks: true, autoStartFocus: true);
    engine.StartFocus();
    clock.Advance(TimeSpan.FromMinutes(25));
    engine.Tick();
    clock.Advance(TimeSpan.FromMinutes(5));
    engine.Tick();

    Assert(engine.Snapshot.Phase == PomodoroPhase.Focus, "auto focus on -> focus");
    Assert(engine.Snapshot.RunState == PomodoroRunState.Running, "focus running");
    Assert(engine.Snapshot.CompletedFocusInCycle == 1, "cycle count preserved");
}

static void LongBreakCycle()
{
    var (engine, clock) = Create(autoStartBreaks: true, autoStartFocus: true);
    engine.StartFocus();

    for (var round = 1; round <= 4; round++)
    {
        clock.Advance(TimeSpan.FromMinutes(25));
        engine.Tick();

        if (round < 4)
        {
            Assert(engine.Snapshot.Phase == PomodoroPhase.ShortBreak, $"round {round} short break");
            Assert(engine.Snapshot.CompletedFocusInCycle == round, $"round {round} count");
            clock.Advance(TimeSpan.FromMinutes(5));
            engine.Tick();
            Assert(engine.Snapshot.Phase == PomodoroPhase.Focus, $"round {round} auto focus");
        }
    }

    Assert(engine.Snapshot.Phase == PomodoroPhase.LongBreak, "long break after four");
    Assert(engine.Snapshot.Total == TimeSpan.FromMinutes(15), "15 minute long break");
    Assert(engine.Snapshot.CompletedFocusInCycle == 0, "cycle reset on long break");

    clock.Advance(TimeSpan.FromMinutes(15));
    engine.Tick();
    Assert(engine.Snapshot.Phase == PomodoroPhase.Focus, "auto focus after long break");
}

static void SkipFocusDoesNotCount()
{
    var (engine, _) = Create(autoStartBreaks: true, autoStartFocus: false);
    engine.StartFocus();
    engine.Skip();

    Assert(engine.Snapshot.Phase == PomodoroPhase.Idle, "idle after skip focus");
    Assert(engine.Snapshot.RunState == PomodoroRunState.Stopped, "stopped");
    Assert(engine.Snapshot.CompletedFocusInCycle == 0, "skip not counted");
}

static void SkipBreakIgnoresAutoFocus()
{
    var (engine, clock) = Create(autoStartBreaks: true, autoStartFocus: true);
    engine.StartFocus();
    clock.Advance(TimeSpan.FromMinutes(25));
    engine.Tick();
    Assert(engine.Snapshot.Phase == PomodoroPhase.ShortBreak, "break phase");

    engine.Skip();
    Assert(engine.Snapshot.Phase == PomodoroPhase.Idle, "skip break idle");
    Assert(engine.Snapshot.RunState == PomodoroRunState.Stopped, "skip break no auto focus");
}

static void ResetClearsCycle()
{
    var (engine, clock) = Create(autoStartBreaks: true, autoStartFocus: false);
    engine.StartFocus();
    clock.Advance(TimeSpan.FromMinutes(25));
    engine.Tick();
    Assert(engine.Snapshot.CompletedFocusInCycle == 1, "one completed before reset");

    engine.Reset();
    Assert(engine.Snapshot.Phase == PomodoroPhase.Idle, "idle after reset");
    Assert(engine.Snapshot.CompletedFocusInCycle == 0, "reset clears count");
}

static void SuspendPauseReason()
{
    var (engine, clock) = Create(autoStartBreaks: true, autoStartFocus: false);
    engine.StartFocus();
    clock.Advance(TimeSpan.FromMinutes(3));
    engine.Tick();
    engine.Pause("SystemSuspend");

    Assert(engine.Snapshot.RunState == PomodoroRunState.Paused, "paused by suspend");
    Assert(engine.Snapshot.PauseReason == "SystemSuspend", "suspend reason");
    Assert(engine.Snapshot.Phase == PomodoroPhase.Focus, "focus phase retained");
    Assert(engine.Snapshot.Remaining == TimeSpan.FromMinutes(22), "remaining retained");
}

static void UpdateOptionsNextPhase()
{
    var (engine, clock) = Create(autoStartBreaks: true, autoStartFocus: false);
    engine.StartFocus();
    clock.Advance(TimeSpan.FromMinutes(10));
    engine.Tick();

    engine.UpdateOptions(new PomodoroOptions
    {
        FocusMinutes = 50,
        ShortBreakMinutes = 7,
        LongBreakMinutes = 20,
        RoundsPerLongBreak = 3,
        AutoStartBreaks = true,
        AutoStartFocus = false
    });

    Assert(engine.Snapshot.Total == TimeSpan.FromMinutes(25), "running phase keeps its total");
    clock.Advance(TimeSpan.FromMinutes(15));
    engine.Tick();
    Assert(engine.Snapshot.Phase == PomodoroPhase.ShortBreak, "short break after focus");
    Assert(engine.Snapshot.Total == TimeSpan.FromMinutes(7), "next phase uses updated duration");
    Assert(engine.Snapshot.RoundsPerLongBreak == 3, "rounds updated");
}

static (PomodoroEngine Engine, ManualClock Clock) Create(bool autoStartBreaks, bool autoStartFocus)
{
    var clock = new ManualClock();
    var engine = new PomodoroEngine(
        new PomodoroOptions
        {
            FocusMinutes = 25,
            ShortBreakMinutes = 5,
            LongBreakMinutes = 15,
            RoundsPerLongBreak = 4,
            AutoStartBreaks = autoStartBreaks,
            AutoStartFocus = autoStartFocus
        },
        () => clock.Now);
    return (engine, clock);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class ManualClock
{
    public TimeSpan Now { get; private set; }

    public void Advance(TimeSpan delta) => Now += delta;
}
