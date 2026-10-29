using System.Runtime.InteropServices;
using System.Windows;
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
    bool IsPlaying,
    bool IsSleeping,
    bool Busy,
    bool Topmost,
    bool Muted,
    int FocusMinutes,
    int ShortBreakMinutes);

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

    public event Action? StartFocusRequested;

    public event Action? StartBreakRequested;

    public event Action? PauseResumeRequested;

    public event Action? SkipRequested;

    public event Action? ResetRequested;

    public event Action? PlayRequested;

    public event Action? SleepRequested;

    public event Action? TopmostRequested;

    public event Action? ResetPositionRequested;

    public event Action? HideRequested;

    public event Action? SettingsRequested;

    public event Action? MuteRequested;

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

        PlayButtonText.Text = state.IsPlaying ? "结束玩耍" : "陪它玩毛线球";
        SleepButtonText.Text = state.IsSleeping ? "叫醒它" : "让它睡觉";
        PlayButton.IsEnabled = !state.Busy;
        SleepButton.IsEnabled = !state.Busy;

        TopmostCheckMark.Visibility = state.Topmost ? Visibility.Visible : Visibility.Collapsed;
        MuteCheckMark.Visibility = state.Muted ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowNearCursor(int cursorX, int cursorY)
    {
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

        BeginAnimation(OpacityProperty, null);
        if (SystemParameters.ClientAreaAnimation)
        {
            Opacity = 0;
            BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
                {
                    FillBehavior = FillBehavior.HoldEnd,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }
        else
        {
            Opacity = 1;
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

    private void OnPlayClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(PlayRequested);

    private void OnSleepClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(SleepRequested);

    private void OnTopmostClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(TopmostRequested);

    private void OnResetPositionClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(ResetPositionRequested);

    private void OnHideClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(HideRequested);

    private void OnSettingsClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(SettingsRequested);

    private void OnMuteClicked(object sender, RoutedEventArgs e) =>
        RaiseAndClose(MuteRequested);

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
