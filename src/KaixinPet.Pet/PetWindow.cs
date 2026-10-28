using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using KaixinPet.Core.Logging;
using KaixinPet.Pet.Animation;
using KaixinPet.Pet.Assets;
using KaixinPet.Pet.Native;
using KaixinPet.Pet.Rendering;
using KaixinPet.Pet.Scene;

namespace KaixinPet.Pet;

public sealed class PetWindow : IDisposable
{
    private const string WindowClassName = "KaixinPet.LayeredPetWindow";

    private const int MenuHeader = 1;
    private const int MenuDemo25 = 1001;
    private const int MenuDemo1 = 1002;
    private const int MenuStopDemo = 1003;
    private const int MenuTopmost = 1004;
    private const int MenuOpenLogs = 1005;
    private const int MenuExit = 1006;

    private static readonly object ClassGate = new();
    private static bool s_classRegistered;
    private static NativeMethods.WndProc? s_wndProc;
    private static PetWindow? s_instance;

    private readonly PetScene _scene;
    private readonly PetRenderer _renderer;
    private readonly PetAnimator _animator;
    private readonly FrameClock _clock;
    private readonly Stopwatch _startupClock;
    private readonly Action<double>? _onFirstFrame;

    private IntPtr _hwnd;
    private uint _dpi = 96;
    private double _dpiScale = 1.0;
    private bool _topmost = true;
    private bool _dragging;
    private NativeMethods.POINT _dragStartCursor;
    private NativeMethods.RECT _dragStartRect;
    private bool _firstFrameReported;
    private bool _commitFailureLogged;
    private bool _disposed;

    public PetWindow(
        AssetCatalog assets,
        PetScene scene,
        Stopwatch startupClock,
        Action<double>? onFirstFrame = null)
    {
        _scene = scene;
        _startupClock = startupClock;
        _onFirstFrame = onFirstFrame;
        _renderer = new PetRenderer(assets, scene);
        _animator = new PetAnimator(assets.Character);
        _clock = new FrameClock(Dispatcher.CurrentDispatcher, OnTick, 10);
    }

    public event Action<int>? DemoRequested;

    public event Action? DemoStopRequested;

    public event Action? ExitRequested;

    public event Action? OpenLogsRequested;

    public event Action<bool>? DragStateChanged;

    public event Action? BoundsChanged;

    public event Action<bool>? TopmostChanged;

    public event Action? FirstFrame;

    public IntPtr Handle => _hwnd;

    public double DpiScale => _dpiScale;

    public int WidthPx => _renderer.WidthPx;

    public int HeightPx => _renderer.HeightPx;

    public bool IsTopmost => _topmost;

    public FrameClock Clock => _clock;

    public void SetBenchmarkMode(bool enabled) => _animator.BenchmarkMode = enabled;

    public PixelRect GetBoundsPx()
    {
        if (_hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(_hwnd, out var rect))
        {
            return new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height);
        }

        return default;
    }

    public void Show()
    {
        s_instance = this;
        RegisterWindowClass();

        var systemDpi = NativeMethods.GetDpiForSystem();
        if (systemDpi == 0)
        {
            systemDpi = 96;
        }

        var systemScale = systemDpi / 96.0;
        var width = (int)Math.Round(SceneLayout.WindowWidthDip * systemScale);
        var height = (int)Math.Round(SceneLayout.WindowHeightDip * systemScale);
        var work = ScreenUtil.GetPrimaryWorkArea();
        var left = work.Right - width - (int)Math.Round(24 * systemScale);
        var top = work.Bottom - height - (int)Math.Round(8 * systemScale);

        var instance = NativeMethods.GetModuleHandle(null);
        _hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW |
            NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST,
            WindowClassName,
            "开心",
            NativeMethods.WS_POPUP,
            left,
            top,
            width,
            height,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建桌宠分层窗口失败");
        }

        _dpi = NativeMethods.GetDpiForWindow(_hwnd);
        if (_dpi == 0)
        {
            _dpi = 96;
        }

        _dpiScale = _dpi / 96.0;
        _renderer.SetDpi(_dpiScale);

        var windowWork = ScreenUtil.GetWorkAreaForRect(new PixelRect(left, top, width, height));
        left = windowWork.Right - WidthPx - (int)Math.Round(24 * _dpiScale);
        top = windowWork.Bottom - HeightPx - (int)Math.Round(8 * _dpiScale);

        _renderer.Render(new AnimationFrameState(0, -1));
        if (!_renderer.Commit(_hwnd, withBounds: true, left, top))
        {
            Log.Warn($"首次提交 UpdateLayeredWindow 失败：{Marshal.GetLastWin32Error()}");
        }

        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        _clock.Start();

        Log.Info(
            $"pet window created: hwnd=0x{_hwnd.ToInt64():X}, dpi={_dpi} ({_dpiScale:0.##}x), " +
            $"size={WidthPx}x{HeightPx}px, pos=({left},{top}), work={windowWork.Left},{windowWork.Top},{windowWork.Right},{windowWork.Bottom}");

        ReportFirstFrame();
    }

    public void SetTopmost(bool topmost)
    {
        if (_topmost == topmost)
        {
            return;
        }

        _topmost = topmost;
        NativeMethods.SetWindowPos(
            _hwnd,
            topmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        TopmostChanged?.Invoke(topmost);
    }

    private static void RegisterWindowClass()
    {
        lock (ClassGate)
        {
            if (s_classRegistered)
            {
                return;
            }

            s_wndProc = StaticWndProc;

            var windowClass = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = s_wndProc,
                hInstance = NativeMethods.GetModuleHandle(null),
                hCursor = NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_ARROW),
                lpszClassName = WindowClassName
            };

            if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "注册桌宠窗口类失败");
            }

            s_classRegistered = true;
        }
    }

    private static IntPtr StaticWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var instance = s_instance;
        if (instance is not null && instance._hwnd == hwnd)
        {
            return instance.HandleMessage(hwnd, msg, wParam, lParam);
        }

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private IntPtr HandleMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_NCHITTEST:
                return HitTest(lParam);

            case NativeMethods.WM_LBUTTONDOWN:
                BeginDrag();
                return IntPtr.Zero;

            case NativeMethods.WM_MOUSEMOVE:
                if (_dragging)
                {
                    DragToCursor();
                }

                return IntPtr.Zero;

            case NativeMethods.WM_LBUTTONUP:
                EndDrag();
                return IntPtr.Zero;

            case NativeMethods.WM_RBUTTONUP:
                ShowMenu();
                return IntPtr.Zero;

            case NativeMethods.WM_DPICHANGED:
                OnDpiChanged(wParam, lParam);
                return IntPtr.Zero;

            case NativeMethods.WM_DISPLAYCHANGE:
                ClampToWorkArea();
                BoundsChanged?.Invoke();
                return IntPtr.Zero;

            case NativeMethods.WM_ERASEBKGND:
                return (IntPtr)1;

            case NativeMethods.WM_PAINT:
                NativeMethods.ValidateRect(hwnd, IntPtr.Zero);
                return IntPtr.Zero;

            default:
                return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }

    private int HitTest(IntPtr lParam)
    {
        var raw = lParam.ToInt64();
        var point = new NativeMethods.POINT(
            unchecked((short)(raw & 0xFFFF)),
            unchecked((short)((raw >> 16) & 0xFFFF)));

        NativeMethods.ScreenToClient(_hwnd, ref point);

        if (_scene.IsPointInHourglass(point.X, point.Y, _dpiScale))
        {
            return NativeMethods.HTCLIENT;
        }

        return _renderer.GetAlpha(point.X, point.Y) >= SceneLayout.AlphaHitThreshold
            ? NativeMethods.HTCLIENT
            : NativeMethods.HTTRANSPARENT;
    }

    private void BeginDrag()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var client = cursor;
        NativeMethods.ScreenToClient(_hwnd, ref client);

        if (_scene.IsPointInHourglass(client.X, client.Y, _dpiScale))
        {
            return;
        }

        if (_renderer.GetAlpha(client.X, client.Y) < SceneLayout.AlphaHitThreshold)
        {
            return;
        }

        _dragging = true;
        _dragStartCursor = cursor;
        NativeMethods.GetWindowRect(_hwnd, out _dragStartRect);
        NativeMethods.SetCapture(_hwnd);
        _scene.SetDragging(true);
        DragStateChanged?.Invoke(true);
    }

    private void DragToCursor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var left = _dragStartRect.Left + (cursor.X - _dragStartCursor.X);
        var top = _dragStartRect.Top + (cursor.Y - _dragStartCursor.Y);

        var proposed = new PixelRect(left, top, WidthPx, HeightPx);
        var work = ScreenUtil.GetWorkAreaForRect(proposed);
        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - WidthPx));
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - HeightPx));

        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, left, top, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        BoundsChanged?.Invoke();
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        NativeMethods.ReleaseCapture();
        _scene.SetDragging(false);
        DragStateChanged?.Invoke(false);
        BoundsChanged?.Invoke();
    }

    private void OnDpiChanged(IntPtr wParam, IntPtr lParam)
    {
        var newDpi = (uint)(wParam.ToInt32() & 0xFFFF);
        if (newDpi == 0)
        {
            newDpi = 96;
        }

        var suggested = Marshal.PtrToStructure<NativeMethods.RECT>(lParam);
        _dpi = newDpi;
        _dpiScale = newDpi / 96.0;
        _renderer.SetDpi(_dpiScale);

        var left = suggested.Left;
        var top = suggested.Top;
        var work = ScreenUtil.GetWorkAreaForRect(new PixelRect(left, top, WidthPx, HeightPx));
        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - WidthPx));
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - HeightPx));

        _renderer.Render(new AnimationFrameState(0, -1));
        if (!_renderer.Commit(_hwnd, withBounds: true, left, top))
        {
            Log.Warn($"DPI 变化后提交失败：{Marshal.GetLastWin32Error()}");
        }

        if (_dragging)
        {
            NativeMethods.GetCursorPos(out _dragStartCursor);
            NativeMethods.GetWindowRect(_hwnd, out _dragStartRect);
        }

        Log.Info($"dpi changed: {_dpi} ({_dpiScale:0.##}x), size={WidthPx}x{HeightPx}px, pos=({left},{top})");
        BoundsChanged?.Invoke();
    }

    private void ClampToWorkArea()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var bounds = GetBoundsPx();
        var work = ScreenUtil.GetWorkAreaForRect(bounds);
        var left = Math.Clamp(bounds.Left, work.Left, Math.Max(work.Left, work.Right - bounds.Width));
        var top = Math.Clamp(bounds.Top, work.Top, Math.Max(work.Top, work.Bottom - bounds.Height));

        if (left != bounds.Left || top != bounds.Top)
        {
            NativeMethods.SetWindowPos(
                _hwnd, IntPtr.Zero, left, top, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
    }

    private void ShowMenu()
    {
        if (_dragging)
        {
            return;
        }

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.GetCursorPos(out var cursor);

            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING | NativeMethods.MF_GRAYED,
                new IntPtr(MenuHeader), "开心 · M0 技术验证");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, IntPtr.Zero, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new IntPtr(MenuDemo25), "开始计时演示 · 25 分钟");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new IntPtr(MenuDemo1), "开始计时演示 · 1 分钟");
            NativeMethods.AppendMenu(
                menu,
                NativeMethods.MF_STRING | (_scene.TimerVisible ? 0 : NativeMethods.MF_GRAYED),
                new IntPtr(MenuStopDemo),
                "结束计时演示");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, IntPtr.Zero, null);
            NativeMethods.AppendMenu(
                menu,
                NativeMethods.MF_STRING | (_topmost ? NativeMethods.MF_CHECKED : NativeMethods.MF_UNCHECKED),
                new IntPtr(MenuTopmost),
                "始终置顶");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, IntPtr.Zero, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new IntPtr(MenuOpenLogs), "打开日志目录");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new IntPtr(MenuExit), "退出");

            NativeMethods.SetForegroundWindow(_hwnd);
            var command = (int)NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON,
                cursor.X, cursor.Y, 0, _hwnd, IntPtr.Zero);

            NativeMethods.PostMessage(_hwnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            HandleMenuCommand(command);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private void HandleMenuCommand(int command)
    {
        switch (command)
        {
            case MenuDemo25:
                DemoRequested?.Invoke(25);
                break;
            case MenuDemo1:
                DemoRequested?.Invoke(1);
                break;
            case MenuStopDemo:
                DemoStopRequested?.Invoke();
                break;
            case MenuTopmost:
                SetTopmost(!_topmost);
                break;
            case MenuOpenLogs:
                OpenLogsRequested?.Invoke();
                break;
            case MenuExit:
                ExitRequested?.Invoke();
                break;
        }
    }

    private void OnTick(TimeSpan now)
    {
        if (_hwnd == IntPtr.Zero || _disposed)
        {
            return;
        }

        try
        {
            var state = _animator.Advance(now);
            _renderer.Render(state);

            if (!_renderer.Commit(_hwnd, withBounds: false, 0, 0) && !_commitFailureLogged)
            {
                _commitFailureLogged = true;
                Log.Warn($"UpdateLayeredWindow 提交失败：{Marshal.GetLastWin32Error()}");
            }

            ReportFirstFrame();
        }
        catch (Exception exception)
        {
            Log.Error("渲染周期失败", exception);
        }
    }

    private void ReportFirstFrame()
    {
        if (_firstFrameReported)
        {
            return;
        }

        _firstFrameReported = true;
        var elapsedMs = _startupClock.Elapsed.TotalMilliseconds;
        Log.Info($"first frame committed (managed clock): {elapsedMs:0} ms");
        _onFirstFrame?.Invoke(elapsedMs);
        FirstFrame?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clock.Dispose();

        if (_dragging)
        {
            NativeMethods.ReleaseCapture();
        }

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        _renderer.Dispose();
        s_instance = null;
    }
}
