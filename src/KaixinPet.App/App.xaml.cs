using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using KaixinPet.App.Tray;
using KaixinPet.App.Ui;
using KaixinPet.Core;
using KaixinPet.Core.Diagnostics;
using KaixinPet.Core.Logging;
using KaixinPet.Core.Settings;
using KaixinPet.Pet;
using KaixinPet.Pet.Assets;
using KaixinPet.Pet.Native;
using KaixinPet.Pet.Scene;

namespace KaixinPet.App;

public partial class App : Application
{
    private const double BenchmarkFps = 24;

    private static readonly Stopwatch StartupClock = Stopwatch.StartNew();

    private readonly ProcessMetrics _metrics = new();
    private readonly TimerDemo _demo = new();

    private SingleInstanceGuard? _singleInstance;
    private SettingsService? _settings;
    private AssetCatalog? _assets;
    private PetScene? _scene;
    private PetWindow? _pet;
    private TimeTagWindow? _tag;
    private TrayService? _tray;
    private DispatcherTimer? _uiTimer;
    private DispatcherTimer? _statsTimer;
    private DispatcherTimer? _completionTimer;

    private string _petName = "开心";
    private bool _completionShown;
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
        Log.Info($"args: benchmark={options.BenchmarkSeconds}s, demo={options.DemoMinutes}min");

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

            _assets = AssetCatalog.Load(AppPaths.AssetsRoot);
            _scene = new PetScene();
            _tag = new TimeTagWindow();
            _pet = new PetWindow(
                _assets,
                _scene,
                StartupClock,
                _petName,
                _settings.Current.General.AlwaysOnTop,
                elapsedMs => Log.Info(
                    $"startup: first frame at {SafeProcessAgeMs():0} ms from process start " +
                    $"(managed clock {elapsedMs:0} ms)"));

            WirePetEvents();
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
            else if (options.DemoMinutes > 0)
            {
                StartDemo(options.DemoMinutes);
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

        _pet.DemoRequested += StartDemo;
        _pet.DemoStopRequested += StopDemo;
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
        _tray.Demo25Requested += () => StartDemo(25);
        _tray.Demo1Requested += () => StartDemo(1);
        _tray.DemoStopRequested += StopDemo;
        _tray.TopmostToggleRequested += topmost => _pet?.SetTopmost(topmost);
        _tray.ResetPositionRequested += () => _pet?.ResetPosition();
        _tray.OpenDataRequested += () => OpenFolder(AppPaths.DataRoot);
        _tray.OpenLogsRequested += () => OpenFolder(AppPaths.LogsDirectory);
        _tray.ExitRequested += RequestExit;
    }

    private PixelRect? GetRestorePosition()
    {
        var position = _settings?.Current.General.LastPosition;
        return position is null ? null : new PixelRect(position.X, position.Y, 0, 0);
    }

    private void StartDemo(int minutes)
    {
        if (_scene is null || _tag is null || _pet is null)
        {
            return;
        }

        CancelCompletionTimer();
        _completionShown = false;

        _demo.Start(TimeSpan.FromMinutes(minutes));
        _scene.SetProgress(0);
        _scene.SetTimerVisible(true);

        _tag.SetPhaseFocus();
        _tag.SetPaused(false);
        _tag.SetTime(_demo.Duration);

        if (_pet.IsWindowVisible)
        {
            _tag.Place(_pet.GetBoundsPx(), _pet.DpiScale);
            _tag.ShowFadeIn();
        }
        else
        {
            _tag.HideNow();
        }

        Log.Info($"demo timer started: {minutes} min");
        UpdateTray();
    }

    private void StopDemo()
    {
        CancelCompletionTimer();
        _completionShown = false;
        _demo.Stop();
        _scene?.SetTimerVisible(false);
        _scene?.SetProgress(0);
        _tag?.FadeOutAndHide();
        Log.Info("demo timer stopped");
        UpdateTray();
    }

    private void OnUiTick(object? sender, EventArgs e)
    {
        if (_scene is null || _tag is null || !_demo.IsRunning)
        {
            return;
        }

        var remaining = _demo.Remaining;
        _scene.SetProgress(_demo.Progress);

        if (_demo.TickAndCheckCompleted())
        {
            _completionShown = true;
            _tag.SetTime(TimeSpan.Zero);
            _scene.SetProgress(1);
            Log.Info("demo timer completed");
            UpdateTray();

            _completionTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _completionTimer.Tick += (_, _) =>
            {
                CancelCompletionTimer();
                StopDemo();
            };
            _completionTimer.Start();
            return;
        }

        _tag.SetTime(remaining);
        UpdateTray();
    }

    private void OnPetBoundsChanged()
    {
        if (_pet is not null && _tag is not null && _pet.IsWindowVisible)
        {
            _tag.Place(_pet.GetBoundsPx(), _pet.DpiScale);
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
        if (_tag is null || _pet is null)
        {
            return;
        }

        if (visible)
        {
            if (_demo.IsRunning && !_completionShown)
            {
                _tag.Place(_pet.GetBoundsPx(), _pet.DpiScale);
                _tag.ShowFadeIn();
            }
        }
        else
        {
            _tag.HideNow();
        }

        UpdateTray();
    }

    private void OnPetDragStateChanged(bool dragging)
    {
        if (_tag is null || _pet is null)
        {
            return;
        }

        if (dragging)
        {
            _tag.HideNow();
            return;
        }

        if (_pet.IsWindowVisible && _demo.IsRunning && !_completionShown)
        {
            _tag.Place(_pet.GetBoundsPx(), _pet.DpiScale);
            _tag.ShowFadeIn();
        }
    }

    private void HidePet()
    {
        if (_pet is null || !_pet.IsWindowVisible)
        {
            return;
        }

        _pet.SetVisible(false);
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

        _pet.BringToFront();
        if (_demo.IsRunning && !_completionShown && _tag is not null)
        {
            _tag.Place(_pet.GetBoundsPx(), _pet.DpiScale);
            if (!_tag.IsVisible)
            {
                _tag.ShowFadeIn();
            }

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
        var demoRunning = _demo.IsRunning;
        var statusText = "空闲";
        var phase = TrayPhase.Idle;

        if (_completionShown)
        {
            statusText = "已完成";
        }
        else if (demoRunning)
        {
            statusText = $"专注中 {FormatRemaining(_demo.Remaining)}";
            phase = TrayPhase.Focus;
        }

        return new TrayStatus(
            _petName,
            _pet?.IsWindowVisible ?? true,
            demoRunning,
            _pet?.IsTopmost ?? true,
            statusText,
            phase);
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
        _uiTimer.Tick += OnUiTick;
        _uiTimer.Start();

        _statsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statsTimer.Tick += OnStatsTick;
        _statsTimer.Start();

        if (_pet is not null)
        {
            _lastCommitCount = _pet.Clock.CommitCount;
            _lastDroppedCount = _pet.Clock.DroppedTicks;
        }
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
        StartDemo(25);

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
        StopDemo();
        Shutdown();
    }

    private void RequestExit()
    {
        if (_demo.IsRunning && !_completionShown)
        {
            var result = MessageBox.Show(
                "计时演示仍在进行，确定退出吗？",
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

    private void CancelCompletionTimer()
    {
        if (_completionTimer is null)
        {
            return;
        }

        _completionTimer.Stop();
        _completionTimer = null;
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
        _statsTimer?.Stop();
        CancelCompletionTimer();

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
        _settings?.Dispose();
        _singleInstance?.Dispose();

        Log.Info($"exit code={e.ApplicationExitCode}");
        base.OnExit(e);
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

    private sealed record CommandLineOptions(int BenchmarkSeconds, int DemoMinutes)
    {
        public static CommandLineOptions Parse(string[] args)
        {
            var benchmark = 0;
            var demo = 0;

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
                        demo = 25;
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var minutes))
                        {
                            demo = Math.Clamp(minutes, 1, 180);
                            i++;
                        }

                        break;
                }
            }

            return new CommandLineOptions(benchmark, demo);
        }
    }
}
