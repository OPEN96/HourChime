using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace HourChime.Views;

public partial class MainWindow : Window
{
    private static readonly Brush PillRunning = new SolidColorBrush(Color.FromRgb(0x16, 0x24, 0x1D));
    private static readonly Brush PillIdle = new SolidColorBrush(Color.FromRgb(0x1C, 0x21, 0x2B));
    private static readonly Brush PillPaused = new SolidColorBrush(Color.FromRgb(0x2B, 0x22, 0x0E));

    private readonly App _app;
    private readonly ChimeScheduler _sched;
    private readonly ObservableCollection<AlarmRow> _rows = new();

    private AppSettings S => _app.Settings;

    private bool _loading = true;
    private bool _voiceLoaded;
    private string _voiceInfo = "";

    public MainWindow(App app)
    {
        InitializeComponent();
        _app = app;
        _sched = app.Scheduler;
        AlarmList.ItemsSource = _rows;

        SourceInitialized += (_, _) => Platform.EnableRoundedCorners(this);
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _sched.Tick -= OnTick;
            _sched.StateChanged -= UpdateStatus;
            _sched.AlarmsChanged -= RefreshAlarms;
        };

        _sched.Tick += OnTick;
        _sched.StateChanged += UpdateStatus;
        _sched.AlarmsChanged += RefreshAlarms;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadValues();
        RefreshAlarms();
        UpdateStatus();
        OnTick(DateTime.Now);
        _ = LoadVoicesAsync();
    }

    // ---------- 载入与同步 ----------

    private void LoadValues()
    {
        _loading = true;

        RepeatMinutesBox.ItemsSource = Enumerable.Range(0, 61).ToList();
        RepeatTimesBox.ItemsSource = Enumerable.Range(1, 10).ToList();

        HourlyToggle.IsChecked = S.HourlyEnabled;
        FreqHour.IsChecked = !S.HalfHourlyEnabled;
        FreqHourHalf.IsChecked = S.HalfHourlyEnabled;
        ModeVoice.IsChecked = S.HourlyMode == "voice";
        ModeChime.IsChecked = S.HourlyMode == "chime";
        ModeBoth.IsChecked = S.HourlyMode is not ("voice" or "chime");

        RateSlider.Value = Math.Clamp(S.Rate, -10, 10);
        VolumeSlider.Value = Math.Clamp(S.Volume, 0, 100);
        RateValue.Text = ((int)RateSlider.Value).ToString("+0;-0;0");
        VolumeValue.Text = $"{(int)VolumeSlider.Value}%";

        QuietToggle.IsChecked = S.QuietEnabled;
        QuietFromBox.Text = S.QuietFrom;
        QuietToBox.Text = S.QuietTo;
        HourlyToastToggle.IsChecked = S.HourlyToast;

        AutoStartToggle.IsChecked = S.AutoStart;
        StartMinToggle.IsChecked = S.StartMinimized;
        CloseTrayToggle.IsChecked = S.CloseToTray;

        RepeatMinutesBox.SelectedItem = Math.Clamp(S.RepeatRemindMinutes, 0, 60);
        RepeatTimesBox.SelectedItem = Math.Clamp(S.RepeatRemindTimes, 1, 10);

        VoiceBox.Items.Clear();
        VoiceBox.Items.Add("系统默认语音");
        VoiceBox.SelectedIndex = 0;

        _loading = false;
        ApplyHourlyEnabled();
        UpdateChimeUi();
        UpdateHint();
    }

    /// <summary>托盘菜单改动设置后回写界面。</summary>
    public void SyncFromSettings()
    {
        _loading = true;
        HourlyToggle.IsChecked = S.HourlyEnabled;
        FreqHour.IsChecked = !S.HalfHourlyEnabled;
        FreqHourHalf.IsChecked = S.HalfHourlyEnabled;
        ModeVoice.IsChecked = S.HourlyMode == "voice";
        ModeChime.IsChecked = S.HourlyMode == "chime";
        ModeBoth.IsChecked = S.HourlyMode is not ("voice" or "chime");
        _loading = false;
        ApplyHourlyEnabled();
        UpdateStatus();
    }

    private void ApplyHourlyEnabled()
    {
        HourlyPanel.IsEnabled = S.HourlyEnabled;
        HourlyToastToggle.IsEnabled = S.HourlyEnabled;
        QuietFromBox.IsEnabled = S.QuietEnabled && S.HourlyEnabled;
        QuietToBox.IsEnabled = S.QuietEnabled && S.HourlyEnabled;
    }

    private void UpdateChimeUi()
    {
        bool custom = !string.IsNullOrWhiteSpace(S.ChimePath) && File.Exists(S.ChimePath);
        ChimeText.Text = custom ? Path.GetFileName(S.ChimePath!) : "内置三音阶（三音提示）";
        ChimeText.Foreground = custom ? (Brush)FindResource("Fg") : (Brush)FindResource("SubFg");
        ResetChimeButton.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateHint()
    {
        string chime = string.IsNullOrWhiteSpace(S.ChimePath) ? "内置三音阶" : Path.GetFileName(S.ChimePath);
        VoiceHint.Text = string.IsNullOrEmpty(_voiceInfo)
            ? $"提示音：{chime}"
            : $"{_voiceInfo}  ·  提示音：{chime}";
    }

    // ---------- 时钟与状态 ----------

    private void OnTick(DateTime now)
    {
        ClockText.Text = now.ToString("HH:mm");
        SecondsText.Text = ":" + now.ToString("ss");
        DateText.Text = $"{now:M月d日} {Weekday(now.DayOfWeek)}";
        RefreshNext(now);
        UpdateStatus();
    }

    private static string Weekday(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日"
    };

    private void RefreshNext(DateTime now)
    {
        var next = _sched.NextAlarm(now);
        if (next == null)
        {
            NextTimeText.Text = "--:--";
            NextLabelText.Text = S.Alarms.Count == 0 ? "还没有闹钟" : "没有启用的闹钟";
            return;
        }

        var at = next.Value.At;
        var alarm = next.Value.Alarm;
        NextTimeText.Text = at.ToString("HH:mm");
        string label = string.IsNullOrWhiteSpace(alarm.Label) ? "闹钟" : alarm.Label.Trim();
        var span = at - now;
        string rel = span < TimeSpan.FromHours(12) && span > TimeSpan.Zero
            ? $"{(int)Math.Ceiling(span.TotalMinutes)} 分钟后"
            : Texts.RepeatText(alarm);
        NextLabelText.Text = $"{label} · {rel}";
    }

    private void UpdateStatus()
    {
        if (_sched.IsPaused)
        {
            StatusText.Text = "已暂停";
            StatusText.Foreground = (Brush)FindResource("Accent");
            StatusDot.Fill = (Brush)FindResource("Accent");
            StatusPill.Background = PillPaused;
            PauseButton.Content = $"恢复提醒（{_sched.PausedUntil:HH:mm} 结束）";
            return;
        }

        bool on = S.HourlyEnabled;
        StatusText.Text = on ? "运行中" : "整点已关";
        StatusText.Foreground = (Brush)FindResource(on ? "Ok" : "SubFg");
        StatusDot.Fill = (Brush)FindResource(on ? "Ok" : "MutedFg");
        StatusPill.Background = on ? PillRunning : PillIdle;
        PauseButton.Content = "暂停提醒 30 分钟";
    }

    // ---------- 整点报时 ----------

    private void HourlyToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.HourlyEnabled = HourlyToggle.IsChecked == true;
        _sched.Save();
        ApplyHourlyEnabled();
        UpdateStatus();
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.HourlyMode = sender == ModeVoice ? "voice" : sender == ModeChime ? "chime" : "both";
        _sched.Save();
    }

    private void Freq_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.HalfHourlyEnabled = FreqHourHalf.IsChecked == true;
        _sched.Save();
    }

    private void VoiceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !_voiceLoaded) return;
        S.VoiceName = VoiceBox.SelectedIndex <= 0 ? null : VoiceBox.SelectedItem as string;
        _sched.Save();
    }

    private void RateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RateValue == null || VolumeValue == null) return;
        RateValue.Text = ((int)e.NewValue).ToString("+0;-0;0");
        if (_loading) return;
        S.Rate = (int)e.NewValue;
        _sched.Save();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeValue == null) return;
        VolumeValue.Text = $"{(int)e.NewValue}%";
        if (_loading) return;
        S.Volume = (int)e.NewValue;
        _sched.Save();
    }

    private void QuietToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.QuietEnabled = QuietToggle.IsChecked == true;
        _sched.Save();
        ApplyHourlyEnabled();
    }

    private void QuietTime_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var box = (TextBox)sender;
        if (TimeSpan.TryParse(box.Text, out var ts) && ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1))
        {
            string text = $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";
            box.Text = text;
            if (box == QuietFromBox) S.QuietFrom = text;
            else S.QuietTo = text;
            _sched.Save();
        }
        else
        {
            box.Text = box == QuietFromBox ? S.QuietFrom : S.QuietTo;
        }
    }

    private void HourlyToast_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.HourlyToast = HourlyToastToggle.IsChecked == true;
        _sched.Save();
    }

    private void BrowseChime_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择提示音",
            Filter = "WAV 音频 (*.wav)|*.wav|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;
        S.ChimePath = dlg.FileName;
        _sched.Save();
        UpdateChimeUi();
        UpdateHint();
        Tones.PlayChime(S);
    }

    private void ResetChime_Click(object sender, RoutedEventArgs e)
    {
        S.ChimePath = null;
        _sched.Save();
        UpdateChimeUi();
        UpdateHint();
        Tones.PlayChime(S);
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        var temp = new AppSettings
        {
            Volume = (int)VolumeSlider.Value,
            Rate = (int)RateSlider.Value,
            ChimePath = S.ChimePath,
            VoiceName = VoiceBox.SelectedIndex > 0 ? VoiceBox.SelectedItem as string : null
        };
        var now = DateTime.Now;
        var stamp = new DateTime(now.Year, now.Month, now.Day, now.Hour,
            now.Minute >= 30 ? 30 : 0, 0);
        // 试听同时也是提醒效果的预览：弹出一张预览卡片（真实整点是否弹窗仍由“整点同时弹窗”开关决定）
        _sched.PreviewToast();
        if (ModeChime.IsChecked == true)
        {
            Tones.PlayChime(temp);
            return;
        }
        if (ModeBoth.IsChecked == true) Tones.PlayChime(temp);
        _sched.Speech.Speak($"这是试听。{Texts.ClockSpoken(stamp)}", temp);
    }

    private async Task LoadVoicesAsync()
    {
        try
        {
            var voices = await Task.Run(() => _sched.Speech.Voices);
            if (voices.Count == 0)
            {
                // SAPI 初始化较慢时再试一次
                await Task.Delay(2200);
                voices = await Task.Run(() => _sched.Speech.Voices);
            }

            _loading = true;
            _voiceLoaded = true;
            VoiceBox.Items.Clear();
            VoiceBox.Items.Add("系统默认语音");
            foreach (var v in voices) VoiceBox.Items.Add(v);
            int idx = 0;
            if (S.VoiceName is { } name)
            {
                int found = VoiceBox.Items.IndexOf(name);
                if (found > 0) idx = found;
            }
            VoiceBox.SelectedIndex = idx;
            _loading = false;

            _voiceInfo = voices.Count > 0
                ? $"已识别到 {voices.Count} 个系统语音"
                : (S.VoiceName == null ? "未检测到系统语音，将只播放提示音" : "未找到指定语音，使用默认语音");
            UpdateHint();
        }
        catch
        {
            // 窗口已关闭时忽略
        }
    }

    // ---------- 闹钟 ----------

    public void RefreshAlarms()
    {
        var ordered = S.Alarms.OrderBy(a => a.Hour).ThenBy(a => a.Minute).ToList();
        _rows.Clear();
        foreach (var a in ordered)
            _rows.Add(new AlarmRow(a, () => { _sched.Save(); RefreshNext(DateTime.Now); }));

        CountText.Text = ordered.Count.ToString();
        bool empty = ordered.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        AlarmList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        RefreshNext(DateTime.Now);
    }

    private void AddAlarm_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AlarmDialog(null) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;
        S.Alarms.Add(dlg.Result);
        _sched.Save();
        RefreshAlarms();
    }

    private void EditAlarm_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AlarmRow row) return;
        var dlg = new AlarmDialog(row.Model) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _sched.Save();
        RefreshAlarms();
    }

    private void DeleteAlarm_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AlarmRow row) return;
        string name = string.IsNullOrWhiteSpace(row.Model.Label)
            ? $"{row.Model.Hour:00}:{row.Model.Minute:00}"
            : row.Model.Label;
        if (!ConfirmDialog.Ask(this, "删除闹钟", $"确定删除闹钟「{name}」吗？")) return;
        S.Alarms.Remove(row.Model);
        _sched.Save();
        RefreshAlarms();
    }

    private void AlarmEnable_Click(object sender, RoutedEventArgs e) => _sched.Save();

    private void AlarmRow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if ((sender as FrameworkElement)?.DataContext is not AlarmRow row) return;
        var dlg = new AlarmDialog(row.Model) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _sched.Save();
        RefreshAlarms();
    }

    private void RepeatSetting_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (RepeatMinutesBox.SelectedItem is int m) S.RepeatRemindMinutes = m;
        if (RepeatTimesBox.SelectedItem is int t) S.RepeatRemindTimes = t;
        _sched.Save();
    }

    // ---------- 偏好 ----------

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.AutoStart = AutoStartToggle.IsChecked == true;
        Platform.SetAutoStart(S.AutoStart);
        _sched.Save();
    }

    private void StartMin_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.StartMinimized = StartMinToggle.IsChecked == true;
        _sched.Save();
    }

    private void CloseTray_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.CloseToTray = CloseTrayToggle.IsChecked == true;
        _sched.Save();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_sched.IsPaused) _sched.Resume();
        else _sched.TogglePause(30);
        UpdateStatus();
    }

    // ---------- 窗口 ----------

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        try { DragMove(); } catch { }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_app.IsExiting && S.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }
}
