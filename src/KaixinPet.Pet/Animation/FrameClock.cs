using System.Diagnostics;
using System.Windows.Threading;

namespace KaixinPet.Pet.Animation;

public sealed class FrameClock : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<TimeSpan> _callback;
    private readonly ManualResetEventSlim _stop = new(false);

    private Thread? _thread;
    private int _pending;
    private long _commits;
    private long _dropped;

    public FrameClock(Dispatcher dispatcher, Action<TimeSpan> callback, double targetFps)
    {
        _dispatcher = dispatcher;
        _callback = callback;
        TargetFps = targetFps;
    }

    public double TargetFps { get; set; }

    public long CommitCount => Interlocked.Read(ref _commits);

    public long DroppedTicks => Interlocked.Read(ref _dropped);

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "PetFrameClock",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
    }

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        long frame = 0;

        while (!_stop.IsSet)
        {
            var intervalMs = 1000.0 / Math.Max(1.0, TargetFps);
            var wait = frame * intervalMs - clock.Elapsed.TotalMilliseconds;

            if (wait > 2)
            {
                _stop.Wait(TimeSpan.FromMilliseconds(Math.Min(wait - 1, 8)));
                continue;
            }

            if (wait > 0)
            {
                Thread.SpinWait(400);
                continue;
            }

            if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
            {
                Interlocked.Increment(ref _dropped);
                frame++;
                continue;
            }

            var now = clock.Elapsed;
            try
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    try
                    {
                        _callback(now);
                    }
                    finally
                    {
                        Interlocked.Increment(ref _commits);
                        Volatile.Write(ref _pending, 0);
                    }
                }));
            }
            catch (TaskCanceledException)
            {
                Volatile.Write(ref _pending, 0);
            }
            catch (InvalidOperationException)
            {
                Volatile.Write(ref _pending, 0);
            }

            frame++;

            var lagMs = clock.Elapsed.TotalMilliseconds - frame * intervalMs;
            if (lagMs > 250)
            {
                frame = (long)(clock.Elapsed.TotalMilliseconds / intervalMs);
            }
        }
    }

    public void Stop()
    {
        _stop.Set();
        _thread?.Join(TimeSpan.FromMilliseconds(500));
        _thread = null;
    }

    public void Dispose()
    {
        Stop();
        _stop.Dispose();
    }
}
