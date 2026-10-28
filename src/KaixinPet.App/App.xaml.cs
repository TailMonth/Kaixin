using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using KaixinPet.App.Ui;
using KaixinPet.Core;
using KaixinPet.Core.Diagnostics;
using KaixinPet.Core.Logging;
using KaixinPet.Pet;
using KaixinPet.Pet.Assets;
using KaixinPet.Pet.Scene;

namespace KaixinPet.App;

public partial class App : Application
{
    private const double BenchmarkFps = 24;

    private static readonly Stopwatch StartupClock = Stopwatch.StartNew();

    private readonly ProcessMetrics _metrics = new();
    private readonly TimerDemo _demo = new();

    private AssetCatalog? _assets;
    private PetScene? _scene;
    private PetWindow? _pet;
    private TimeTagWindow? _tag;
    private DispatcherTimer? _uiTimer;
    private DispatcherTimer? _statsTimer;
    private DispatcherTimer? _completionTimer;

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
        var processAgeMs = SafeProcessAgeMs();
        Log.Info(
            $"KaixinPet 0.1.0-m0 starting; os={Environment.OSVersion.VersionString}; cores={Environment.ProcessorCount}; " +
            $"processAge={processAgeMs:0}ms; assets={AppPaths.AssetsRoot}");
        Log.Info($"args: benchmark={options.BenchmarkSeconds}s, demo={options.DemoMinutes}min");

        try
        {
            _assets = AssetCatalog.Load(AppPaths.AssetsRoot);
            _scene = new PetScene();
            _tag = new TimeTagWindow();
            _pet = new PetWindow(
                _assets,
                _scene,
                StartupClock,
                elapsedMs => Log.Info(
                    $"startup: first frame at {SafeProcessAgeMs():0} ms from process start " +
                    $"(managed clock {elapsedMs:0} ms)"));

            _pet.DemoRequested += StartDemo;
            _pet.DemoStopRequested += StopDemo;
            _pet.ExitRequested += Shutdown;
            _pet.OpenLogsRequested += OpenLogsFolder;
            _pet.BoundsChanged += OnPetBoundsChanged;
            _pet.DragStateChanged += OnPetDragStateChanged;
            _pet.TopmostChanged += topmost => _tag?.SetTopmost(topmost);

            _pet.Show();

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

            _lastCommitCount = _pet.Clock.CommitCount;
            _lastDroppedCount = _pet.Clock.DroppedTicks;

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
        _tag.Place(_pet.GetBoundsPx(), _pet.DpiScale);
        _tag.ShowFadeIn();

        Log.Info($"demo timer started: {minutes} min");
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
    }

    private void OnPetBoundsChanged()
    {
        if (_pet is not null && _tag is not null)
        {
            _tag.Place(_pet.GetBoundsPx(), _pet.DpiScale);
        }
    }

    private void OnPetDragStateChanged(bool dragging)
    {
        if (_tag is null)
        {
            return;
        }

        if (dragging)
        {
            _tag.HideNow();
            return;
        }

        if (_demo.IsRunning && !_completionShown && _pet is not null)
        {
            _tag.Place(_pet.GetBoundsPx(), _pet.DpiScale);
            _tag.ShowFadeIn();
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

    private void CancelCompletionTimer()
    {
        if (_completionTimer is null)
        {
            return;
        }

        _completionTimer.Stop();
        _completionTimer = null;
    }

    private static void OpenLogsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{AppPaths.LogsDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            Log.Warn($"打开日志目录失败：{exception.Message}");
        }
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

        _pet?.Dispose();
        _tag?.Close();

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
