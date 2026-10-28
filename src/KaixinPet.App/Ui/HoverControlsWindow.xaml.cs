using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using KaixinPet.Pet.Native;

namespace KaixinPet.App.Ui;

public partial class HoverControlsWindow : Window
{
    public const double BarWidthDip = 92;
    public const double BarHeightDip = 30;
    public const double BarTopDip = 6;

    public HoverControlsWindow()
    {
        InitializeComponent();
        Left = -10000;
        Top = -10000;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            WindowInterop.MakeNonActivating(hwnd);
        };
        Loaded += (_, _) => Reposition();
        DpiChanged += (_, _) => Reposition();
    }

    public event Action? PauseResumeRequested;

    public event Action? SkipRequested;

    public event Action? SettingsRequested;

    private PixelRect _petBounds;
    private double _dpiScale = 1.0;
    private bool _fadingOut;

    public void Place(PixelRect petBounds, double dpiScale)
    {
        _petBounds = petBounds;
        _dpiScale = dpiScale;
        Reposition();
    }

    public void SetPaused(bool paused)
    {
        PauseBars.Visibility = paused ? Visibility.Collapsed : Visibility.Visible;
        PlayIcon.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetTopmost(bool topmost) => Topmost = topmost;

    public void ShowFadeIn()
    {
        _fadingOut = false;
        BeginAnimation(OpacityProperty, null);
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }

        Reposition();
        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
            {
                FillBehavior = FillBehavior.HoldEnd,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
    }

    public void FadeOutAndHide()
    {
        if (!IsVisible || _fadingOut)
        {
            return;
        }

        _fadingOut = true;
        var animation = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(160))
        {
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            Hide();
            _fadingOut = false;
        };
        BeginAnimation(OpacityProperty, animation);
    }

    private void Reposition()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (hwnd == IntPtr.Zero || _petBounds.Width <= 0)
        {
            return;
        }

        var barWidth = (int)Math.Round(BarWidthDip * _dpiScale);
        var left = _petBounds.Left + (_petBounds.Width - barWidth) / 2;
        var top = _petBounds.Top + (int)Math.Round(BarTopDip * _dpiScale);
        WindowInterop.Move(hwnd, left, top);
    }

    private void OnPauseClicked(object sender, RoutedEventArgs e) => PauseResumeRequested?.Invoke();

    private void OnSkipClicked(object sender, RoutedEventArgs e) => SkipRequested?.Invoke();

    private void OnSettingsClicked(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
}
