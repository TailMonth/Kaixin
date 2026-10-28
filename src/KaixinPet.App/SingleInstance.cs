using KaixinPet.Core.Logging;

namespace KaixinPet.App;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\KaixinPet.SingleInstance";
    private const string ShowEventName = @"Local\KaixinPet.ShowSignal";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _showEvent;
    private readonly ManualResetEvent? _stopEvent;
    private readonly Thread? _listener;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;

        if (createdNew)
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _stopEvent = new ManualResetEvent(false);
            _listener = new Thread(Listen)
            {
                IsBackground = true,
                Name = "KaixinPet.SingleInstanceListener"
            };
            _listener.Start();
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

    private void Listen()
    {
        var handles = new WaitHandle[] { _showEvent!, _stopEvent! };
        while (true)
        {
            var index = WaitHandle.WaitAny(handles);
            if (index != 0)
            {
                return;
            }

            try
            {
                ShowRequested?.Invoke();
            }
            catch (Exception exception)
            {
                Log.Warn($"single-instance show handler failed: {exception.Message}");
            }
        }
    }

    public void Dispose()
    {
        _stopEvent?.Set();
        _listener?.Join(TimeSpan.FromMilliseconds(500));
        _showEvent?.Dispose();
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
