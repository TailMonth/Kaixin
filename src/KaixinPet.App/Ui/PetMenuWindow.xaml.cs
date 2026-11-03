using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KaixinPet.Pet.Native;

namespace KaixinPet.App.Ui;

public sealed record PetMenuState(
    string PetName,
    string StatusText,
    bool TimerActive,
    bool Paused,
    bool FocusPhase,
    bool IsSleeping,
    bool Busy,
    bool Topmost,
    bool Muted,
    int FocusMinutes,
    int ShortBreakMinutes,
    bool PetVisible = true,
    bool ShowDataFolder = false);

public partial class PetMenuWindow : Window
{
    private const int VkEscape = 0x1B;
    private const int VkLeftButton = 0x01;
    private const int VkRightButton = 0x02;
    private const int VkMiddleButton = 0x04;

    private readonly DispatcherTimer _closeWatcher;
    private DateTime _openedAt;

    public PetMenuWindow()
    {
        InitializeComponent();
        Left = -10000;
        Top = -10000;
        _closeWatcher = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(60)
        };
        _closeWatcher.Tick += (_, _) => WatchForOutsideClose();
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => _closeWatcher.Stop();
    }

    public bool ClosedByOutsideClick { get; private set; }

    public DateTime OutsideCloseUtc { get; private set; }

    public int OutsideCloseX { get; private set; }

    public int OutsideCloseY { get; private set; }

    public event Action? StartFocusRequested;

    public event Action? StartBreakRequested;

    public event Action? PauseResumeRequested;

    public event Action? SkipRequested;

    public event Action? ResetRequested;

    public event Action? SleepRequested;

    public event Action? TopmostRequested;

    public event Action? ResetPositionRequested;

    public event Action? VisibilityToggleRequested;

    public event Action? SettingsRequested;

    public event Action? MuteRequested;

    public event Action? OpenDataRequested;

    public event Action? OpenLogsRequested;

    public event Action? ExitRequested;

    public void Bind(PetMenuState state)
    {
        MenuStatusText.Text = $"{state.PetName} · {state.StatusText}";
        StartFocusText.Text = $"开始专注 · {state.FocusMinutes} 分钟";
        StartBreakText.Text = $"休息 · {state.ShortBreakMinutes} 分钟";
        TimerIdleGroup.Visibility = state.TimerActive ? Visibility.Collapsed : Visibility.Visible;
        TimerActiveGroup.Visibility = state.TimerActive ? Visibility.Visible : Visibility.Collapsed;

        PauseResumeText.Text = state.Paused ? "继续计时" : "暂停计时";
        PauseIcon.Data = Geometry.Parse(
            state.Paused ? "M8,5 L19,12 L8,19 Z" : "M8,5 L8,19 M16,5 L16,19");
        ResetText.Text = state.FocusPhase ? "结束本次专注" : "结束休息";

        SleepButtonText.Text = state.IsSleeping ? "叫醒它" : "让它睡觉";
        SleepButton.IsEnabled = !state.Busy;

        VisibilityButtonText.Text = state.PetVisible ? "隐藏桌宠" : "显示桌宠";
        AutomationProperties.SetName(VisibilityButton, VisibilityButtonText.Text);
        OpenDataButton.Visibility = state.ShowDataFolder ? Visibility.Visible : Visibility.Collapsed;

        TopmostCheckMark.Visibility = state.Topmost ? Visibility.Visible : Visibility.Collapsed;
        MuteCheckMark.Visibility = state.Muted ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowNearCursor(int cursorX, int cursorY)
    {
        var animate = SystemParameters.ClientAreaAnimation;
        BeginAnimation(OpacityProperty, null);
        Opacity = animate ? 0 : 1;

        Show();
        UpdateLayout();

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        WindowInterop.MakeNonActivating(hwnd);
        WindowInterop.SetClickThrough(hwnd, false);

        var size = WindowInterop.GetBoundsPx(hwnd);
        var work = ScreenUtil.GetWorkAreaForRect(new PixelRect(cursorX, cursorY, 1, 1));
        var left = cursorX + 12;
        if (left + size.Width > work.Right - 8)
        {
            left = cursorX - size.Width - 12;
        }

        var top = cursorY + 12;
        if (top + size.Height > work.Bottom - 8)
        {
            top = cursorY - size.Height - 12;
        }

        left = Math.Clamp(left, work.Left + 8, Math.Max(work.Left + 8, work.Right - size.Width - 8));
        top = Math.Clamp(top, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - size.Height - 8));
        WindowInterop.Move(hwnd, left, top);

        if (animate)
        {
            BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
                {
                    FillBehavior = FillBehavior.HoldEnd,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }

        _ = GetAsyncKeyState(VkEscape);
        _ = GetAsyncKeyState(VkLeftButton);
        _ = GetAsyncKeyState(VkRightButton);
        _ = GetAsyncKeyState(VkMiddleButton);

        _openedAt = DateTime.UtcNow;
        _closeWatcher.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowInterop.MakeNonActivating(hwnd);
    }

    private void WatchForOutsideClose()
    {
        if (!IsVisible)
        {
            _closeWatcher.Stop();
            return;
        }

        if ((DateTime.UtcNow - _openedAt).TotalMilliseconds < 150)
        {
            return;
        }

        if ((GetAsyncKeyState(VkEscape) & 0x0001) != 0)
        {
            Close();
            return;
        }

        var mousePressed = (GetAsyncKeyState(VkLeftButton) & 0x0001) != 0 ||
                           (GetAsyncKeyState(VkRightButton) & 0x0001) != 0 ||
                           (GetAsyncKeyState(VkMiddleButton) & 0x0001) != 0;
        if (!mousePressed)
        {
            return;
        }

        CursorUtil.GetPosition(out var x, out var y);
        var bounds = WindowInterop.GetBoundsPx(new WindowInteropHelper(this).Handle);
        var inside = x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;
        if (!inside)
        {
            ClosedByOutsideClick = true;
            OutsideCloseUtc = DateTime.UtcNow;
            OutsideCloseX = x;
            OutsideCloseY = y;
            Close();
        }
    }

    private void OnStartFocusClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(StartFocusRequested);

    private void OnStartBreakClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(StartBreakRequested);

    private void OnPauseResumeClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(PauseResumeRequested);

    private void OnSkipClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(SkipRequested);

    private void OnResetClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(ResetRequested);

    private void OnSleepClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(SleepRequested);

    private void OnTopmostClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(TopmostRequested);

    private void OnResetPositionClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(ResetPositionRequested);

    private void OnVisibilityClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(VisibilityToggleRequested);

    private void OnSettingsClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(SettingsRequested);

    private void OnMuteClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(MuteRequested);

    private void OnOpenDataClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(OpenDataRequested);

    private void OnOpenLogsClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(OpenLogsRequested);

    private void OnExitClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(ExitRequested);

    private void RaiseAndClose(Action? action)
    {
        Close();
        action?.Invoke();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
