using System.Windows;
using System.Windows.Input;

namespace HourChime.Views;

public partial class AlarmDialog : Window
{
    private sealed record SnoozeOption(int Minutes, string Text);
    private sealed record DateOption(DateTime Date, string Text);

    private readonly AlarmItem _target;

    public AlarmItem? Result { get; private set; }

    public AlarmDialog(AlarmItem? editing)
    {
        InitializeComponent();
        _target = editing ?? new AlarmItem();
        TitleText.Text = editing == null ? "新建闹钟" : "编辑闹钟";

        HourBox.ItemsSource = Enumerable.Range(0, 24).Select(i => i.ToString("00")).ToList();
        MinuteBox.ItemsSource = Enumerable.Range(0, 60).Select(i => i.ToString("00")).ToList();
        HourBox.SelectedItem = _target.Hour.ToString("00");
        MinuteBox.SelectedItem = _target.Minute.ToString("00");

        LabelBox.Text = _target.Label;

        RepeatDaily.IsChecked = _target.Repeat == RepeatMode.Daily;
        RepeatWeekdays.IsChecked = _target.Repeat == RepeatMode.Weekdays;
        RepeatWeekend.IsChecked = _target.Repeat == RepeatMode.Weekend;
        RepeatOnce.IsChecked = _target.Repeat == RepeatMode.Once;

        RingBoth.IsChecked = _target.RingMode is not ("chime" or "silent");
        RingChime.IsChecked = _target.RingMode == "chime";
        RingSilent.IsChecked = _target.RingMode == "silent";

        var snoozes = new List<SnoozeOption> { new(0, "不贪睡") };
        foreach (var m in new[] { 1, 2, 3, 5, 10, 15, 20, 30, 45, 60 })
            snoozes.Add(new SnoozeOption(m, $"{m} 分钟"));
        SnoozeBox.ItemsSource = snoozes;
        SnoozeBox.SelectedItem = snoozes.FirstOrDefault(s => s.Minutes == _target.SnoozeMinutes) ?? snoozes[4];

        var dates = new List<DateOption>();
        var today = DateTime.Today;
        for (int i = 0; i < 30; i++)
        {
            var d = today.AddDays(i);
            string prefix = i == 0 ? "今天" : i == 1 ? "明天" : i == 2 ? "后天" : WeekdayCn(d.DayOfWeek);
            dates.Add(new DateOption(d, $"{prefix} · {d:M月d日}"));
        }
        DateBox.ItemsSource = dates;
        var wanted = dates.FirstOrDefault(d =>
            _target.OnceDate != null && d.Date.ToString("yyyy-MM-dd") == _target.OnceDate);
        DateBox.SelectedItem = wanted ?? dates[0];

        DateRow.Visibility = _target.Repeat == RepeatMode.Once ? Visibility.Visible : Visibility.Collapsed;

        SourceInitialized += (_, _) => Platform.EnableRoundedCorners(this);
        Loaded += (_, _) => LabelBox.Focus();
    }

    private static string WeekdayCn(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };

    private void Repeat_Checked(object sender, RoutedEventArgs e)
    {
        if (DateRow == null) return;
        DateRow.Visibility = RepeatOnce.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        int hour = int.Parse((string)HourBox.SelectedItem);
        int minute = int.Parse((string)MinuteBox.SelectedItem);

        _target.Hour = hour;
        _target.Minute = minute;
        _target.Label = LabelBox.Text.Trim();
        _target.Repeat = RepeatDaily.IsChecked == true ? RepeatMode.Daily
            : RepeatWeekdays.IsChecked == true ? RepeatMode.Weekdays
            : RepeatWeekend.IsChecked == true ? RepeatMode.Weekend
            : RepeatMode.Once;
        _target.OnceDate = _target.Repeat == RepeatMode.Once && DateBox.SelectedItem is DateOption opt
            ? opt.Date.ToString("yyyy-MM-dd")
            : null;
        _target.RingMode = RingChime.IsChecked == true ? "chime"
            : RingSilent.IsChecked == true ? "silent"
            : "both";
        _target.SnoozeMinutes = SnoozeBox.SelectedItem is SnoozeOption s ? s.Minutes : 5;

        Result = _target;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        try { DragMove(); } catch { }
    }
}
