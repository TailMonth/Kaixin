using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using KaixinPet.Audio;
using KaixinPet.Core.Settings;
using Microsoft.Win32;

namespace KaixinPet.App.Ui;

public partial class SettingsWindow : Window
{
    private static readonly Brush ErrorBrush = CreateBrush(0x93, 0x4A, 0x44);
    private static readonly Brush HintBrush = CreateBrush(0x68, 0x67, 0x5F);

    private readonly App _app;
    private readonly SettingsService _settings;
    private readonly List<SoundRow> _soundRows = [];
    private readonly DropShadowEffect _windowShadow;

    private DispatcherTimer? _sliderApplyTimer;
    private bool _loading = true;
    private bool _closed;
    private bool _capturingHotkey;
    private string _hotkeyBeforeCapture = string.Empty;
    private CancellationTokenSource? _importCancellation;

    public SettingsWindow(App app)
    {
        InitializeComponent();
        _app = app;
        _settings = app.SettingsService;
        _windowShadow = WindowShadow;
        Topmost = app.IsPetTopmost;

        _sliderApplyTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _sliderApplyTimer.Tick += (_, _) =>
        {
            _sliderApplyTimer?.Stop();
            if (!_closed)
            {
                _app.ApplyAllSettings();
            }
        };

        BuildSoundRows();
        SyncFromSettings();
        _loading = false;
        UpdateWindowChromeState();
    }

    public void SyncFromSettings()
    {
        if (_closed)
        {
            return;
        }

        _loading = true;
        try
        {
            var settings = _settings.Current;

            if (!PetNameBox.IsKeyboardFocused)
            {
                PetNameBox.Text = settings.General.PetName;
            }

            PetNameError.Visibility = Visibility.Collapsed;
            TopmostCheck.IsChecked = settings.General.AlwaysOnTop;
            HideFullscreenCheck.IsChecked = settings.General.HideOnFullscreen;
            LaunchAtLoginCheck.IsChecked = settings.General.LaunchAtLogin;
            LaunchAtLoginError.Visibility = Visibility.Collapsed;

            FocusMinutesSlider.Value = settings.Pomodoro.FocusMinutes;
            ShortBreakSlider.Value = settings.Pomodoro.ShortBreakMinutes;
            LongBreakSlider.Value = settings.Pomodoro.LongBreakMinutes;
            RoundsSlider.Value = settings.Pomodoro.RoundsPerLongBreak;
            AutoStartBreaksCheck.IsChecked = settings.Pomodoro.AutoStartBreaks;
            AutoStartFocusCheck.IsChecked = settings.Pomodoro.AutoStartFocus;
            ShowTimeCheck.IsChecked = settings.Pomodoro.ShowHourglassTime;
            ShowHoverCheck.IsChecked = settings.Pomodoro.ShowHoverControls;
            NotificationsCheck.IsChecked = settings.Pomodoro.ShowNotifications;
            QuietCheck.IsChecked = settings.Pomodoro.QuietDuringFocus;

            MutedCheck.IsChecked = settings.Audio.Muted;
            MasterSlider.Value = Math.Round(settings.Audio.MasterVolume * 100);
            EffectsSlider.Value = Math.Round(settings.Audio.EffectsVolume * 100);
            AmbientSlider.Value = Math.Round(settings.Audio.AmbientVolume * 100);
            PurrDuringFocusCheck.IsChecked = settings.Audio.AmbientPurrDuringFocus;
            SetVolumeSlidersEnabled(!settings.Audio.Muted);

            SpontaneousPlayCheck.IsChecked = settings.Behavior.SpontaneousPlay;
            PlayMaxSlider.Value = settings.Behavior.PlaySessionMaxSeconds;
            SleepAfterSlider.Value = settings.Behavior.SleepAfterIdleMinutes;
            FocusQuietSlider.Value = settings.Behavior.FocusQuietAfterMinutes;
            PlayMaxSlider.IsEnabled = settings.Behavior.SpontaneousPlay;

            UpdateVolumeLabels();
            UpdatePomodoroPreview();
            UpdateBehaviorPreview();

            VersionText.Text = $"版本 {_app.VersionText} · .NET 10 · Windows 桌面";
            HotkeyError.Visibility = Visibility.Collapsed;
            if (!_capturingHotkey)
            {
                HotkeyBox.Text = _app.HotkeyText;
                SetHotkeyHint(
                    _app.HotkeyRegistered
                        ? "点击“更改”后按下新的组合键，至少包含 Ctrl、Alt、Shift 或 Win 中的一个；Esc 取消。"
                        : "当前快捷键未生效，可能被其他程序占用。请更改或恢复默认。",
                    warning: !_app.HotkeyRegistered);
            }

            foreach (var row in _soundRows)
            {
                UpdateSoundRow(row);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _sliderApplyTimer?.Stop();
        _sliderApplyTimer = null;
        _importCancellation?.Cancel();
        _importCancellation?.Dispose();
        _importCancellation = null;
        base.OnClosed(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateWindowChromeState();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (NavList.SelectedIndex < 0)
        {
            NavList.SelectedIndex = 0;
        }

        SyncFromSettings();
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedIndex < 0)
        {
            return;
        }

        GeneralSection.Visibility = NavList.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        PomodoroSection.Visibility = NavList.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        AudioSection.Visibility = NavList.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        BehaviorSection.Visibility = NavList.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        AboutSection.Visibility = NavList.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(
            new Action(() => ContentScroll.ScrollToTop()),
            DispatcherPriority.Loaded);
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void UpdateWindowChromeState()
    {
        if (OuterRoot is null || WindowFrame is null || MaximizeIcon is null)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        OuterRoot.Margin = maximized ? new Thickness(0) : new Thickness(12);
        WindowFrame.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(12);
        WindowFrame.Effect = maximized ? null : _windowShadow;
        MaximizeIcon.Data = Geometry.Parse(
            maximized ? "M8,8 L18,8 L18,18 L8,18 Z M6,6 L16,6" : "M6,6 L18,6 L18,18 L6,18 Z");
        MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
        AutomationProperties.SetName(MaximizeButton, maximized ? "还原" : "最大化");
    }

    private void OnPetNameCommitted(object sender, RoutedEventArgs e) => CommitPetName();

    private void OnPetNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitPetName();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            PetNameBox.Text = _settings.Current.General.PetName;
            PetNameError.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    private void CommitPetName()
    {
        if (_loading)
        {
            return;
        }

        var name = PetNameBox.Text.Trim();
        if (name.Length is < 1 or > 24)
        {
            PetNameError.Text = "名字需要 1–24 个字符";
            PetNameError.Visibility = Visibility.Visible;
            return;
        }

        PetNameError.Visibility = Visibility.Collapsed;
        PetNameBox.Text = name;
        if (name != _settings.Current.General.PetName)
        {
            _settings.Current.General.PetName = name;
            SaveAndApply();
        }
    }

    private void OnGeneralToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.Current.General.AlwaysOnTop = TopmostCheck.IsChecked == true;
        _settings.Current.General.HideOnFullscreen = HideFullscreenCheck.IsChecked == true;
        SaveAndApply();
    }

    private void OnLaunchAtLoginChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var enabled = LaunchAtLoginCheck.IsChecked == true;
        if (!StartupRegistration.TryApply(enabled, out var error))
        {
            LaunchAtLoginError.Text = error;
            LaunchAtLoginError.Visibility = Visibility.Visible;
            _loading = true;
            LaunchAtLoginCheck.IsChecked = !enabled;
            _loading = false;
            return;
        }

        LaunchAtLoginError.Visibility = Visibility.Collapsed;
        _settings.Current.General.LaunchAtLogin = enabled;
        _settings.SaveDebounced();
    }

    private void OnResetPositionClicked(object sender, RoutedEventArgs e) => _app.ResetPetPosition();

    private void OnPomodoroSliderChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var pomodoro = _settings.Current.Pomodoro;
        pomodoro.FocusMinutes = (int)Math.Round(FocusMinutesSlider.Value);
        pomodoro.ShortBreakMinutes = (int)Math.Round(ShortBreakSlider.Value);
        pomodoro.LongBreakMinutes = (int)Math.Round(LongBreakSlider.Value);
        pomodoro.RoundsPerLongBreak = (int)Math.Round(RoundsSlider.Value);
        UpdatePomodoroPreview();
        _settings.SaveDebounced();
        ScheduleSliderApply();
    }

    private void OnPomodoroToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var settings = _settings.Current.Pomodoro;
        settings.AutoStartBreaks = AutoStartBreaksCheck.IsChecked == true;
        settings.AutoStartFocus = AutoStartFocusCheck.IsChecked == true;
        settings.ShowHourglassTime = ShowTimeCheck.IsChecked == true;
        settings.ShowHoverControls = ShowHoverCheck.IsChecked == true;
        settings.ShowNotifications = NotificationsCheck.IsChecked == true;
        settings.QuietDuringFocus = QuietCheck.IsChecked == true;
        SaveAndApply();
    }

    private void OnBehaviorSliderChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var behavior = _settings.Current.Behavior;
        behavior.PlaySessionMaxSeconds = (int)Math.Round(PlayMaxSlider.Value);
        behavior.SleepAfterIdleMinutes = (int)Math.Round(SleepAfterSlider.Value);
        behavior.FocusQuietAfterMinutes = (int)Math.Round(FocusQuietSlider.Value);
        UpdateBehaviorPreview();
        _settings.SaveDebounced();
        ScheduleSliderApply();
    }

    private void OnBehaviorToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var enabled = SpontaneousPlayCheck.IsChecked == true;
        _settings.Current.Behavior.SpontaneousPlay = enabled;
        PlayMaxSlider.IsEnabled = enabled;
        SaveAndApply();
    }

    private void OnMutedChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var muted = MutedCheck.IsChecked == true;
        _settings.Current.Audio.Muted = muted;
        SetVolumeSlidersEnabled(!muted);
        _settings.SaveDebounced();
        _app.ApplyAllSettings();
    }

    private void OnPurrChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.Current.Audio.AmbientPurrDuringFocus = PurrDuringFocusCheck.IsChecked == true;
        SaveAndApply();
    }

    private void OnVolumeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var audio = _settings.Current.Audio;
        audio.MasterVolume = Math.Round(MasterSlider.Value) / 100.0;
        audio.EffectsVolume = Math.Round(EffectsSlider.Value) / 100.0;
        audio.AmbientVolume = Math.Round(AmbientSlider.Value) / 100.0;
        UpdateVolumeLabels();
        _settings.SaveDebounced();
        _app.Audio?.ApplySettings(audio);
    }

    private void SetVolumeSlidersEnabled(bool enabled)
    {
        MasterSlider.IsEnabled = enabled;
        EffectsSlider.IsEnabled = enabled;
        AmbientSlider.IsEnabled = enabled;
    }

    private void UpdatePomodoroPreview()
    {
        var focus = (int)Math.Round(FocusMinutesSlider.Value);
        var shortBreak = (int)Math.Round(ShortBreakSlider.Value);
        var longBreak = (int)Math.Round(LongBreakSlider.Value);
        var rounds = (int)Math.Round(RoundsSlider.Value);

        FocusMinutesValue.Text = $"{focus} 分钟";
        ShortBreakValue.Text = $"{shortBreak} 分钟";
        LongBreakValue.Text = $"{longBreak} 分钟";
        RoundsValue.Text = $"{rounds} 个专注";
        CycleFocusText.Text = $"专注 {focus} 分钟";
        CycleShortText.Text = $"短休息 {shortBreak} 分钟";
        CycleLongText.Text = $"长休息 {longBreak} 分钟";
        UpdateRoundDots(rounds);
    }

    private void UpdateRoundDots(int rounds)
    {
        CycleRoundsPanel.Children.Clear();
        for (var i = 0; i < rounds; i++)
        {
            CycleRoundsPanel.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                Margin = new Thickness(0, 0, 3, 0),
                Fill = (Brush)FindResource("EyeSageBrush")
            });
        }

        AutomationProperties.SetName(CycleRoundsPanel, $"{rounds} 个专注后长休息");
    }

    private void UpdateBehaviorPreview()
    {
        var playMax = (int)Math.Round(PlayMaxSlider.Value);
        var sleepAfter = (int)Math.Round(SleepAfterSlider.Value);
        var quietAfter = (int)Math.Round(FocusQuietSlider.Value);
        PlayMaxValue.Text = $"{playMax} 秒";
        SleepAfterValue.Text = $"{sleepAfter} 分钟";
        FocusQuietValue.Text = quietAfter == 0 ? "立即" : $"{quietAfter} 分钟";
    }

    private void UpdateVolumeLabels()
    {
        MasterValueText.Text = $"{Math.Round(MasterSlider.Value)}%";
        EffectsValueText.Text = $"{Math.Round(EffectsSlider.Value)}%";
        AmbientValueText.Text = $"{Math.Round(AmbientSlider.Value)}%";
    }

    private void ScheduleSliderApply()
    {
        if (_sliderApplyTimer is null)
        {
            return;
        }

        _sliderApplyTimer.Stop();
        _sliderApplyTimer.Start();
    }

    private void OnChangeHotkeyClicked(object sender, RoutedEventArgs e)
    {
        if (_capturingHotkey)
        {
            CancelHotkeyCapture();
            return;
        }

        _capturingHotkey = true;
        _hotkeyBeforeCapture = _app.HotkeyText;
        HotkeyBox.Text = "请按下新的组合键…";
        HotkeyError.Visibility = Visibility.Collapsed;
        ChangeHotkeyButton.Content = "取消";
        SetHotkeyHint("等待按键中：Esc 取消。", warning: false);
        Activate();
        ChangeHotkeyButton.Focus();
        Keyboard.Focus(ChangeHotkeyButton);
    }

    private void OnResetHotkeyClicked(object sender, RoutedEventArgs e)
    {
        if (_capturingHotkey)
        {
            CancelHotkeyCapture();
        }

        if (_app.TryApplyHotkey("Ctrl+Alt+P", out var error))
        {
            HotkeyBox.Text = _app.HotkeyText;
            HotkeyError.Visibility = Visibility.Collapsed;
            SetHotkeyHint("已恢复默认快捷键。", warning: false);
        }
        else
        {
            HotkeyBox.Text = _app.HotkeyText;
            HotkeyError.Text = error;
            HotkeyError.Visibility = Visibility.Visible;
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingHotkey)
        {
            if (e.Key == Key.Escape)
            {
                CancelHotkeyCapture();
                e.Handled = true;
                return;
            }

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (IsModifierKey(key))
            {
                e.Handled = true;
                return;
            }

            if (key is Key.None or Key.ImeProcessed)
            {
                e.Handled = true;
                return;
            }

            var modifiers = Keyboard.Modifiers;
            if (modifiers == ModifierKeys.None)
            {
                ShowHotkeyError("需要至少一个修饰键（Ctrl、Alt、Shift 或 Win）");
                e.Handled = true;
                return;
            }

            var keyName = FormatKeyName(key);
            if (keyName is null)
            {
                ShowHotkeyError("这个按键不支持作为快捷键");
                e.Handled = true;
                return;
            }

            var gesture = FormatGesture(modifiers, keyName);
            if (_app.TryApplyHotkey(gesture, out var error))
            {
                EndHotkeyCapture();
                HotkeyBox.Text = _app.HotkeyText;
                HotkeyError.Visibility = Visibility.Collapsed;
                SetHotkeyHint("快捷键已更新。", warning: false);
            }
            else
            {
                EndHotkeyCapture();
                HotkeyBox.Text = _hotkeyBeforeCapture;
                ShowHotkeyError(error);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void CancelHotkeyCapture()
    {
        EndHotkeyCapture();
        HotkeyBox.Text = _hotkeyBeforeCapture;
        HotkeyError.Visibility = Visibility.Collapsed;
        SetHotkeyHint(
            _app.HotkeyRegistered
                ? "点击“更改”后按下新的组合键，至少包含 Ctrl、Alt、Shift 或 Win 中的一个；Esc 取消。"
                : "当前快捷键未生效，可能被其他程序占用。请更改或恢复默认。",
            warning: !_app.HotkeyRegistered);
    }

    private void EndHotkeyCapture()
    {
        _capturingHotkey = false;
        ChangeHotkeyButton.Content = "更改";
    }

    private void ShowHotkeyError(string message)
    {
        HotkeyError.Text = message;
        HotkeyError.Visibility = Visibility.Visible;
        SetHotkeyHint("请重试，或按 Esc 取消。", warning: true);
    }

    private void SetHotkeyHint(string text, bool warning)
    {
        HotkeyHint.Text = text;
        HotkeyHint.Foreground = warning
            ? (Brush)FindResource("DangerBrush")
            : (Brush)FindResource("FurMutedBrush");
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static string? FormatKeyName(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)(key - Key.D0)).ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return key.ToString();
        }

        return key switch
        {
            Key.None or Key.Escape or Key.Tab or Key.ImeProcessed => null,
            _ => key.ToString()
        };
    }

    private static string FormatGesture(ModifierKeys modifiers, string keyName)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    private void BuildSoundRows()
    {
        SoundRowsPanel.Children.Clear();
        _soundRows.Clear();

        var audio = _app.Audio;
        if (audio is null)
        {
            SoundRowsPanel.Children.Add(new TextBlock
            {
                Text = "音频服务未初始化。",
                Foreground = (Brush)FindResource("DangerBrush"),
                Margin = new Thickness(0, 8, 0, 0)
            });
            return;
        }

        foreach (var definition in audio.Events)
        {
            var row = CreateSoundRow(definition);
            _soundRows.Add(row);
            SoundRowsPanel.Children.Add(row.Root);
        }
    }

    private SoundRow CreateSoundRow(SoundEventDefinition definition)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = definition.Loop ? $"{definition.Label}（循环）" : definition.Label,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("FurInkBrush")
        };
        name.ToolTip = definition.Id;
        var source = new TextBlock
        {
            FontSize = 11,
            Foreground = (Brush)FindResource("FurMutedBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 12, 0)
        };
        textStack.Children.Add(name);
        textStack.Children.Add(source);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var previewButton = CreateSmallButton(
            "试听", "M8,5 L19,12 L8,19 Z", true, $"试听 {definition.Label}",
            $"SoundPreview_{definition.Id}");
        var replaceButton = CreateSmallButton(
            "更换", "M3,6 L10,6 L12,8 L21,8 L21,19 L3,19 Z", false, $"更换 {definition.Label}",
            $"SoundReplace_{definition.Id}");
        var restoreButton = CreateSmallButton(
            "还原", "M4,10 A8,8 0 1 1 6,17 M4,4 L4,10 L10,10", false, $"还原 {definition.Label}",
            $"SoundRestore_{definition.Id}");
        previewButton.Margin = new Thickness(6, 0, 0, 0);
        replaceButton.Margin = new Thickness(6, 0, 0, 0);
        restoreButton.Margin = new Thickness(6, 0, 0, 0);
        buttons.Children.Add(previewButton);
        buttons.Children.Add(replaceButton);
        buttons.Children.Add(restoreButton);

        Grid.SetColumn(textStack, 0);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(textStack);
        grid.Children.Add(buttons);

        var info = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed
        };

        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(info);

        var root = new Border
        {
            BorderBrush = (Brush)FindResource("FurOutlineSoftBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 10, 0, 10),
            Child = stack
        };

        var row = new SoundRow(definition, root, source, info, previewButton, replaceButton, restoreButton);
        previewButton.Click += (_, _) => PreviewSound(row);
        replaceButton.Click += async (_, _) => await ReplaceSoundAsync(row);
        restoreButton.Click += (_, _) => RestoreSound(row);
        return row;
    }

    private Button CreateSmallButton(
        string text,
        string iconData,
        bool solid,
        string automationName,
        string automationId)
    {
        var button = new Button
        {
            Style = (Style)FindResource("SmallButton"),
            ToolTip = automationName
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(iconData),
            Style = (Style)FindResource(solid ? "SolidIcon" : "LineIcon"),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        button.Content = panel;
        AutomationProperties.SetName(button, automationName);
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }

    private void UpdateSoundRow(SoundRow row)
    {
        var info = _app.Audio?.GetSourceInfo(row.Definition.Id);
        if (info is null)
        {
            row.SourceText.Text = "音频服务不可用";
            row.RestoreButton.IsEnabled = false;
            return;
        }

        if (!info.HasCustom)
        {
            row.SourceText.Text = "默认";
            row.RestoreButton.IsEnabled = false;
        }
        else if (info.CustomMissing)
        {
            row.SourceText.Text = "自定义文件缺失，使用默认";
            row.RestoreButton.IsEnabled = true;
        }
        else
        {
            row.SourceText.Text = $"自定义 · {info.DisplayName}";
            row.RestoreButton.IsEnabled = true;
        }
    }

    private void PreviewSound(SoundRow row)
    {
        if (_settings.Current.Audio.Muted)
        {
            row.ShowMessage("已静音，取消静音后可试听。", error: true);
            return;
        }

        var played = _app.Audio?.Preview(row.Definition.Id) == true;
        row.ShowMessage(
            played ? "正在试听。" : "无法试听：音频设备不可用。",
            error: !played);
    }

    private async Task ReplaceSoundAsync(SoundRow row)
    {
        if (_importCancellation is not null)
        {
            row.ShowMessage("请等待当前音效导入完成。", error: true);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"为「{row.Definition.Label}」选择音频",
            Filter = "音频文件|*.wav;*.mp3;*.m4a;*.aac;*.flac|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var selection = dialog.FileName;
        using var cancellation = new CancellationTokenSource();
        _importCancellation = cancellation;
        row.SetBusy(true);
        row.ShowMessage("正在导入并转码…", error: false);
        try
        {
            var result = await Task.Run(
                () => SoundImporter.Import(selection, row.Definition.Id, _app.DataRoot, cancellation.Token),
                cancellation.Token);

            if (_closed)
            {
                return;
            }

            if (!result.Success)
            {
                row.ShowMessage(result.Error ?? "导入失败", error: true);
                return;
            }

            var audioSettings = _settings.Current.Audio;
            audioSettings.CustomSounds.TryGetValue(row.Definition.Id, out var previous);
            audioSettings.CustomSounds[row.Definition.Id] = result.RelativePath!;
            _app.Audio?.UpdateCustomSounds(audioSettings.CustomSounds);
            _settings.SaveDebounced();
            if (!string.IsNullOrWhiteSpace(previous) &&
                !string.Equals(previous, result.RelativePath, StringComparison.OrdinalIgnoreCase))
            {
                SoundImporter.DeleteCachedFile(_app.DataRoot, previous);
            }

            UpdateSoundRow(row);
            if (_settings.Current.Audio.Muted)
            {
                row.ShowMessage("已导入；当前静音，取消静音后可试听。", error: false);
            }
            else
            {
                var played = _app.Audio?.Preview(row.Definition.Id) == true;
                row.ShowMessage(
                    played ? "已导入并试听。" : "已导入，但音频设备不可用，暂时无法试听。",
                    error: !played);
            }
        }
        catch (OperationCanceledException)
        {
            if (!_closed)
            {
                row.ShowMessage("导入已取消。", error: true);
            }
        }
        catch (Exception exception)
        {
            if (!_closed)
            {
                row.ShowMessage($"导入失败：{exception.Message}", error: true);
            }
        }
        finally
        {
            _importCancellation = null;
            if (!_closed)
            {
                row.SetBusy(false);
                UpdateSoundRow(row);
            }
        }
    }

    private void RestoreSound(SoundRow row)
    {
        var audioSettings = _settings.Current.Audio;
        if (!audioSettings.CustomSounds.TryGetValue(row.Definition.Id, out var previous))
        {
            UpdateSoundRow(row);
            return;
        }

        audioSettings.CustomSounds.Remove(row.Definition.Id);
        _app.Audio?.UpdateCustomSounds(audioSettings.CustomSounds);
        _settings.SaveDebounced();
        SoundImporter.DeleteCachedFile(_app.DataRoot, previous);
        UpdateSoundRow(row);
        row.ShowMessage("已还原默认音效。", error: false);
    }

    private void OnRestoreAllSoundsClicked(object sender, RoutedEventArgs e)
    {
        var audioSettings = _settings.Current.Audio;
        if (audioSettings.CustomSounds.Count == 0)
        {
            return;
        }

        foreach (var relative in audioSettings.CustomSounds.Values.ToArray())
        {
            SoundImporter.DeleteCachedFile(_app.DataRoot, relative);
        }

        audioSettings.CustomSounds.Clear();
        _app.Audio?.UpdateCustomSounds(audioSettings.CustomSounds);
        _settings.SaveDebounced();
        foreach (var row in _soundRows)
        {
            UpdateSoundRow(row);
            row.ClearMessage();
        }
    }

    private void OnOpenDataClicked(object sender, RoutedEventArgs e) => OpenFolder(_app.DataRoot);

    private void OnOpenLogsClicked(object sender, RoutedEventArgs e) =>
        OpenFolder(Path.Combine(_app.DataRoot, "logs"));

    private void OnOpenLicensesClicked(object sender, RoutedEventArgs e)
    {
        var licenseFile = Path.Combine(AppContext.BaseDirectory, "licenses", "audio.txt");
        if (File.Exists(licenseFile))
        {
            OpenPath(licenseFile);
            return;
        }

        OpenFolder(Path.Combine(AppContext.BaseDirectory, "licenses"));
    }

    private void OnResetAllClicked(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "恢复所有设置为默认值，并删除已导入的自定义音效缓存？此操作不可撤销。",
            "重置所有设置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _app.ResetAllSettings();
        SyncFromSettings();
        MessageBox.Show(this, "已恢复默认设置。", "开心", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveAndApply()
    {
        if (_loading)
        {
            return;
        }

        _sliderApplyTimer?.Stop();
        _settings.SaveDebounced();
        _app.ApplyAllSettings();
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening a folder is best effort.
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening a file is best effort.
        }
    }

    private static Brush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private sealed class SoundRow(
        SoundEventDefinition definition,
        Border root,
        TextBlock sourceText,
        TextBlock infoText,
        Button previewButton,
        Button replaceButton,
        Button restoreButton)
    {
        public SoundEventDefinition Definition { get; } = definition;

        public Border Root { get; } = root;

        public TextBlock SourceText { get; } = sourceText;

        public TextBlock InfoText { get; } = infoText;

        public Button PreviewButton { get; } = previewButton;

        public Button ReplaceButton { get; } = replaceButton;

        public Button RestoreButton { get; } = restoreButton;

        public void SetBusy(bool busy)
        {
            PreviewButton.IsEnabled = !busy;
            ReplaceButton.IsEnabled = !busy;
            RestoreButton.IsEnabled = !busy;
        }

        public void ShowMessage(string message, bool error)
        {
            InfoText.Text = message;
            InfoText.Foreground = error ? ErrorBrush : HintBrush;
            InfoText.Visibility = Visibility.Visible;
        }

        public void ClearMessage()
        {
            InfoText.Text = string.Empty;
            InfoText.Visibility = Visibility.Collapsed;
        }
    }
}
