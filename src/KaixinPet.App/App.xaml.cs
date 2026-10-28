using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using KaixinPet.App.Tray;
using KaixinPet.App.Ui;
using KaixinPet.Behavior;
using KaixinPet.Core;
using KaixinPet.Core.Diagnostics;
using KaixinPet.Core.Logging;
using KaixinPet.Core.Settings;
using KaixinPet.Pet;
using KaixinPet.Pet.Assets;
using KaixinPet.Pet.Native;
using KaixinPet.Pet.Scene;
using KaixinPet.Pomodoro;

namespace KaixinPet.App;

public partial class App : Application
{
    private const double BenchmarkFps = 24;

    private static readonly Stopwatch StartupClock = Stopwatch.StartNew();

    private readonly ProcessMetrics _metrics = new();
    private readonly Stopwatch _hourglassClock = new();

    private SingleInstanceGuard? _singleInstance;
    private SettingsService? _settings;
    private AssetCatalog? _assets;
    private PetScene? _scene;
    private PetWindow? _pet;
    private TimeTagWindow? _tag;
    private HoverControlsWindow? _hover;
    private TrayService? _tray;
    private PomodoroEngine? _pomodoro;
    private PomodoroOptions? _pomodoroOptions;

    private DispatcherTimer? _uiTimer;
    private DispatcherTimer? _pomodoroTimer;
    private DispatcherTimer? _statsTimer;
    private DispatcherTimer? _hourglassTimer;
    private DispatcherTimer? _completionTimer;

    private string _petName = "开心";
    private bool _tagEnabled = true;
    private bool _hoverControlsEnabled = true;
    private bool _notificationsEnabled = true;
    private bool _fullscreenHidden;
    private bool _userHidden;
    private bool _isDragging;
    private bool _completionShown;
    private bool _suspendPaused;
    private int _lastTagSecond = -1;
    private PomodoroRunState _lastRunState = PomodoroRunState.Stopped;
    private DateTime _hoverEnteredAt;
    private DateTime _hoverLeftAt;
    private bool _hoverShown;
    private HourglassAnimationKind _hourglassAnimation;
    private double _hourglassFromAlpha;
    private double _hourglassToAlpha;
    private double _hourglassDuration;
    private Action? _hourglassMidpoint;
    private Action? _hourglassCompleted;
    private bool _hourglassMidpointDone;

    private long _lastCommitCount;
    private long _lastDroppedCount;
    private int _normalTickCount;
    private BenchmarkState? _benchmark;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Log.Error("未捕获异常", exception);
            }
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.EnsureDataDirectories();
        Log.Initialize(AppPaths.LogsDirectory);

        var options = CommandLineOptions.Parse(e.Args);
        Log.Info(
            $"KaixinPet {GetVersion()} starting; os={Environment.OSVersion.VersionString}; " +
            $"cores={Environment.ProcessorCount}; processAge={SafeProcessAgeMs():0}ms");
        Log.Info(
            $"data={AppPaths.DataRoot}; portable={AppPaths.IsPortable}; assets={AppPaths.AssetsRoot}");
        Log.Info($"args: benchmark={options.BenchmarkSeconds}s, startFocus={options.StartFocus}");

        _singleInstance = new SingleInstanceGuard();
        if (!_singleInstance.IsFirstInstance)
        {
            Log.Info("another instance is already running; signaling it and exiting");
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown(0);
            return;
        }

        _singleInstance.ShowRequested += () =>
            Dispatcher.BeginInvoke(new Action(ShowPetFromExternalRequest));

        try
        {
            _settings = new SettingsService(AppPaths.DataRoot);
            _settings.Load();
            _petName = _settings.Current.General.PetName;
            _tagEnabled = _settings.Current.Pomodoro.ShowHourglassTime;
            _hoverControlsEnabled = _settings.Current.Pomodoro.ShowHoverControls;
            _notificationsEnabled = _settings.Current.Pomodoro.ShowNotifications;

            _pomodoroOptions = new PomodoroOptions
            {
                FocusMinutes = _settings.Current.Pomodoro.FocusMinutes,
                ShortBreakMinutes = _settings.Current.Pomodoro.ShortBreakMinutes,
                LongBreakMinutes = _settings.Current.Pomodoro.LongBreakMinutes,
                RoundsPerLongBreak = _settings.Current.Pomodoro.RoundsPerLongBreak,
                AutoStartBreaks = _settings.Current.Pomodoro.AutoStartBreaks,
                AutoStartFocus = _settings.Current.Pomodoro.AutoStartFocus
            };
            _pomodoro = new PomodoroEngine(_pomodoroOptions);
            _pomodoro.PhaseChanged += OnPomodoroPhaseChanged;

            _assets = AssetCatalog.Load(AppPaths.AssetsRoot);
            _scene = new PetScene();
            _tag = new TimeTagWindow();
            _hover = new HoverControlsWindow();
            _hover.PauseResumeRequested += TogglePauseResume;
            _hover.SkipRequested += () => _pomodoro?.Skip();
            _hover.SettingsRequested += () => OpenFolder(AppPaths.DataRoot);
            var behaviorConfig = BehaviorConfig.Load(AppPaths.AssetsRoot);
            var behaviorOptions = new BehaviorOptions
            {
                SleepAfterIdleMinutes = _settings.Current.Behavior.SleepAfterIdleMinutes,
                PlaySessionMaxSeconds = _settings.Current.Behavior.PlaySessionMaxSeconds,
                SpontaneousPlay = _settings.Current.Behavior.SpontaneousPlay,
                GroundYDip = SceneLayout.GroundYDip,
                WindowWidthDip = SceneLayout.WindowWidthDip,
                CatCenterXDip = SceneLayout.CatCenterXDip
            };
            _pet = new PetWindow(
                _assets,
                _scene,
                StartupClock,
                _petName,
                _settings.Current.General.AlwaysOnTop,
                behaviorOptions,
                behaviorConfig,
                elapsedMs => Log.Info(
                    $"startup: first frame at {SafeProcessAgeMs():0} ms from process start " +
                    $"(managed clock {elapsedMs:0} ms)"));

            WirePetEvents();
            _pet.SetPomodoroStatus(_pomodoro.Snapshot, _pomodoroOptions.FocusMinutes, _pomodoroOptions.ShortBreakMinutes);
            _pet.Show(GetRestorePosition());
            _tag.SetTopmost(_settings.Current.General.AlwaysOnTop);

            try
            {
                _tray = new TrayService(BuildTrayStatus());
                WireTrayEvents();
                UpdateTray();
            }
            catch (Exception trayException)
            {
                Log.Error("托盘初始化失败；应用继续以桌宠菜单运行", trayException);
                _tray = null;
            }

            StartTimers();

            if (_settings.LoadIssue is not null && options.BenchmarkSeconds == 0)
            {
                _tray?.ShowBalloon("设置已恢复默认", "设置文件损坏，已备份到数据目录。", warning: true);
            }

            if (options.BenchmarkSeconds > 0)
            {
                StartBenchmark(options.BenchmarkSeconds);
            }
            else if (options.StartFocus)
            {
                StartFocusFromUser(showPet: true);
            }
        }
        catch (Exception exception)
        {
            Log.Error("启动失败", exception);
            MessageBox.Show(
                $"开心启动失败：{exception.Message}\n\n日志目录：{AppPaths.LogsDirectory}",
                "开心",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void WirePetEvents()
    {
        if (_pet is null)
        {
            return;
        }

        _pet.StartFocusRequested += () => StartFocusFromUser(showPet: false);
        _pet.StartBreakRequested += () => StartBreakFromUser();
        _pet.PauseResumeRequested += TogglePauseResume;
        _pet.SkipTimerRequested += () => _pomodoro?.Skip();
        _pet.ResetTimerRequested += () => _pomodoro?.Reset();
        _pet.HotkeyPressed += ToggleTimerFromHotkey;
        _pet.SystemSuspending += OnSystemSuspending;
        _pet.SystemResumed += OnSystemResumed;
        _pet.ExitRequested += RequestExit;
        _pet.OpenLogsRequested += () => OpenFolder(AppPaths.LogsDirectory);
        _pet.HideRequested += HidePet;
        _pet.BoundsChanged += OnPetBoundsChanged;
        _pet.PositionChanged += OnPetPositionChanged;
        _pet.DragStateChanged += OnPetDragStateChanged;
        _pet.VisibilityChanged += OnPetVisibilityChanged;
        _pet.TopmostChanged += OnPetTopmostChanged;
    }

    private void WireTrayEvents()
    {
        if (_tray is null)
        {
            return;
        }

        _tray.ShowRequested += ShowPetAndHighlight;
        _tray.ToggleVisibilityRequested += TogglePetVisibility;
        _tray.StartFocusRequested += () => StartFocusFromUser(showPet: true);
        _tray.StartBreakRequested += () => StartBreakFromUser();
        _tray.PauseResumeRequested += TogglePauseResume;
        _tray.SkipTimerRequested += () => _pomodoro?.Skip();
        _tray.ResetTimerRequested += () => _pomodoro?.Reset();
        _tray.PlayRequested += TogglePlayWithFocusGuard;
        _tray.SleepToggleRequested += ToggleSleep;
        _tray.TopmostToggleRequested += topmost => _pet?.SetTopmost(topmost);
        _tray.ResetPositionRequested += () => _pet?.ResetPosition();
        _tray.OpenDataRequested += () => OpenFolder(AppPaths.DataRoot);
        _tray.OpenLogsRequested += () => OpenFolder(AppPaths.LogsDirectory);
        _tray.ExitRequested += RequestExit;
    }

    private void StartFocusFromUser(bool showPet)
    {
        if (_pomodoro is null || _pomodoro.Snapshot.Phase != PomodoroPhase.Idle)
        {
            return;
        }

        if (showPet && _pet is { IsWindowVisible: false })
        {
            ShowPetAndHighlight();
        }

        _pomodoro.StartFocus();
        Log.Info("pomodoro: user started focus");
    }

    private void StartBreakFromUser()
    {
        if (_pomodoro is null || _pomodoro.Snapshot.Phase != PomodoroPhase.Idle)
        {
            return;
        }

        if (_pet is { IsWindowVisible: false })
        {
            ShowPetAndHighlight();
        }

        _pomodoro.StartBreak(isLong: false);
        Log.Info("pomodoro: user started short break");
    }

    private void TogglePauseResume()
    {
        if (_pomodoro is null)
        {
            return;
        }

        var snapshot = _pomodoro.Snapshot;
        if (snapshot.Phase == PomodoroPhase.Idle)
        {
            StartFocusFromUser(showPet: true);
        }
        else if (snapshot.RunState == PomodoroRunState.Running)
        {
            _pomodoro.Pause("User");
        }
        else if (snapshot.RunState == PomodoroRunState.Paused)
        {
            _pomodoro.Resume();
        }
    }

    private void ToggleTimerFromHotkey()
    {
        if (_pet is { IsWindowVisible: false } && _pomodoro?.Snapshot.Phase == PomodoroPhase.Idle)
        {
            ShowPetAndHighlight();
        }

        TogglePauseResume();
    }

    private void TogglePlayWithFocusGuard()
    {
        if (_pet is null)
        {
            return;
        }

        if (_pet.IsPlaying)
        {
            _pet.StopPlay();
            UpdateTray();
            return;
        }

        var snapshot = _pomodoro?.Snapshot;
        if (snapshot is { Phase: PomodoroPhase.Focus, RunState: not PomodoroRunState.Stopped })
        {
            var result = MessageBox.Show(
                "玩毛线球会打扰专注，继续吗？",
                _petName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        if (!_pet.IsWindowVisible)
        {
            ShowPetAndHighlight();
        }

        _pet.StartPlay();
        UpdateTray();
    }

    private void ToggleSleep()
    {
        if (_pet is null)
        {
            return;
        }

        if (!_pet.IsWindowVisible)
        {
            ShowPetAndHighlight();
        }

        _pet.ToggleSleep();
        UpdateTray();
    }

    private void OnPomodoroPhaseChanged(object? sender, PhaseTransition transition)
    {
        if (_pomodoro is null || _scene is null || _pet is null)
        {
            return;
        }

        Log.Info(
            $"pomodoro phase: {transition.From} -> {transition.To} " +
            $"reason={transition.Reason} run={transition.RunState} " +
            $"completed={transition.CompletedFocusInCycle}");

        var snapshot = _pomodoro.Snapshot;
        _lastRunState = snapshot.RunState;
        CancelCompletionDisplay();
        _pet.SetPomodoroStatus(
            snapshot,
            _pomodoroOptions?.FocusMinutes ?? 25,
            _pomodoroOptions?.ShortBreakMinutes ?? 5);
        _pet.SetFocusMode(
            snapshot.Phase == PomodoroPhase.Focus,
            _settings?.Current.Behavior.FocusQuietAfterMinutes ?? 2);

        var toFocus = snapshot.Phase == PomodoroPhase.Focus;
        var toBreak = snapshot.Phase is PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak;

        if (toFocus)
        {
            _scene.SetSandBreak(false);
            _scene.SetProgress(0);
            if (transition.From == PomodoroPhase.Idle)
            {
                _scene.SetTimerVisible(true);
                StartHourglassAnimation(
                    HourglassAnimationKind.Fade,
                    0, 1, 0.45,
                    midpoint: null,
                    completed: null);
                _pet.PlayTimerReaction("play.pounce");
            }
            else
            {
                _scene.SetTimerVisible(true);
                StartHourglassAnimation(
                    HourglassAnimationKind.Flip,
                    1, 1, 0.36,
                    midpoint: () =>
                    {
                        _scene.SetSandBreak(false);
                        _scene.SetProgress(0);
                    },
                    completed: null);
                _pet.PlayTimerReaction("play.pounce");
            }

            if (transition.From is PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak)
            {
                Notify("休息结束", "开始下一个番茄？");
            }

            ShowTagForActivePhase(snapshot);
        }
        else if (toBreak)
        {
            _scene.SetTimerVisible(true);
            _scene.SetProgress(0);

            if (transition.From == PomodoroPhase.Focus)
            {
                _pet.PlayTimerReaction("react.pet");
                Notify(
                    "专注完成",
                    $"专注 {_pomodoroOptions?.FocusMinutes ?? 25} 分钟完成，该休息啦");
                StartHourglassAnimation(
                    HourglassAnimationKind.Flip,
                    1, 1, 0.36,
                    midpoint: () =>
                    {
                        _scene.SetSandBreak(true);
                        _scene.SetProgress(0);
                    },
                    completed: null);
            }
            else
            {
                _scene.SetSandBreak(true);
                StartHourglassAnimation(
                    HourglassAnimationKind.Fade,
                    0, 1, 0.45,
                    midpoint: null,
                    completed: null);
                _pet.PlayTimerReaction("play.pounce");
                var minutes = snapshot.Phase == PomodoroPhase.LongBreak
                    ? _pomodoroOptions?.LongBreakMinutes ?? 15
                    : _pomodoroOptions?.ShortBreakMinutes ?? 5;
                Notify("休息开始", $"休息 {minutes} 分钟");
            }

            ShowTagForActivePhase(snapshot);
        }
        else if (snapshot.Phase == PomodoroPhase.Idle)
        {
            _scene.SetProgress(0);
            HideHoverControls();
            StartHourglassAnimation(
                HourglassAnimationKind.Fade,
                _scene.HourglassAlpha, 0, 0.4,
                midpoint: null,
                completed: () =>
                {
                    _scene.SetTimerVisible(false);
                    _scene.SetHourglassVisual(1, 1);
                });

            if (transition.Reason == PhaseTransitionReason.Completed)
            {
                _pet.PlayTimerReaction(
                    transition.From == PomodoroPhase.Focus ? "react.pet" : "idle.stretch");
                ShowCompletionTag();
                var message = transition.From == PomodoroPhase.Focus
                    ? "专注完成，休息一下？"
                    : "休息结束，准备下一个番茄";
                var title = transition.From == PomodoroPhase.Focus ? "专注完成" : "休息结束";
                Notify(title, message);
            }
            else
            {
                _pet.PlayTimerReaction("play.pounce");
                _tag?.FadeOutAndHide();
            }
        }

        UpdateTagTime(snapshot, force: true);
        RefreshTag();
        UpdateTray();
    }

    private void TickPomodoro()
    {
        if (_pomodoro is null || _scene is null)
        {
            return;
        }

        _pomodoro.Tick();
        var snapshot = _pomodoro.Snapshot;
        _scene.SetProgress(snapshot.Total > TimeSpan.Zero
            ? 1.0 - snapshot.Remaining.TotalSeconds / snapshot.Total.TotalSeconds
            : 0);

        UpdateRunState(snapshot);
        UpdateTagTime(snapshot, force: false);
        _pet?.SetPomodoroStatus(
            snapshot,
            _pomodoroOptions?.FocusMinutes ?? 25,
            _pomodoroOptions?.ShortBreakMinutes ?? 5);
    }

    private void UpdateRunState(PomodoroSnapshot snapshot)
    {
        if (snapshot.RunState == _lastRunState)
        {
            return;
        }

        if (snapshot.RunState == PomodoroRunState.Paused)
        {
            if (snapshot.Phase != PomodoroPhase.Idle && ShouldShowTag())
            {
                _tag?.SetPaused(true);
            }

            _pet?.PlayTimerReaction("idle.look_around");
            if (snapshot.PauseReason == "SystemSuspend")
            {
                _suspendPaused = true;
            }
        }
        else if (snapshot.RunState == PomodoroRunState.Running &&
                 _lastRunState == PomodoroRunState.Paused)
        {
            ApplyTagPhase(snapshot);
            _pet?.PlayTimerReaction("idle.stretch");
            if (_suspendPaused)
            {
                _suspendPaused = false;
                Notify("系统休眠", "计时已暂停，按继续恢复");
            }
        }

        _lastRunState = snapshot.RunState;
        UpdateTray();
    }

    private void UpdateTagTime(PomodoroSnapshot snapshot, bool force)
    {
        if (_tag is null || snapshot.Phase == PomodoroPhase.Idle)
        {
            return;
        }

        var seconds = (int)Math.Ceiling(snapshot.Remaining.TotalSeconds);
        if (!force && seconds == _lastTagSecond)
        {
            return;
        }

        _lastTagSecond = seconds;
        if (ShouldShowTag())
        {
            _tag.SetTime(snapshot.Remaining);
        }
    }

    private bool ShouldShowTag() =>
        _tagEnabled &&
        _pet?.IsWindowVisible == true &&
        !_fullscreenHidden &&
        !_isDragging &&
        !_completionShown &&
        _pomodoro?.Snapshot.Phase != PomodoroPhase.Idle;

    private void ApplyTagPhase(PomodoroSnapshot snapshot)
    {
        if (_tag is null)
        {
            return;
        }

        if (snapshot.Phase == PomodoroPhase.Focus)
        {
            _tag.SetPhaseFocus();
        }
        else if (snapshot.Phase is PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak)
        {
            _tag.SetPhaseBreak();
        }

        if (snapshot.RunState == PomodoroRunState.Paused)
        {
            _tag.SetPaused(true);
        }
        else
        {
            _tag.SetPaused(false);
            if (snapshot.Phase == PomodoroPhase.Focus)
            {
                _tag.SetPhaseFocus();
            }
            else if (snapshot.Phase is PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak)
            {
                _tag.SetPhaseBreak();
            }
        }

        _tag.SetTime(snapshot.Remaining);
    }

    private void ShowTagForActivePhase(PomodoroSnapshot snapshot)
    {
        if (_tag is null || _pet is null || !ShouldShowTag())
        {
            return;
        }

        ApplyTagPhase(snapshot);
        _tag.Place(_pet.GetBoundsPx(), _pet.DpiScale);
        if (!_tag.IsVisible)
        {
            _tag.ShowFadeIn();
        }
    }

    private void ShowCompletionTag()
    {
        if (_tag is null || _pet is null || !_tagEnabled || !_pet.IsWindowVisible || _fullscreenHidden)
        {
            return;
        }

        CancelCompletionDisplay();
        _completionShown = true;
        _tag.SetPhaseFocus();
        _tag.SetPaused(false);
        _tag.SetTime(TimeSpan.Zero);
        _tag.Place(_pet.GetBoundsPx(), _pet.DpiScale);
        if (!_tag.IsVisible)
        {
            _tag.ShowFadeIn();
        }

        _completionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _completionTimer.Tick += (_, _) =>
        {
            CancelCompletionDisplay();
            _tag.FadeOutAndHide();
        };
        _completionTimer.Start();
    }

    private void CancelCompletionDisplay()
    {
        if (_completionTimer is not null)
        {
            _completionTimer.Stop();
            _completionTimer = null;
        }

        _completionShown = false;
    }

    private void RefreshTag()
    {
        if (_tag is null || _pet is null)
        {
            return;
        }

        if (_completionShown)
        {
            return;
        }

        if (ShouldShowTag())
        {
            ShowTagForActivePhase(_pomodoro!.Snapshot);
        }
        else if (_tag.IsVisible)
        {
            _tag.FadeOutAndHide();
        }
    }

    private void UpdateHoverControls()
    {
        if (!_hoverControlsEnabled || _hover is null || _pet is null || _pomodoro is null)
        {
            return;
        }

        var snapshot = _pomodoro.Snapshot;
        if (snapshot.Phase == PomodoroPhase.Idle ||
            !_pet.IsWindowVisible ||
            _fullscreenHidden ||
            _isDragging)
        {
            HideHoverControls();
            return;
        }

        CursorUtil.GetPosition(out var cursorX, out var cursorY);
        var petBounds = _pet.GetBoundsPx();
        var scale = _pet.DpiScale;
        var barWidth = (int)Math.Round(HoverControlsWindow.BarWidthDip * scale);
        var barHeight = (int)Math.Round(HoverControlsWindow.BarHeightDip * scale);
        var barLeft = petBounds.Left + (petBounds.Width - barWidth) / 2;
        var barTop = petBounds.Top + (int)Math.Round(HoverControlsWindow.BarTopDip * scale);

        var overPet = cursorX >= petBounds.Left && cursorX < petBounds.Right &&
                      cursorY >= petBounds.Top && cursorY < petBounds.Bottom;
        var overBar = _hoverShown &&
                      cursorX >= barLeft && cursorX < barLeft + barWidth &&
                      cursorY >= barTop && cursorY < barTop + barHeight;

        var now = DateTime.UtcNow;
        if (overPet || overBar)
        {
            _hoverLeftAt = default;
            if (_hoverEnteredAt == default)
            {
                _hoverEnteredAt = now;
            }

            if (!_hoverShown && (now - _hoverEnteredAt).TotalMilliseconds >= 350)
            {
                _hover.SetPaused(snapshot.RunState == PomodoroRunState.Paused);
                _hover.SetTopmost(_pet.IsTopmost);
                _hover.Place(petBounds, scale);
                _hover.ShowFadeIn();
                _hoverShown = true;
                Log.Info("hover controls shown");
            }

            if (_hoverShown)
            {
                _hover.SetPaused(snapshot.RunState == PomodoroRunState.Paused);
                _hover.Place(petBounds, scale);
            }

            return;
        }

        _hoverEnteredAt = default;
        if (!_hoverShown)
        {
            return;
        }

        if (_hoverLeftAt == default)
        {
            _hoverLeftAt = now;
        }
        else if ((now - _hoverLeftAt).TotalMilliseconds >= 1500)
        {
            HideHoverControls();
        }
    }

    private void HideHoverControls()
    {
        _hoverEnteredAt = default;
        _hoverLeftAt = default;
        if (!_hoverShown)
        {
            return;
        }

        _hoverShown = false;
        _hover?.FadeOutAndHide();
    }

    private void Notify(string title, string message)
    {
        if (!_notificationsEnabled)
        {
            return;
        }

        _tray?.ShowBalloon(title, message);
    }

    private void OnPetBoundsChanged()
    {
        if (_pet is not null && _tag is not null && _pet.IsWindowVisible)
        {
            _tag.Place(_pet.GetBoundsPx(), _pet.DpiScale);
        }

        if (_hover is { IsVisible: true } && _pet is not null)
        {
            _hover.Place(_pet.GetBoundsPx(), _pet.DpiScale);
        }
    }

    private void OnPetPositionChanged()
    {
        if (_settings is null || _pet is null)
        {
            return;
        }

        var bounds = _pet.GetBoundsPx();
        if (bounds.Width <= 0)
        {
            return;
        }

        _settings.Current.General.LastPosition = new WindowPosition { X = bounds.Left, Y = bounds.Top };
        _settings.SaveDebounced();
    }

    private void OnPetTopmostChanged(bool topmost)
    {
        if (_settings is not null)
        {
            _settings.Current.General.AlwaysOnTop = topmost;
            _settings.SaveDebounced();
        }

        _tag?.SetTopmost(topmost);
        UpdateTray();
    }

    private void OnPetVisibilityChanged(bool visible)
    {
        if (visible)
        {
            RefreshTag();
        }
        else
        {
            _tag?.HideNow();
            HideHoverControls();
        }

        UpdateTray();
    }

    private void OnPetDragStateChanged(bool dragging)
    {
        _isDragging = dragging;
        if (dragging)
        {
            _tag?.HideNow();
            HideHoverControls();
        }
        else
        {
            RefreshTag();
        }
    }

    private void HidePet()
    {
        if (_pet is null || !_pet.IsWindowVisible)
        {
            return;
        }

        _userHidden = true;
        _pet.SetVisible(false);
        _tag?.HideNow();
        HideHoverControls();
        Log.Info("pet hidden by user");
    }

    private void TogglePetVisibility()
    {
        if (_pet is null)
        {
            return;
        }

        if (_pet.IsWindowVisible)
        {
            HidePet();
        }
        else
        {
            ShowPetAndHighlight();
        }
    }

    private void ShowPetAndHighlight()
    {
        if (_pet is null)
        {
            return;
        }

        _userHidden = false;
        _pet.BringToFront();
        RefreshTag();
        if (_pomodoro?.Snapshot.Phase != PomodoroPhase.Idle && _tag?.IsVisible == true)
        {
            _tag.Highlight();
        }

        Log.Info("pet shown from tray or second instance");
        UpdateTray();
    }

    private void ShowPetFromExternalRequest()
    {
        Log.Info("show requested by a second instance");
        ShowPetAndHighlight();
    }

    private TrayStatus BuildTrayStatus()
    {
        var snapshot = _pomodoro?.Snapshot ?? new PomodoroSnapshot(
            PomodoroPhase.Idle, PomodoroRunState.Stopped, TimeSpan.Zero, TimeSpan.Zero, 0, 4, null);
        var statusText = "空闲";
        var phase = TrayPhase.Idle;

        if (snapshot.Phase != PomodoroPhase.Idle)
        {
            if (snapshot.RunState == PomodoroRunState.Paused)
            {
                statusText = $"已暂停 {FormatRemaining(snapshot.Remaining)}";
                phase = TrayPhase.Paused;
            }
            else
            {
                statusText = snapshot.Phase == PomodoroPhase.Focus
                    ? $"专注中 {FormatRemaining(snapshot.Remaining)}"
                    : $"休息中 {FormatRemaining(snapshot.Remaining)}";
                phase = snapshot.Phase == PomodoroPhase.Focus ? TrayPhase.Focus : TrayPhase.Break;
            }
        }
        else if (_completionShown)
        {
            statusText = "已完成";
        }
        else if (_pet?.IsSleeping == true)
        {
            statusText = "睡觉中";
        }
        else if (_pet?.IsPlaying == true)
        {
            statusText = "玩耍中";
        }

        return new TrayStatus(
            _petName,
            _pet?.IsWindowVisible ?? true,
            _pet?.IsTopmost ?? true,
            statusText,
            phase,
            _pet?.IsSleeping ?? false,
            _pet?.IsPlaying ?? false,
            snapshot,
            _pomodoroOptions?.FocusMinutes ?? 25,
            _pomodoroOptions?.ShortBreakMinutes ?? 5);
    }

    private void UpdateTray() => _tray?.Update(BuildTrayStatus());

    private static string FormatRemaining(TimeSpan remaining) =>
        $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";

    private void StartTimers()
    {
        _uiTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _uiTimer.Tick += (_, _) =>
        {
            UpdateTray();
            UpdateHoverControls();
        };
        _uiTimer.Start();

        _pomodoroTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _pomodoroTimer.Tick += (_, _) => TickPomodoro();
        _pomodoroTimer.Start();

        _statsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statsTimer.Tick += OnStatsTick;
        _statsTimer.Start();

        _hourglassTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _hourglassTimer.Tick += OnHourglassAnimationTick;

        if (_pet is not null)
        {
            _lastCommitCount = _pet.Clock.CommitCount;
            _lastDroppedCount = _pet.Clock.DroppedTicks;
        }
    }

    private void OnHourglassAnimationTick(object? sender, EventArgs e)
    {
        if (_scene is null)
        {
            return;
        }

        var duration = Math.Max(0.05, _hourglassDuration);
        var t = Math.Clamp(_hourglassClock.Elapsed.TotalSeconds / duration, 0, 1);
        if (_hourglassAnimation == HourglassAnimationKind.Flip)
        {
            var flip = Math.Abs(Math.Cos(Math.PI * t));
            _scene.SetHourglassVisual(1, Math.Max(0.02, flip));
        }
        else
        {
            var alpha = _hourglassFromAlpha + (_hourglassToAlpha - _hourglassFromAlpha) * t;
            _scene.SetHourglassVisual(alpha, 1);
        }

        if (!_hourglassMidpointDone && t >= 0.5)
        {
            _hourglassMidpointDone = true;
            _hourglassMidpoint?.Invoke();
        }

        if (t >= 1)
        {
            _hourglassTimer?.Stop();
            if (_hourglassAnimation == HourglassAnimationKind.Flip)
            {
                _scene.SetHourglassVisual(1, 1);
            }

            _hourglassCompleted?.Invoke();
        }
    }

    private void StartHourglassAnimation(
        HourglassAnimationKind kind,
        double fromAlpha,
        double toAlpha,
        double durationSeconds,
        Action? midpoint,
        Action? completed)
    {
        _hourglassAnimation = kind;
        _hourglassFromAlpha = fromAlpha;
        _hourglassToAlpha = toAlpha;
        _hourglassDuration = durationSeconds;
        _hourglassMidpoint = midpoint;
        _hourglassCompleted = completed;
        _hourglassMidpointDone = false;
        _hourglassClock.Restart();
        _hourglassTimer?.Start();
    }

    private void OnStatsTick(object? sender, EventArgs e)
    {
        if (_pet is null)
        {
            return;
        }

        var commits = _pet.Clock.CommitCount;
        var commitsDelta = commits - _lastCommitCount;
        _lastCommitCount = commits;

        var dropped = _pet.Clock.DroppedTicks;
        var droppedDelta = dropped - _lastDroppedCount;
        _lastDroppedCount = dropped;

        if (_benchmark is not null)
        {
            UpdateBenchmark(commitsDelta, droppedDelta);
            return;
        }

        _normalTickCount++;
        if (_normalTickCount % 10 == 0)
        {
            var cpu = _metrics.SampleCpuPercent();
            var memoryMb = _metrics.PrivateBytes / 1024.0 / 1024.0;
            Log.Info(
                $"stats: cpu={cpu:0.00}%, mem={memoryMb:0.0}MB, commits/s={commitsDelta}, dropped={droppedDelta}");
        }

        CheckFullscreen();
    }

    private void CheckFullscreen()
    {
        if (_settings is null || _pet is null)
        {
            return;
        }

        if (!_settings.Current.General.HideOnFullscreen)
        {
            if (_fullscreenHidden)
            {
                _fullscreenHidden = false;
                if (!_userHidden)
                {
                    _pet.SetVisible(true);
                    RefreshTag();
                }
            }

            return;
        }

        var tagHandle = _tag is null ? IntPtr.Zero : new WindowInteropHelper(_tag).Handle;
        var fullscreen = FullscreenWatcher.IsFullscreen(_pet.Handle, tagHandle);
        if (fullscreen == _fullscreenHidden)
        {
            return;
        }

        _fullscreenHidden = fullscreen;
        if (fullscreen)
        {
            _pet.SetVisible(false);
            _tag?.HideNow();
            HideHoverControls();
            Log.Info("fullscreen detected; pet scene hidden");
        }
        else
        {
            if (!_userHidden)
            {
                _pet.SetVisible(true);
                RefreshTag();
            }

            Log.Info("fullscreen exited; pet scene restored");
        }

        UpdateTray();
    }

    private void StartBenchmark(int seconds)
    {
        if (_pet is null)
        {
            return;
        }

        _benchmark = new BenchmarkState
        {
            Watch = Stopwatch.StartNew(),
            Seconds = seconds
        };

        _pet.Clock.TargetFps = BenchmarkFps;
        _pet.SetBenchmarkMode(true);
        StartFocusFromUser(showPet: false);

        _lastCommitCount = _pet.Clock.CommitCount;
        _lastDroppedCount = _pet.Clock.DroppedTicks;
        Log.Info($"BENCHMARK start: {seconds}s at {BenchmarkFps:0}fps target, cores={Environment.ProcessorCount}");
    }

    private void UpdateBenchmark(long commitsDelta, long droppedDelta)
    {
        var benchmark = _benchmark!;
        var cpu = _metrics.SampleCpuPercent();
        var memory = _metrics.PrivateBytes;

        benchmark.CpuSum += cpu;
        benchmark.CpuMax = Math.Max(benchmark.CpuMax, cpu);
        benchmark.CpuSamples++;
        benchmark.PeakPrivate = Math.Max(benchmark.PeakPrivate, memory);

        benchmark.FpsSum += commitsDelta;
        benchmark.FpsSamples++;
        benchmark.FpsMin = Math.Min(benchmark.FpsMin, commitsDelta);
        benchmark.FpsMax = Math.Max(benchmark.FpsMax, commitsDelta);
        benchmark.DroppedTotal += droppedDelta;

        var elapsedSeconds = (int)benchmark.Watch.Elapsed.TotalSeconds;
        if (elapsedSeconds - benchmark.LastLogSecond >= 5)
        {
            benchmark.LastLogSecond = elapsedSeconds;
            Log.Info(
                $"benchmark t={elapsedSeconds}s cpu={cpu:0.00}% mem={memory / 1024.0 / 1024.0:0.0}MB " +
                $"commits/s={commitsDelta} dropped={droppedDelta}");
        }

        if (benchmark.Watch.Elapsed.TotalSeconds < benchmark.Seconds)
        {
            return;
        }

        var avgCpu = benchmark.CpuSum / Math.Max(1, benchmark.CpuSamples);
        var avgFps = benchmark.FpsSum / Math.Max(1, benchmark.FpsSamples);
        Log.Info(
            $"BENCHMARK RESULT: target={BenchmarkFps:0}fps actual_avg={avgFps:0.0}fps min={benchmark.FpsMin} " +
            $"max={benchmark.FpsMax} dropped_total={benchmark.DroppedTotal} cpu_avg={avgCpu:0.00}% " +
            $"cpu_max={benchmark.CpuMax:0.00}% private_peak={benchmark.PeakPrivate / 1024.0 / 1024.0:0.0}MB " +
            $"cores={Environment.ProcessorCount}");

        _benchmark = null;
        _pomodoro?.Reset();
        Shutdown();
    }

    private void OnSystemSuspending()
    {
        if (_pomodoro?.Snapshot.RunState == PomodoroRunState.Running)
        {
            _suspendPaused = true;
            _pomodoro.Pause("SystemSuspend");
            Log.Info("pomodoro paused due to system suspend");
        }
    }

    private void OnSystemResumed()
    {
        if (!_suspendPaused)
        {
            return;
        }

        _suspendPaused = false;
        RefreshTag();
        UpdateTray();
        Notify("系统休眠", "计时已暂停，按继续恢复");
        Log.Info("system resumed; pomodoro remains paused");
    }

    private void RequestExit()
    {
        var snapshot = _pomodoro?.Snapshot;
        if (snapshot is { IsActive: true })
        {
            var result = MessageBox.Show(
                "番茄钟仍在进行，确定退出吗？",
                _petName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        Shutdown();
    }

    private PixelRect? GetRestorePosition()
    {
        var position = _settings?.Current.General.LastPosition;
        return position is null ? null : new PixelRect(position.X, position.Y, 0, 0);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            Log.Warn($"打开目录失败：{exception.Message}");
        }
    }

    private static string GetVersion()
    {
        var informational = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational
               ?? typeof(App).Assembly.GetName().Version?.ToString()
               ?? "0.0.0";
    }

    private static double SafeProcessAgeMs()
    {
        try
        {
            return (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        }
        catch
        {
            return StartupClock.Elapsed.TotalMilliseconds;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _uiTimer?.Stop();
        _pomodoroTimer?.Stop();
        _statsTimer?.Stop();
        _hourglassTimer?.Stop();
        CancelCompletionDisplay();

        if (_settings is not null && _pet is not null)
        {
            var bounds = _pet.GetBoundsPx();
            if (bounds.Width > 0)
            {
                _settings.Current.General.LastPosition = new WindowPosition
                {
                    X = bounds.Left,
                    Y = bounds.Top
                };
            }

            _settings.Current.General.AlwaysOnTop = _pet.IsTopmost;
            _settings.SaveNow();
        }

        _tray?.Dispose();
        _pet?.Dispose();
        _tag?.Close();
        _hover?.Close();
        _settings?.Dispose();
        _singleInstance?.Dispose();

        Log.Info($"exit code={e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private enum HourglassAnimationKind
    {
        Fade,
        Flip
    }

    private sealed class BenchmarkState
    {
        public required Stopwatch Watch { get; init; }

        public required int Seconds { get; init; }

        public double CpuSum;
        public double CpuMax;
        public int CpuSamples;
        public long PeakPrivate;

        public double FpsSum;
        public int FpsSamples;
        public double FpsMin = double.MaxValue;
        public double FpsMax;
        public long DroppedTotal;

        public int LastLogSecond;
    }

    private sealed record CommandLineOptions(int BenchmarkSeconds, bool StartFocus)
    {
        public static CommandLineOptions Parse(string[] args)
        {
            var benchmark = 0;
            var startFocus = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--benchmark":
                        benchmark = 30;
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var seconds))
                        {
                            benchmark = Math.Clamp(seconds, 5, 600);
                            i++;
                        }

                        break;

                    case "--demo":
                    case "--start-focus":
                        startFocus = true;
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out _))
                        {
                            i++;
                        }

                        break;
                }
            }

            return new CommandLineOptions(benchmark, startFocus);
        }
    }
}
