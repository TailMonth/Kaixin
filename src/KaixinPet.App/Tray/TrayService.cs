using System.IO;
using System.Runtime.InteropServices;
using KaixinPet.Core;
using KaixinPet.Core.Logging;
using KaixinPet.Pomodoro;

namespace KaixinPet.App.Tray;

public enum TrayPhase
{
    Idle,
    Focus,
    Break,
    Paused
}

public sealed record TrayStatus(
    string PetName,
    bool PetVisible,
    bool Topmost,
    string StatusText,
    TrayPhase Phase,
    bool IsSleeping,
    PomodoroSnapshot Timer,
    int FocusMinutes,
    int ShortBreakMinutes,
    bool Muted);

public sealed class TrayService : IDisposable
{
    private const string WindowClassName = "KaixinPet.TrayHost";
    private const uint TrayIconId = 1;
    private const uint CallbackMessage = TrayNative.WM_APP + 1;

    private static readonly object ClassGate = new();
    private static bool s_classRegistered;
    private static TrayNative.WndProc? s_wndProc;
    private static TrayService? s_instance;
    private static uint s_taskbarCreatedMessage;

    private readonly TrayIcons _icons;
    private IntPtr _hwnd;
    private TrayStatus _status;
    private TrayPhase _iconPhase;
    private string _lastTip = string.Empty;
    private DateTime _lastMenuRequestUtc;
    private bool _disposed;

    public TrayService(TrayStatus status)
    {
        _status = status;
        _icons = new TrayIcons(Path.Combine(AppPaths.AssetsRoot, "ui"));

        RegisterWindowClass();
        CreateHostWindow();
        if (_hwnd != IntPtr.Zero)
        {
            AddIcon();
        }
        else
        {
            Log.Warn("tray host window creation failed; tray disabled");
        }
    }

    public event Action? ShowRequested;

    public event Action<int, int>? MenuRequested;

    public void Update(TrayStatus status)
    {
        if (_disposed)
        {
            return;
        }

        _status = status;

        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var tip = BuildTip();
        var phaseChanged = status.Phase != _iconPhase;
        if (!phaseChanged && tip == _lastTip)
        {
            return;
        }

        var data = CreateData();
        data.uFlags = TrayNative.NIF_ICON | TrayNative.NIF_TIP;
        data.hIcon = _icons.Get(status.Phase);
        data.szTip = tip;

        if (TrayNative.Shell_NotifyIcon(TrayNative.NIM_MODIFY, ref data))
        {
            _lastTip = tip;
            _iconPhase = status.Phase;
        }
        else
        {
            Log.Warn($"tray icon update failed: {Marshal.GetLastWin32Error()}");
        }
    }

    public void ShowBalloon(string title, string text, bool warning = false)
    {
        if (_hwnd == IntPtr.Zero || _disposed)
        {
            return;
        }

        var data = CreateData();
        data.uFlags = TrayNative.NIF_INFO;
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(text, 255);
        data.dwInfoFlags = warning ? TrayNative.NIIF_WARNING : TrayNative.NIIF_INFO;
        data.uTimeoutOrVersion = 5000;
        TrayNative.Shell_NotifyIcon(TrayNative.NIM_MODIFY, ref data);
    }

    private void AddIcon()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var data = CreateData();
        data.uFlags = TrayNative.NIF_MESSAGE | TrayNative.NIF_ICON | TrayNative.NIF_TIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icons.Get(_status.Phase);
        data.szTip = BuildTip();

        if (!TrayNative.Shell_NotifyIcon(TrayNative.NIM_ADD, ref data))
        {
            Log.Warn($"tray icon add failed: {Marshal.GetLastWin32Error()}");
            return;
        }

        var versionData = CreateData();
        versionData.uTimeoutOrVersion = TrayNative.NOTIFYICON_VERSION_4;
        TrayNative.Shell_NotifyIcon(TrayNative.NIM_SETVERSION, ref versionData);

        _lastTip = data.szTip;
        _iconPhase = _status.Phase;
        Log.Info($"tray icon added: tip={data.szTip}");
    }

    private TrayNative.NOTIFYICONDATAW CreateData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<TrayNative.NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = TrayIconId,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty
    };

    private string BuildTip() =>
        Truncate($"{_status.PetName} · {_status.StatusText}{(_status.Muted ? " · 静音" : "")}", 120);

    private static void RegisterWindowClass()
    {
        lock (ClassGate)
        {
            if (s_classRegistered)
            {
                return;
            }

            s_wndProc = StaticWndProc;
            var windowClass = new TrayNative.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<TrayNative.WNDCLASSEX>(),
                lpfnWndProc = s_wndProc,
                hInstance = TrayNative.GetModuleHandle(null),
                lpszClassName = WindowClassName
            };

            if (TrayNative.RegisterClassEx(ref windowClass) == 0)
            {
                throw new InvalidOperationException(
                    $"注册托盘窗口类失败：{Marshal.GetLastWin32Error()}");
            }

            s_taskbarCreatedMessage = TrayNative.RegisterWindowMessage("TaskbarCreated");
            s_classRegistered = true;
        }
    }

    private void CreateHostWindow()
    {
        s_instance = this;
        _hwnd = TrayNative.CreateWindowEx(
            TrayNative.WS_EX_TOOLWINDOW | TrayNative.WS_EX_NOACTIVATE,
            WindowClassName,
            "KaixinPet.TrayHost",
            TrayNative.WS_POPUP,
            -32000,
            -32000,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            TrayNative.GetModuleHandle(null),
            IntPtr.Zero);
    }

    private static IntPtr StaticWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var instance = s_instance;
        if (instance is not null && instance._hwnd == hwnd)
        {
            return instance.HandleMessage(msg, wParam, lParam);
        }

        return TrayNative.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private IntPtr HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == CallbackMessage)
        {
            HandleTrayCallback(lParam);
            return IntPtr.Zero;
        }

        if (s_taskbarCreatedMessage != 0 && msg == s_taskbarCreatedMessage)
        {
            Log.Info("taskbar recreated; re-adding tray icon");
            AddIcon();
            return IntPtr.Zero;
        }

        if (msg == TrayNative.WM_DESTROY)
        {
            return IntPtr.Zero;
        }

        return TrayNative.DefWindowProc(_hwnd, msg, wParam, lParam);
    }

    private void HandleTrayCallback(IntPtr lParam)
    {
        var notification = (int)(lParam.ToInt64() & 0xFFFF);
        switch (notification)
        {
            case TrayNative.WM_CONTEXTMENU:
            case TrayNative.WM_RBUTTONUP:
            {
                var now = DateTime.UtcNow;
                if ((now - _lastMenuRequestUtc).TotalMilliseconds < 250)
                {
                    break;
                }

                _lastMenuRequestUtc = now;
                if (TrayNative.GetCursorPos(out var cursor))
                {
                    MenuRequested?.Invoke(cursor.X, cursor.Y);
                }

                break;
            }

            case TrayNative.WM_LBUTTONDBLCLK:
            case TrayNative.NIN_SELECT:
            case TrayNative.NIN_KEYSELECT:
            case TrayNative.WM_LBUTTONUP:
                ShowRequested?.Invoke();
                break;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            var data = CreateData();
            TrayNative.Shell_NotifyIcon(TrayNative.NIM_DELETE, ref data);
            TrayNative.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        _icons.Dispose();
        s_instance = null;
    }

    private sealed class TrayIcons : IDisposable
    {
        private readonly string _directory;
        private readonly Dictionary<TrayPhase, IntPtr> _loaded = new();
        private IntPtr _fallback;

        public TrayIcons(string directory)
        {
            _directory = directory;
        }

        public IntPtr Get(TrayPhase phase)
        {
            if (_loaded.TryGetValue(phase, out var cached) && cached != IntPtr.Zero)
            {
                return cached;
            }

            var fileName = phase switch
            {
                TrayPhase.Focus => "tray-focus.ico",
                TrayPhase.Break => "tray-break.ico",
                TrayPhase.Paused => "tray-paused.ico",
                _ => "tray-idle.ico"
            };

            var path = Path.Combine(_directory, fileName);
            var size = TrayNative.GetSystemMetrics(TrayNative.SM_CXSMICON);
            var handle = File.Exists(path)
                ? TrayNative.LoadImage(
                    IntPtr.Zero, path, TrayNative.IMAGE_ICON, size, size, TrayNative.LR_LOADFROMFILE)
                : IntPtr.Zero;

            if (handle == IntPtr.Zero)
            {
                if (_fallback == IntPtr.Zero)
                {
                    _fallback = TrayNative.LoadIcon(IntPtr.Zero, TrayNative.IDI_APPLICATION);
                }

                Log.Warn($"tray icon missing or failed to load: {path}");
                return _fallback;
            }

            _loaded[phase] = handle;
            return handle;
        }

        public void Dispose()
        {
            foreach (var icon in _loaded.Values)
            {
                if (icon != IntPtr.Zero)
                {
                    TrayNative.DestroyIcon(icon);
                }
            }

            _loaded.Clear();
        }
    }
}
