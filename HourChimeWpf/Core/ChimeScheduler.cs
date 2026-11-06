using System.Windows.Threading;

namespace HourChime;

/// <summary>一次提醒弹窗请求。OnClosed 参数为 true 表示用户点了按钮（或弹窗被新提醒顶替）。</summary>
public sealed record ToastRequest(
    string Title,
    string TimeText,
    string Message,
    string? SnoozeLabel,
    Action? OnSnooze,
    Action<bool>? OnClosed);

/// <summary>
/// 与界面无关的调度核心：整点报时、闹钟、贪睡、未确认重复提醒、夜间免打扰、临时暂停。
/// 所有成员均在 UI 线程（DispatcherTimer）上调用。
/// </summary>
public sealed class ChimeScheduler : IDisposable
{
    public AppSettings Settings { get; }
    public SpeechService Speech { get; }

    /// <summary>请求界面弹出一条提醒。</summary>
    public event Action<ToastRequest>? ToastRequested;
    /// <summary>闹钟集合或启用状态发生变化（含“仅一次”自动停用）。</summary>
    public event Action? AlarmsChanged;
    /// <summary>暂停状态、整点开关等影响界面显示的状态变化。</summary>
    public event Action? StateChanged;
    /// <summary>每 500ms 一次，供界面刷新时钟。</summary>
    public event Action<DateTime>? Tick;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly HashSet<string> _firedKeys = new();
    private readonly List<(string AlarmId, DateTime Due)> _snoozed = new();
    private readonly List<(string AlarmId, DateTime Due, int Attempt)> _repeats = new();
    private readonly Dictionary<string, int> _repeatCounts = new();

    private DateTime _pausedUntil = DateTime.MinValue;
    private string _lastHourKey;
    private bool _disposed;

    public ChimeScheduler(AppSettings settings, SpeechService speech)
    {
        Settings = settings;
        Speech = speech;
        // 启动时不补报当前整点/半点（只把“当前所在的报时点”标记为已报过）
        var boot = DateTime.Now;
        _lastHourKey = boot.ToString("yyyyMMddHH") + (boot.Minute >= 30 ? "H" : "0");

        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    public bool IsPaused => _pausedUntil > DateTime.Now;
    public DateTime PausedUntil => _pausedUntil;

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }

    // ---------- 调度 ----------

    private void OnTick()
    {
        if (_disposed) return;
        var now = DateTime.Now;
        Tick?.Invoke(now);

        if (_pausedUntil > now) return;

        RunHourly(now);
        RunAlarms(now);
        RunSnoozed(now);
        RunRepeats(now);
    }

    private void RunHourly(DateTime now)
    {
        if (!Settings.HourlyEnabled) return;

        // 整点 :00、半点 :30；均允许 90 秒内补报（防止刚唤醒/卡顿时错过）
        bool isHalf;
        int m = now.Minute;
        if (m == 0 || (m == 1 && now.Second <= 30)) isHalf = false;
        else if (Settings.HalfHourlyEnabled && (m == 30 || (m == 31 && now.Second <= 30))) isHalf = true;
        else return;

        string key = now.ToString("yyyyMMddHH") + (isHalf ? "H" : "0");
        if (_lastHourKey == key) return;
        _lastHourKey = key;

        if (IsQuietNow(now.TimeOfDay)) return;

        var stamp = new DateTime(now.Year, now.Month, now.Day, now.Hour, isHalf ? 30 : 0, 0);
        string spoken = Texts.ClockSpoken(stamp);

        if (Settings.HourlyMode is "voice" or "both")
            Speech.Speak(spoken, Settings);
        if (Settings.HourlyMode is "chime" or "both")
            Tones.PlayChime(Settings);
        if (Settings.HourlyToast)
            ShowToast(isHalf ? "半点报时" : "整点报时", stamp.ToString("HH:mm"), spoken, null, null);
    }

    private void RunAlarms(DateTime now)
    {
        foreach (var a in Settings.Alarms.ToList())
        {
            if (!a.Enabled) continue;
            if (a.Hour != now.Hour || a.Minute != now.Minute) continue;
            if (!Texts.RepeatMatches(a, now)) continue;

            string key = a.Id + now.ToString("yyyyMMddHHmm");
            if (_firedKeys.Contains(key)) continue;
            if (_firedKeys.Count > 300) _firedKeys.Clear();
            _firedKeys.Add(key);

            FireAlarm(a, now);
        }
    }

    private void RunSnoozed(DateTime now)
    {
        if (_snoozed.Count == 0) return;
        var due = _snoozed.Where(s => s.Due <= now).ToList();
        if (due.Count == 0) return;
        _snoozed.RemoveAll(s => s.Due <= now);
        foreach (var (alarmId, _) in due)
        {
            var a = Settings.Alarms.FirstOrDefault(x => x.Id == alarmId);
            if (a != null && a.Enabled) FireAlarm(a, now);
        }
    }

    private void RunRepeats(DateTime now)
    {
        if (_repeats.Count == 0) return;
        var due = _repeats.Where(r => r.Due <= now).ToList();
        if (due.Count == 0) return;
        _repeats.RemoveAll(r => r.Due <= now);
        foreach (var (alarmId, _, attempt) in due)
        {
            var a = Settings.Alarms.FirstOrDefault(x => x.Id == alarmId);
            // 重复提醒是同一闹钟的延续，即使“仅一次”已自动停用也继续
            if (a != null) FireAlarm(a, now, attempt);
        }
    }

    private void FireAlarm(AlarmItem a, DateTime now, int attempt = 0)
    {
        string label = string.IsNullOrWhiteSpace(a.Label) ? "闹钟提醒" : a.Label.Trim();
        switch (a.RingMode)
        {
            case "silent":
                break;
            case "chime":
                Tones.PlayChime(Settings);
                break;
            default:
                Speech.Speak(Texts.AlarmSpoken(a, now), Settings);
                Tones.PlayChime(Settings);
                break;
        }

        string message = string.IsNullOrWhiteSpace(a.Label) ? "闹钟时间到了" : $"{a.Label} · 时间到了";
        if (attempt > 0) message += $"（第 {attempt + 1} 次提醒）";

        ShowToast(label, now.ToString("HH:mm"), message,
            a.SnoozeMinutes > 0 ? $"稍后 {a.SnoozeMinutes} 分钟" : null,
            () => SnoozeAlarm(a),
            dismissed =>
            {
                if (dismissed)
                {
                    _repeatCounts.Remove(a.Id);
                    return;
                }
                // 弹窗自动关闭且用户未处理 → 稍后再响
                if (Settings.RepeatRemindMinutes <= 0) return;
                int count = _repeatCounts.TryGetValue(a.Id, out var c) ? c : 0;
                if (count >= Settings.RepeatRemindTimes) { _repeatCounts.Remove(a.Id); return; }
                _repeatCounts[a.Id] = count + 1;
                _repeats.Add((a.Id, DateTime.Now.AddMinutes(Settings.RepeatRemindMinutes), attempt + 1));
            });

        // “仅一次”闹钟首次触发后自动停用（还有贪睡挂起时先不停用）
        if (a.Repeat == RepeatMode.Once && attempt == 0
            && !_snoozed.Any(s => s.AlarmId == a.Id))
        {
            a.Enabled = false;
            Save();
            AlarmsChanged?.Invoke();
        }
    }

    private void SnoozeAlarm(AlarmItem a)
    {
        int minutes = a.SnoozeMinutes > 0 ? a.SnoozeMinutes : 5;
        _repeatCounts.Remove(a.Id);
        _snoozed.Add((a.Id, DateTime.Now.AddMinutes(minutes)));
    }

    private void ShowToast(string title, string timeText, string message,
        string? snoozeLabel, Action? onSnooze, Action<bool>? onClosed = null)
        => ToastRequested?.Invoke(new ToastRequest(title, timeText, message, snoozeLabel, onSnooze, onClosed));

    // ---------- 操作 ----------

    /// <summary>托盘菜单“立即报时”，不影响正常调度。</summary>
    public void AnnounceNow()
    {
        var now = DateTime.Now;
        var stamp = new DateTime(now.Year, now.Month, now.Day, now.Hour,
            now.Minute >= 30 ? 30 : 0, 0);
        string spoken = Texts.ClockSpoken(stamp);
        if (Settings.HourlyMode is "voice" or "both")
            Speech.Speak($"这是试听。{spoken}", Settings);
        if (Settings.HourlyMode is "chime" or "both")
            Tones.PlayChime(Settings);
        ShowToast("报时试听", stamp.ToString("HH:mm"), spoken, null, null);
    }

    /// <summary>试听时弹出的预览提醒卡片：始终显示，用于预览真实提醒的样子，不影响正常调度。</summary>
    public void PreviewToast()
    {
        var now = DateTime.Now;
        var stamp = new DateTime(now.Year, now.Month, now.Day, now.Hour,
            now.Minute >= 30 ? 30 : 0, 0);
        ShowToast("报时试听（预览）", stamp.ToString("HH:mm"), Texts.ClockSpoken(stamp), null, null);
    }

    public void TogglePause(int minutes)
    {
        _pausedUntil = IsPaused ? DateTime.MinValue : DateTime.Now.AddMinutes(minutes);
        StateChanged?.Invoke();
    }

    public void Resume()
    {
        _pausedUntil = DateTime.MinValue;
        StateChanged?.Invoke();
    }

    public void Save()
    {
        SettingsStore.Save(Settings);
        StateChanged?.Invoke();
    }

    public bool IsQuietNow(TimeSpan t)
    {
        if (!Settings.QuietEnabled) return false;
        var from = ParseTime(Settings.QuietFrom);
        var to = ParseTime(Settings.QuietTo);
        if (from == to) return false;
        return from < to ? (t >= from && t < to) : (t >= from || t < to);
    }

    public static TimeSpan ParseTime(string s)
        => TimeSpan.TryParse(s, out var ts) ? ts : new TimeSpan(22, 0, 0);

    /// <summary>下一次将触发的闹钟。</summary>
    public (DateTime At, AlarmItem Alarm)? NextAlarm(DateTime now)
    {
        AlarmItem? best = null;
        DateTime bestTime = DateTime.MaxValue;
        foreach (var a in Settings.Alarms.Where(x => x.Enabled))
        {
            var t = new DateTime(now.Year, now.Month, now.Day, a.Hour, a.Minute, 0);
            if (t <= now) t = t.AddDays(1);
            // 仅一次闹钟最多向后找 8 天
            for (int i = 0; i < 8 && !Texts.RepeatMatches(a, t); i++)
                t = t.AddDays(1);
            if (!Texts.RepeatMatches(a, t)) continue;
            if (t < bestTime) { bestTime = t; best = a; }
        }
        return best == null ? null : (bestTime, best);
    }
}
