using KaixinPet.Core.Logging;

namespace KaixinPet.App;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\KaixinPet.SingleInstance";
    private const string ShowEventName = @"Local\KaixinPet.ShowSignal";
    private const string ExitEventName = @"Local\KaixinPet.ExitSignal";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _showEvent;
    private readonly EventWaitHandle? _exitEvent;
    private readonly ManualResetEvent? _stopEvent;
    private readonly Thread? _listener;

    public SingleInstanceGuard(bool requestExit = false)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;

        if (createdNew)
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            _stopEvent = new ManualResetEvent(false);
            _listener = new Thread(Listen)
            {
                IsBackground = true,
                Name = "KaixinPet.SingleInstanceListener"
            };
            _listener.Start();
        }
        else if (requestExit)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ExitEventName);
                signal.Set();
                Log.Info("second instance requested a graceful exit");
            }
            catch (Exception exception)
            {
                Log.Warn($"second instance exit signal failed: {exception.Message}");
            }
        }
        else
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowEventName);
                signal.Set();
                Log.Info("second instance detected; signaled the running instance");
            }
            catch (Exception exception)
            {
                Log.Warn($"second instance detected; signal failed: {exception.Message}");
            }
        }
    }

    public bool IsFirstInstance { get; }

    public event Action? ShowRequested;

    public event Action? ExitRequested;

    private void Listen()
    {
        var handles = new WaitHandle[] { _showEvent!, _exitEvent!, _stopEvent! };
        while (true)
        {
            var index = WaitHandle.WaitAny(handles);
            if (index == 2)
            {
                return;
            }

            try
            {
                if (index == 0)
                {
                    ShowRequested?.Invoke();
                }
                else
                {
                    ExitRequested?.Invoke();
                }
            }
            catch (Exception exception)
            {
                Log.Warn($"single-instance handler failed: {exception.Message}");
            }
        }
    }

    public void Dispose()
    {
        _stopEvent?.Set();
        _listener?.Join(TimeSpan.FromMilliseconds(500));
        _showEvent?.Dispose();
        _exitEvent?.Dispose();
        _stopEvent?.Dispose();

        if (IsFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // 进程退出时互斥体可能已被放弃。
            }
        }

        _mutex.Dispose();
    }
}
