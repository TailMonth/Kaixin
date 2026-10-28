using System.IO;
using System.Runtime.InteropServices;
using KaixinPet.Core;
using KaixinPet.Core.Logging;

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
    bool DemoRunning,
    bool Topmost,
    string StatusText,
    TrayPhase Phase,
    bool IsSleeping,
    bool IsPlaying);

public sealed class TrayService : IDisposable
{
    private const string WindowClassName = "KaixinPet.TrayHost";
    private const uint TrayIconId = 1;
    private const uint CallbackMessage = TrayNative.WM_APP + 1;

    private const int MenuHeader = 1;
    private const int MenuToggleVisibility = 2001;
    private const int MenuDemo25 = 2002;
    private const int MenuDemo1 = 2003;
    private const int MenuStopDemo = 2004;
    private const int MenuTopmost = 2005;
    private const int MenuResetPosition = 2006;
    private const int MenuOpenData = 2007;
    private const int MenuOpenLogs = 2008;
    private const int MenuExit = 2009;
    private const int MenuPlay = 2010;
    private const int MenuSleepToggle = 2011;

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

    public event Action? ToggleVisibilityRequested;

    public event Action? Demo25Requested;

    public event Action? Demo1Requested;

    public event Action? DemoStopRequested;

    public event Action? PlayRequested;

    public event Action? SleepToggleRequested;

    public event Action<bool>? TopmostToggleRequested;

    public event Action? ResetPositionRequested;

    public event Action? OpenDataRequested;

    public event Action? OpenLogsRequested;

    public event Action? ExitRequested;

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

    private string BuildTip() => Truncate($"{_status.PetName} · {_status.StatusText}", 120);

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
                ShowMenu();
                break;

            case TrayNative.WM_LBUTTONDBLCLK:
            case TrayNative.NIN_SELECT:
            case TrayNative.NIN_KEYSELECT:
            case TrayNative.WM_LBUTTONUP:
                ShowRequested?.Invoke();
                break;
        }
    }

    private void ShowMenu()
    {
        if (_hwnd == IntPtr.Zero || _disposed)
        {
            return;
        }

        var menu = TrayNative.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            TrayNative.AppendMenu(
                menu,
                TrayNative.MF_STRING | TrayNative.MF_GRAYED,
                IntPtr.Zero,
                $"{_status.PetName} · {_status.StatusText}");
            TrayNative.AppendMenu(menu, TrayNative.MF_SEPARATOR, IntPtr.Zero, null);

            TrayNative.AppendMenu(
                menu,
                TrayNative.MF_STRING,
                new IntPtr(MenuToggleVisibility),
                _status.PetVisible ? "隐藏桌宠" : "显示桌宠");

            TrayNative.AppendMenu(
                menu,
                TrayNative.MF_STRING | (_status.DemoRunning ? TrayNative.MF_GRAYED : 0),
                new IntPtr(MenuDemo25),
                "开始计时演示 · 25 分钟");
            TrayNative.AppendMenu(
                menu,
                TrayNative.MF_STRING | (_status.DemoRunning ? TrayNative.MF_GRAYED : 0),
                new IntPtr(MenuDemo1),
                "开始计时演示 · 1 分钟");
            TrayNative.AppendMenu(
                menu,
                TrayNative.MF_STRING | (_status.DemoRunning ? 0 : TrayNative.MF_GRAYED),
                new IntPtr(MenuStopDemo),
                "结束计时演示");

            TrayNative.AppendMenu(menu, TrayNative.MF_SEPARATOR, IntPtr.Zero, null);
            TrayNative.AppendMenu(
                menu,
                TrayNative.MF_STRING | (_status.IsPlaying ? TrayNative.MF_GRAYED : 0),
                new IntPtr(MenuPlay),
                "陪它玩毛线球");
            TrayNative.AppendMenu(
                menu,
                TrayNative.MF_STRING,
                new IntPtr(MenuSleepToggle),
                _status.IsSleeping ? "叫醒它" : "让它睡觉");

            TrayNative.AppendMenu(menu, TrayNative.MF_SEPARATOR, IntPtr.Zero, null);
            TrayNative.AppendMenu(
                menu,
                TrayNative.MF_STRING | (_status.Topmost ? TrayNative.MF_CHECKED : TrayNative.MF_UNCHECKED),
                new IntPtr(MenuTopmost),
                "始终置顶");
            TrayNative.AppendMenu(menu, TrayNative.MF_STRING, new IntPtr(MenuResetPosition), "重置位置");

            TrayNative.AppendMenu(menu, TrayNative.MF_SEPARATOR, IntPtr.Zero, null);
            TrayNative.AppendMenu(menu, TrayNative.MF_STRING, new IntPtr(MenuOpenData), "打开数据目录");
            TrayNative.AppendMenu(menu, TrayNative.MF_STRING, new IntPtr(MenuOpenLogs), "打开日志目录");
            TrayNative.AppendMenu(menu, TrayNative.MF_SEPARATOR, IntPtr.Zero, null);
            TrayNative.AppendMenu(menu, TrayNative.MF_STRING, new IntPtr(MenuExit), "退出");

            TrayNative.GetCursorPos(out var cursor);
            TrayNative.SetForegroundWindow(_hwnd);
            var command = (int)TrayNative.TrackPopupMenu(
                menu,
                TrayNative.TPM_RETURNCMD | TrayNative.TPM_RIGHTBUTTON,
                cursor.X, cursor.Y, 0, _hwnd, IntPtr.Zero);

            TrayNative.PostMessage(_hwnd, TrayNative.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            HandleCommand(command);
        }
        finally
        {
            TrayNative.DestroyMenu(menu);
        }
    }

    private void HandleCommand(int command)
    {
        switch (command)
        {
            case MenuToggleVisibility:
                ToggleVisibilityRequested?.Invoke();
                break;
            case MenuDemo25:
                Demo25Requested?.Invoke();
                break;
            case MenuDemo1:
                Demo1Requested?.Invoke();
                break;
            case MenuStopDemo:
                DemoStopRequested?.Invoke();
                break;
            case MenuPlay:
                PlayRequested?.Invoke();
                break;
            case MenuSleepToggle:
                SleepToggleRequested?.Invoke();
                break;
            case MenuTopmost:
                TopmostToggleRequested?.Invoke(!_status.Topmost);
                break;
            case MenuResetPosition:
                ResetPositionRequested?.Invoke();
                break;
            case MenuOpenData:
                OpenDataRequested?.Invoke();
                break;
            case MenuOpenLogs:
                OpenLogsRequested?.Invoke();
                break;
            case MenuExit:
                ExitRequested?.Invoke();
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
