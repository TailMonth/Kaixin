using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KaixinPet.Pet.Native;

namespace KaixinPet.App.Ui;

public partial class TimerPanelWindow : Window
{
    public const double CompactWidthDip = 84;
    public const double CompactHeightDip = 30;
    public const double ExpandedWidthDip = 216;
    public const double ExpandedHeightDip = 136;

    private static readonly Brush FocusBrush = CreateFrozenBrush("#A97752");
    private static readonly Brush BreakBrush = CreateFrozenBrush("#667A5E");
    private static readonly Brush NormalTextBrush = CreateFrozenBrush("#3F403B");
    private static readonly Brush PausedTextBrush = CreateFrozenBrush("#68675F");
    private static readonly Brush HighlightBorderBrush = CreateFrozenBrush("#53624F");

    private readonly Brush _compactBorderBrush;
    private readonly Thickness _compactBorderThickness;
    private readonly Brush _expandedBorderBrush;
    private readonly Thickness _expandedBorderThickness;

    private PixelRect _petBounds;
    private double _dpiScale = 1.0;
    private bool _expanded;
    private bool _fadingOut;
    private bool _paused;
    private string _phaseText = "专注中";
    private Brush _phaseBrush = FocusBrush;
    private DispatcherTimer? _highlightTimer;

    public TimerPanelWindow()
    {
        InitializeComponent();
        Left = -10000;
        Top = -10000;
        _compactBorderBrush = CompactPanel.BorderBrush;
        _compactBorderThickness = CompactPanel.BorderThickness;
        _expandedBorderBrush = ExpandedPanel.BorderBrush;
        _expandedBorderThickness = ExpandedPanel.BorderThickness;
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => Reposition();
        DpiChanged += (_, _) => Reposition();
    }

    public event Action? PauseResumeRequested;

    public event Action? SkipRequested;

    public event Action? EndRequested;

    public event Action? SettingsRequested;

    public bool IsExpanded => _expanded;

    public void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
        {
            return;
        }

        _expanded = expanded;
        CompactPanel.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandedPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        Width = expanded ? ExpandedWidthDip : CompactWidthDip;
        Height = expanded ? ExpandedHeightDip : CompactHeightDip;

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        WindowInterop.SetClickThrough(hwnd, !expanded);
        Reposition();
    }

    public void SetTime(TimeSpan remaining)
    {
        var minutes = (int)remaining.TotalMinutes;
        var text = $"{minutes:00}:{remaining.Seconds:00}";
        if (CompactTime.Text != text)
        {
            CompactTime.Text = text;
        }

        if (ExpandedTime.Text != text)
        {
            ExpandedTime.Text = text;
        }
    }

    public void SetProgress(double progress)
    {
        PanelProgress.Value = Math.Clamp(progress, 0, 1);
    }

    public void SetPhaseFocus()
    {
        _phaseText = "专注中";
        _phaseBrush = FocusBrush;
        ApplyStageVisual();
    }

    public void SetPhaseBreak(bool isLong)
    {
        _phaseText = isLong ? "长休息" : "短休息";
        _phaseBrush = BreakBrush;
        ApplyStageVisual();
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        ApplyStageVisual();
    }

    private void ApplyStageVisual()
    {
        var stageBrush = _paused ? PausedTextBrush : _phaseBrush;
        ExpandedStageDot.Fill = stageBrush;
        ExpandedStageText.Foreground = stageBrush;
        ExpandedStageText.Text = _paused ? "已暂停" : _phaseText;
        PanelProgress.Foreground = stageBrush;

        CompactDot.Fill = stageBrush;
        CompactDot.Visibility = _paused ? Visibility.Collapsed : Visibility.Visible;
        CompactPauseGlyph.Visibility = _paused ? Visibility.Visible : Visibility.Collapsed;
        CompactTime.Foreground = _paused ? PausedTextBrush : NormalTextBrush;
        ExpandedTime.Foreground = _paused ? PausedTextBrush : NormalTextBrush;

        ExpandedPauseBars.Visibility = _paused ? Visibility.Collapsed : Visibility.Visible;
        ExpandedPlayIcon.Visibility = _paused ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetTopmost(bool topmost) => Topmost = topmost;

    public void Place(PixelRect petBounds, double dpiScale)
    {
        _petBounds = petBounds;
        _dpiScale = dpiScale;
        Reposition();
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
        if (!SystemParameters.ClientAreaAnimation)
        {
            Opacity = 1;
            return;
        }

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

        if (!SystemParameters.ClientAreaAnimation)
        {
            HideNow();
            return;
        }

        _fadingOut = true;
        var animation = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(150))
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

    public void Highlight()
    {
        if (!IsVisible)
        {
            return;
        }

        _highlightTimer?.Stop();
        if (_expanded)
        {
            ExpandedPanel.BorderBrush = HighlightBorderBrush;
            ExpandedPanel.BorderThickness = new Thickness(1.5);
        }
        else
        {
            CompactPanel.BorderBrush = HighlightBorderBrush;
            CompactPanel.BorderThickness = new Thickness(1.5);
        }

        _highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _highlightTimer.Tick += (_, _) =>
        {
            _highlightTimer?.Stop();
            _highlightTimer = null;
            CompactPanel.BorderBrush = _compactBorderBrush;
            CompactPanel.BorderThickness = _compactBorderThickness;
            ExpandedPanel.BorderBrush = _expandedBorderBrush;
            ExpandedPanel.BorderThickness = _expandedBorderThickness;
        };
        _highlightTimer.Start();
    }

    public PixelRect GetBoundsPx()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        return WindowInterop.GetBoundsPx(hwnd);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowInterop.MakeNonActivating(hwnd);
        WindowInterop.SetClickThrough(hwnd, !_expanded);
        Reposition();
    }

    private void Reposition()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (hwnd == IntPtr.Zero || _petBounds.Width <= 0)
        {
            return;
        }

        var work = ScreenUtil.GetWorkAreaForRect(_petBounds);
        int left;
        int top;
        if (_expanded)
        {
            var width = (int)Math.Round(ExpandedWidthDip * _dpiScale);
            var height = (int)Math.Round(ExpandedHeightDip * _dpiScale);
            left = _petBounds.Left + (int)Math.Round(50 * _dpiScale);
            top = _petBounds.Top - (int)Math.Round(24 * _dpiScale);
            left = Math.Clamp(left, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4));
            top = Math.Clamp(top, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4));
        }
        else
        {
            var width = (int)Math.Round(CompactWidthDip * _dpiScale);
            left = _petBounds.Left + (int)Math.Round(208 * _dpiScale);
            top = _petBounds.Top + (int)Math.Round(152 * _dpiScale);
            left = Math.Clamp(
                left,
                work.Left + 4,
                Math.Max(work.Left + 4, work.Right - width - 4));
            top = Math.Clamp(
                top,
                work.Top + 4,
                Math.Max(work.Top + 4, work.Bottom - (int)Math.Round(CompactHeightDip * _dpiScale) - 4));
        }

        WindowInterop.Move(hwnd, left, top);
    }

    private void OnPauseClicked(object sender, RoutedEventArgs e) => PauseResumeRequested?.Invoke();

    private void OnSkipClicked(object sender, RoutedEventArgs e) => SkipRequested?.Invoke();

    private void OnEndClicked(object sender, RoutedEventArgs e) => EndRequested?.Invoke();

    private void OnSettingsClicked(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private static Brush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
