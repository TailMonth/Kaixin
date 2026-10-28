using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KaixinPet.Pet.Native;
using KaixinPet.Pet.Scene;

namespace KaixinPet.App.Ui;

public partial class TimeTagWindow : Window
{
    private static readonly Brush FocusDotBrush = CreateFrozenBrush("#E4572E");
    private static readonly Brush BreakDotBrush = CreateFrozenBrush("#6D9E6A");
    private static readonly Brush PausedDotBrush = CreateFrozenBrush("#A39C90");
    private static readonly Brush NormalTextBrush = CreateFrozenBrush("#2B2A27");
    private static readonly Brush PausedTextBrush = CreateFrozenBrush("#6F6A61");

    private PixelRect _petBounds;
    private double _dpiScale = 1.0;
    private bool _fadingOut;

    public TimeTagWindow()
    {
        InitializeComponent();
        Left = -10000;
        Top = -10000;
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => Reposition();
        DpiChanged += (_, _) => Reposition();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowInterop.MakeOverlayPassive(hwnd);
        Reposition();
    }

    public void Place(PixelRect petBounds, double dpiScale)
    {
        _petBounds = petBounds;
        _dpiScale = dpiScale;
        Reposition();
    }

    public void SetTime(TimeSpan remaining)
    {
        var minutes = (int)remaining.TotalMinutes;
        var text = $"{minutes:00}:{remaining.Seconds:00}";
        if (TimeText.Text != text)
        {
            TimeText.Text = text;
        }
    }

    public void SetPhaseFocus()
    {
        PhaseDot.Fill = FocusDotBrush;
        TimeText.Foreground = NormalTextBrush;
    }

    public void SetPhaseBreak()
    {
        PhaseDot.Fill = BreakDotBrush;
        TimeText.Foreground = NormalTextBrush;
    }

    public void SetPaused(bool paused)
    {
        PhaseDot.Fill = paused ? PausedDotBrush : FocusDotBrush;
        TimeText.Foreground = paused ? PausedTextBrush : NormalTextBrush;
    }

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
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
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
        var animation = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180))
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

    public void HideNow()
    {
        _fadingOut = false;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Hide();
    }

    public void SetTopmost(bool topmost) => Topmost = topmost;

    private void Reposition()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (hwnd == IntPtr.Zero || _petBounds.Width <= 0)
        {
            return;
        }

        var work = ScreenUtil.GetWorkAreaForRect(_petBounds);
        var left = _petBounds.Left + (int)Math.Round(SceneLayout.TagLeftDip * _dpiScale);
        var top = _petBounds.Top + (int)Math.Round(SceneLayout.TagTopDip * _dpiScale);

        if (top < work.Top + 4)
        {
            top = _petBounds.Top + (int)Math.Round(SceneLayout.TagBelowTopDip * _dpiScale);
        }

        var tagHeight = (int)Math.Round(SceneLayout.TagHeightDip * _dpiScale);
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - tagHeight));

        WindowInterop.Move(hwnd, left, top);
    }

    private static Brush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
