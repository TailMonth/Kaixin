using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using KaixinPet.Behavior;
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
    private const int MenuHide = 1007;
    private const int MenuResetPosition = 1008;
    private const int MenuPlay = 1009;
    private const int MenuSleepToggle = 1010;

    private static readonly object ClassGate = new();
    private static bool s_classRegistered;
    private static NativeMethods.WndProc? s_wndProc;
    private static PetWindow? s_instance;

    private readonly PetScene _scene;
    private readonly PetRenderer _renderer;
    private readonly PetAnimationPlayer _player;
    private readonly PetBehaviorController _behavior;
    private readonly FrameClock _clock;
    private readonly Stopwatch _startupClock;
    private readonly Action<double>? _onFirstFrame;
    private readonly string _petName;
    private readonly DispatcherTimer _behaviorTimer;
    private readonly DispatcherTimer _physicsTimer;
    private readonly Stopwatch _physicsClock = new();
    private readonly Stopwatch _dragClock = new();
    private readonly Queue<(TimeSpan Time, int Y)> _dragSamples = new();

    private IntPtr _hwnd;
    private uint _dpi = 96;
    private double _dpiScale = 1.0;
    private bool _topmost;
    private bool _visible;
    private bool _dragging;
    private bool _dragPending;
    private NativeMethods.POINT _dragStartCursor;
    private NativeMethods.RECT _dragStartRect;
    private TimeSpan _lastPhysicsTime;
    private bool _benchmarkMode;
    private AnimationFrameSet? _lastFrameSet;
    private bool _firstFrameReported;
    private bool _commitFailureLogged;
    private bool _disposed;

    private DispatcherTimer? _fallTimer;
    private readonly Stopwatch _fallClock = new();
    private TimeSpan _lastFallTime;
    private double _fallVelocityY;
    private bool _falling;

    public PetWindow(
        AssetCatalog assets,
        PetScene scene,
        Stopwatch startupClock,
        string petName,
        bool topmost,
        BehaviorOptions behaviorOptions,
        BehaviorConfig behaviorConfig,
        Action<double>? onFirstFrame = null)
    {
        _scene = scene;
        _startupClock = startupClock;
        _petName = string.IsNullOrWhiteSpace(petName) ? "开心" : petName.Trim();
        _topmost = topmost;
        _onFirstFrame = onFirstFrame;
        _renderer = new PetRenderer(assets, scene);
        _clock = new FrameClock(Dispatcher.CurrentDispatcher, OnTick, 10);

        _player = new PetAnimationPlayer(assets.Character);
        _behavior = new PetBehaviorController(_player, behaviorConfig, behaviorOptions);
        _behaviorTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _behaviorTimer.Tick += (_, _) =>
        {
            _behavior.Tick();
            UpdateSceneFromBehavior();
            UpdateBehaviorTimers();
        };
        _physicsTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _physicsTimer.Tick += OnPhysicsTick;

        _player.BaseClipFinished += OnBaseClipFinished;
        _behavior.StateChanged += OnBehaviorStateChanged;
        _behavior.Start();
    }

    public event Action<int>? DemoRequested;

    public event Action? DemoStopRequested;

    public event Action? ExitRequested;

    public event Action? OpenLogsRequested;

    public event Action? HideRequested;

    public event Action<bool>? DragStateChanged;

    public event Action? BoundsChanged;

    public event Action? PositionChanged;

    public event Action<bool>? VisibilityChanged;

    public event Action<bool>? TopmostChanged;

    public event Action? FirstFrame;

    public IntPtr Handle => _hwnd;

    public double DpiScale => _dpiScale;

    public int WidthPx => _renderer.WidthPx;

    public int HeightPx => _renderer.HeightPx;

    public bool IsTopmost => _topmost;

    public bool IsWindowVisible => _visible;

    public FrameClock Clock => _clock;

    public PetBehaviorState BehaviorState => _behavior.State;

    public bool IsSleeping => _behavior.IsSleeping;

    public bool IsPlaying => _behavior.IsPlaying;

    public void SetBenchmarkMode(bool enabled)
    {
        _benchmarkMode = enabled;
        if (enabled)
        {
            _clock.TargetFps = 24;
        }
        else
        {
            UpdateBehaviorTimers();
        }
    }

    public void StartPlay()
    {
        _behavior.StartPlay();
        UpdateSceneFromBehavior();
        UpdateBehaviorTimers();
    }

    public void ToggleSleep()
    {
        _behavior.ToggleSleep();
        UpdateSceneFromBehavior();
        UpdateBehaviorTimers();
    }

    private void OnBaseClipFinished(string clip)
    {
        _behavior.OnClipFinished(clip);
        UpdateSceneFromBehavior();
        UpdateBehaviorTimers();
    }

    private void OnBehaviorStateChanged()
    {
        Log.Info($"behavior state: {_behavior.State}");
        UpdateBehaviorTimers();
    }

    private void UpdateSceneFromBehavior()
    {
        _scene.SetCatOffset(_behavior.CatOffsetXDip);
        _scene.SetSleeping(_behavior.State == PetBehaviorState.Sleep);
        if (_behavior.Ball is { } ball)
        {
            _scene.SetBall(ball.X, ball.Y, ball.Frame);
        }
        else
        {
            _scene.ClearBall();
        }
    }

    private void UpdateBehaviorTimers()
    {
        if (!_benchmarkMode)
        {
            _clock.TargetFps = _behavior.State is
                PetBehaviorState.Play or PetBehaviorState.Drag or PetBehaviorState.Fall
                ? 24
                : 10;
        }

        if (_behavior.IsPlaying)
        {
            if (!_physicsTimer.IsEnabled)
            {
                _physicsClock.Restart();
                _lastPhysicsTime = TimeSpan.Zero;
                _physicsTimer.Start();
            }
        }
        else if (_physicsTimer.IsEnabled)
        {
            _physicsTimer.Stop();
        }
    }

    private void OnPhysicsTick(object? sender, EventArgs e)
    {
        var now = _physicsClock.Elapsed;
        var dt = (now - _lastPhysicsTime).TotalSeconds;
        _lastPhysicsTime = now;
        if (dt <= 0)
        {
            return;
        }

        if (dt > 0.1)
        {
            dt = 0.1;
        }

        _behavior.UpdatePhysics(dt);
        UpdateSceneFromBehavior();

        if (!_behavior.IsPlaying && _physicsTimer.IsEnabled)
        {
            _physicsTimer.Stop();
            UpdateBehaviorTimers();
        }
    }

    public PixelRect GetBoundsPx()
    {
        if (_hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(_hwnd, out var rect))
        {
            return new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height);
        }

        return default;
    }

    public void Show(PixelRect? restorePosition = null)
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

        PixelRect? candidate = null;
        var restored = false;
        if (restorePosition is { } saved)
        {
            var proposed = new PixelRect(saved.Left, saved.Top, width, height);
            if (IsRestoredPositionUsable(proposed))
            {
                candidate = proposed;
                restored = true;
            }
            else
            {
                Log.Info($"saved position ({saved.Left},{saved.Top}) is off-screen; using default");
            }
        }

        var left = candidate?.Left ?? (work.Right - width - (int)Math.Round(24 * systemScale));
        var top = candidate?.Top ?? (work.Bottom - height - (int)Math.Round(8 * systemScale));

        var exStyle = NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW |
                      NativeMethods.WS_EX_NOACTIVATE;
        if (_topmost)
        {
            exStyle |= NativeMethods.WS_EX_TOPMOST;
        }

        var instance = NativeMethods.GetModuleHandle(null);
        _hwnd = NativeMethods.CreateWindowEx(
            exStyle,
            WindowClassName,
            _petName,
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

        if (restored)
        {
            var restoredWork = ScreenUtil.GetWorkAreaForRect(new PixelRect(left, top, WidthPx, HeightPx));
            left = Math.Clamp(left, restoredWork.Left, Math.Max(restoredWork.Left, restoredWork.Right - WidthPx));
            top = Math.Clamp(top, restoredWork.Top, Math.Max(restoredWork.Top, restoredWork.Bottom - HeightPx));
        }
        else
        {
            var windowWork = ScreenUtil.GetWorkAreaForRect(new PixelRect(left, top, width, height));
            left = windowWork.Right - WidthPx - (int)Math.Round(24 * _dpiScale);
            top = windowWork.Bottom - HeightPx - (int)Math.Round(8 * _dpiScale);
        }

        _lastFrameSet = _player.Advance(TimeSpan.Zero);
        _renderer.Render(_lastFrameSet);
        if (!_renderer.Commit(_hwnd, withBounds: true, left, top))
        {
            Log.Warn($"首次提交 UpdateLayeredWindow 失败：{Marshal.GetLastWin32Error()}");
        }

        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        _visible = true;
        _clock.Start();
        _behaviorTimer.Start();
        UpdateSceneFromBehavior();
        UpdateBehaviorTimers();

        Log.Info(
            $"pet window created: hwnd=0x{_hwnd.ToInt64():X}, dpi={_dpi} ({_dpiScale:0.##}x), " +
            $"size={WidthPx}x{HeightPx}px, pos=({left},{top}), restored={restored}, topmost={_topmost}");

        ReportFirstFrame();
    }

    private static bool IsRestoredPositionUsable(PixelRect rect)
    {
        var work = ScreenUtil.GetWorkAreaForRect(rect);
        var overlapWidth = Math.Min(rect.Right, work.Right) - Math.Max(rect.Left, work.Left);
        var overlapHeight = Math.Min(rect.Bottom, work.Bottom) - Math.Max(rect.Top, work.Top);
        return overlapWidth >= 64 && overlapHeight >= 64;
    }

    public void SetTopmost(bool topmost)
    {
        if (_topmost == topmost)
        {
            return;
        }

        _topmost = topmost;
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(
                _hwnd,
                topmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        TopmostChanged?.Invoke(topmost);
    }

    public void SetVisible(bool visible)
    {
        if (_hwnd == IntPtr.Zero || _visible == visible)
        {
            return;
        }

        if (visible)
        {
            var bounds = GetBoundsPx();
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            _visible = true;
            _lastFrameSet ??= _player.Advance(TimeSpan.Zero);
            _renderer.Render(_lastFrameSet);
            if (!_renderer.Commit(_hwnd, withBounds: true, bounds.Left, bounds.Top))
            {
                Log.Warn($"恢复显示时提交失败：{Marshal.GetLastWin32Error()}");
            }

            _behaviorTimer.Start();
            UpdateBehaviorTimers();
        }
        else
        {
            _visible = false;
            _dragPending = false;
            _dragSamples.Clear();

            if ((_dragging || _falling) && _hwnd != IntPtr.Zero)
            {
                SetWindowPosY(GetGroundTop(GetBoundsPx()));
            }

            if (_dragging)
            {
                _dragging = false;
                _dragPending = false;
                NativeMethods.ReleaseCapture();
                _scene.SetDragging(false);
                _behavior.Landed();
                UpdateSceneFromBehavior();
                UpdateBehaviorTimers();
                DragStateChanged?.Invoke(false);
            }

            if (_falling)
            {
                _falling = false;
                _fallTimer?.Stop();
                _scene.SetDragging(false);
                _behavior.Landed();
            }

            _behaviorTimer.Stop();
            _physicsTimer.Stop();
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
        }

        Log.Info($"pet window visibility changed: {visible}");
        VisibilityChanged?.Invoke(visible);
    }

    public void BringToFront()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!_visible)
        {
            SetVisible(true);
        }

        NativeMethods.SetWindowPos(
            _hwnd,
            _topmost ? NativeMethods.HWND_TOPMOST : IntPtr.Zero,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    public void ResetPosition()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var work = ScreenUtil.GetWorkAreaForWindow(_hwnd);
        var left = work.Right - WidthPx - (int)Math.Round(24 * _dpiScale);
        var top = work.Bottom - HeightPx - (int)Math.Round(8 * _dpiScale);

        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, left, top, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        Log.Info($"pet position reset: ({left},{top})");
        BoundsChanged?.Invoke();
        PositionChanged?.Invoke();
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
                OnMouseDown();
                return IntPtr.Zero;

            case NativeMethods.WM_MOUSEMOVE:
                OnMouseMove();
                return IntPtr.Zero;

            case NativeMethods.WM_LBUTTONUP:
                OnMouseUp();
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

    private void OnMouseDown()
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

        if (_falling)
        {
            _fallTimer?.Stop();
            SetWindowPosY(GetGroundTop(GetBoundsPx()));
            Land();
        }

        _dragPending = true;
        _dragStartCursor = cursor;
        NativeMethods.GetWindowRect(_hwnd, out _dragStartRect);
        NativeMethods.SetCapture(_hwnd);
        _dragClock.Restart();
        _dragSamples.Clear();
        _dragSamples.Enqueue((TimeSpan.Zero, cursor.Y));
    }

    private void OnMouseMove()
    {
        NativeMethods.GetCursorPos(out var cursor);

        if (_dragPending && !_dragging)
        {
            var dx = cursor.X - _dragStartCursor.X;
            var dy = cursor.Y - _dragStartCursor.Y;
            if (dx * dx + dy * dy >= 16)
            {
                _dragPending = false;
                _dragging = true;
                _scene.SetDragging(true);
                _behavior.BeginDrag();
                UpdateSceneFromBehavior();
                UpdateBehaviorTimers();
                DragStateChanged?.Invoke(true);
                Log.Info("pet drag started");
            }
        }

        if (!_dragging)
        {
            return;
        }

        DragToCursor(cursor);
        _dragSamples.Enqueue((_dragClock.Elapsed, cursor.Y));
        while (_dragSamples.Count > 1 &&
               (_dragClock.Elapsed - _dragSamples.Peek().Time).TotalMilliseconds > 120)
        {
            _dragSamples.Dequeue();
        }
    }

    private void DragToCursor(NativeMethods.POINT cursor)
    {
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

    private void OnMouseUp()
    {
        if (_dragging)
        {
            EndDragAndStartFall();
            return;
        }

        if (!_dragPending)
        {
            return;
        }

        _dragPending = false;
        NativeMethods.ReleaseCapture();
        _behavior.Pet();
        UpdateSceneFromBehavior();
        UpdateBehaviorTimers();
    }

    private void EndDragAndStartFall()
    {
        _dragging = false;
        _dragPending = false;
        NativeMethods.ReleaseCapture();

        var initialVelocity = ComputeDragVelocity();
        _behavior.EndDrag();
        _scene.SetDragging(true);
        UpdateSceneFromBehavior();
        UpdateBehaviorTimers();

        Log.Info($"pet released; fall velocity={initialVelocity:0}px/s");
        StartFall(initialVelocity);
    }

    private double ComputeDragVelocity()
    {
        if (_dragSamples.Count < 2)
        {
            return 0;
        }

        var first = _dragSamples.Peek();
        var last = _dragSamples.Last();
        var seconds = (last.Time - first.Time).TotalSeconds;
        if (seconds < 0.03)
        {
            return 0;
        }

        return Math.Clamp((last.Y - first.Y) / seconds, -2500, 3000);
    }

    private void StartFall(double initialVelocity)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var bounds = GetBoundsPx();
        var groundTop = GetGroundTop(bounds);
        if (bounds.Top >= groundTop)
        {
            SetWindowPosY(groundTop);
            Land();
            return;
        }

        _falling = true;
        _fallVelocityY = initialVelocity;
        _fallClock.Restart();
        _lastFallTime = TimeSpan.Zero;

        if (_fallTimer is null)
        {
            _fallTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _fallTimer.Tick += OnFallTick;
        }

        _fallTimer.Start();
    }

    private void OnFallTick(object? sender, EventArgs e)
    {
        var now = _fallClock.Elapsed;
        var dt = (now - _lastFallTime).TotalSeconds;
        _lastFallTime = now;
        if (dt <= 0)
        {
            return;
        }

        if (dt > 0.05)
        {
            dt = 0.05;
        }

        _fallVelocityY += 1600 * _dpiScale * dt;
        var bounds = GetBoundsPx();
        var work = ScreenUtil.GetWorkAreaForRect(bounds);
        var groundTop = GetGroundTop(bounds);
        var top = bounds.Top + (int)Math.Round(_fallVelocityY * dt);

        if (top >= groundTop)
        {
            SetWindowPosY(groundTop);
            Land();
            return;
        }

        if (top < work.Top)
        {
            top = work.Top;
            if (_fallVelocityY < 0)
            {
                _fallVelocityY = 0;
            }
        }

        SetWindowPosY(top);
    }

    private int GetGroundTop(PixelRect bounds)
    {
        var work = ScreenUtil.GetWorkAreaForRect(bounds);
        return work.Bottom - HeightPx - (int)Math.Round(8 * _dpiScale);
    }

    private void SetWindowPosY(int top)
    {
        var bounds = GetBoundsPx();
        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, bounds.Left, top, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private void Land()
    {
        if (!_falling)
        {
            return;
        }

        _falling = false;
        _fallTimer?.Stop();
        _scene.SetDragging(false);
        _behavior.Landed();
        UpdateSceneFromBehavior();
        UpdateBehaviorTimers();

        DragStateChanged?.Invoke(false);
        BoundsChanged?.Invoke();
        PositionChanged?.Invoke();
        Log.Info("pet landed");
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

        _renderer.Render(_lastFrameSet ?? _player.Advance(TimeSpan.Zero));
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
        PositionChanged?.Invoke();
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
        if (_dragging || _dragPending || _falling)
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

            var status = _behavior.State switch
            {
                PetBehaviorState.Sleep => "睡觉中",
                PetBehaviorState.Play => "玩耍中",
                PetBehaviorState.Drag => "被拎起",
                PetBehaviorState.Fall => "下落中",
                _ => _scene.TimerVisible ? "专注演示中" : "空闲"
            };
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING | NativeMethods.MF_GRAYED,
                new IntPtr(MenuHeader), $"{_petName} · {status}");
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
                NativeMethods.MF_STRING |
                (_behavior.State is PetBehaviorState.Play or PetBehaviorState.Drag or PetBehaviorState.Fall
                    ? NativeMethods.MF_GRAYED
                    : 0),
                new IntPtr(MenuPlay),
                "陪它玩毛线球");
            NativeMethods.AppendMenu(
                menu,
                NativeMethods.MF_STRING |
                (_behavior.State is PetBehaviorState.Drag or PetBehaviorState.Fall
                    ? NativeMethods.MF_GRAYED
                    : 0),
                new IntPtr(MenuSleepToggle),
                _behavior.State == PetBehaviorState.Sleep ? "叫醒它" : "让它睡觉");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, IntPtr.Zero, null);
            NativeMethods.AppendMenu(
                menu,
                NativeMethods.MF_STRING | (_topmost ? NativeMethods.MF_CHECKED : NativeMethods.MF_UNCHECKED),
                new IntPtr(MenuTopmost),
                "始终置顶");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new IntPtr(MenuResetPosition), "重置位置");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new IntPtr(MenuHide), "隐藏桌宠");
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
            case MenuPlay:
                StartPlay();
                break;
            case MenuSleepToggle:
                ToggleSleep();
                break;
            case MenuTopmost:
                SetTopmost(!_topmost);
                break;
            case MenuResetPosition:
                ResetPosition();
                break;
            case MenuHide:
                HideRequested?.Invoke();
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
        if (_hwnd == IntPtr.Zero || _disposed || !_visible)
        {
            return;
        }

        try
        {
            _lastFrameSet = _player.Advance(now);
            _renderer.Render(_lastFrameSet);
            UpdateSceneFromBehavior();
            UpdateBehaviorTimers();

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
        _visible = false;
        _behaviorTimer.Stop();
        _physicsTimer.Stop();
        _fallTimer?.Stop();
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
