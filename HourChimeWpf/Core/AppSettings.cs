using System.Text.Json;
using System.Text.Json.Serialization;

namespace HourChime;

public enum RepeatMode
{
    Daily,
    Weekdays,
    Weekend,
    Once
}

public sealed class AlarmItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Hour { get; set; } = 9;
    public int Minute { get; set; } = 0;
    public string Label { get; set; } = "";
    public RepeatMode Repeat { get; set; } = RepeatMode.Daily;
    public string? OnceDate { get; set; }          // yyyy-MM-dd，仅一次时生效
    public bool Enabled { get; set; } = true;
    public int SnoozeMinutes { get; set; } = 5;    // 0 = 不贪睡
    public string RingMode { get; set; } = "both"; // both | chime | silent
}

public sealed class AppSettings
{
    public bool HourlyEnabled { get; set; } = true;
    public bool HalfHourlyEnabled { get; set; } = true;  // 半点（:30）也播报
    public string HourlyMode { get; set; } = "both";   // voice | chime | both
    public string? VoiceName { get; set; }
    public int Rate { get; set; } = 0;                 // -10 ~ 10
    public int Volume { get; set; } = 90;              // 0 ~ 100
    public bool QuietEnabled { get; set; } = false;
    public bool HourlyToast { get; set; } = false;
    public int RepeatRemindMinutes { get; set; } = 2;   // 0 = 不重复
    public int RepeatRemindTimes { get; set; } = 3;     // 最多重复次数
    public string? ChimePath { get; set; }              // 自定义 WAV，为空则用内置提示音
    public string QuietFrom { get; set; } = "22:00";
    public string QuietTo { get; set; } = "07:00";
    public bool AutoStart { get; set; } = false;
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public List<AlarmItem> Alarms { get; set; } = new();
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HourChime");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
                if (s != null) return s;
            }
        }
        catch
        {
            // 配置损坏时回退默认值
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // 忽略写入失败（如磁盘只读），下次再试
        }
    }
}

public static class Texts
{
    private static readonly string[] D = { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };

    public static string CnNumber(int n)
    {
        if (n < 10) return D[n];
        if (n < 20) return "十" + (n % 10 == 0 ? "" : D[n % 10]);
        return D[n / 10] + "十" + (n % 10 == 0 ? "" : D[n % 10]);
    }

    public static string Period(int hour)
    {
        return hour switch
        {
            < 6 => "凌晨",
            < 12 => "上午",
            < 13 => "中午",
            < 18 => "下午",
            _ => "晚上"
        };
    }

    public static string HourlySpoken(DateTime t)
    {
        int h = t.Hour == 12 ? 12 : t.Hour % 12;
        return $"现在是{Period(t.Hour)}{CnNumber(h)}点整。";
    }

    /// <summary>整点/半点通用播报文案。</summary>
    public static string ClockSpoken(DateTime t)
    {
        if (t.Minute != 30) return HourlySpoken(t);
        int h = t.Hour == 12 ? 12 : t.Hour % 12;
        // 0:30 用“零点三十分”更自然
        return h == 0
            ? $"现在是{Period(t.Hour)}零点三十分。"
            : $"现在是{Period(t.Hour)}{CnNumber(h)}点半。";
    }

    public static string AlarmSpoken(AlarmItem a, DateTime now)
    {
        var label = string.IsNullOrWhiteSpace(a.Label) ? "闹钟" : a.Label.Trim();
        int h = now.Hour == 12 ? 12 : now.Hour % 12;
        var minPart = now.Minute == 0 ? "整" : $"{CnNumber(now.Minute)}分";
        return $"{label}，时间到了。现在是{Period(now.Hour)}{CnNumber(h)}点{minPart}。";
    }

    public static string RepeatText(AlarmItem a)
    {
        return a.Repeat switch
        {
            RepeatMode.Daily => "每天",
            RepeatMode.Weekdays => "工作日",
            RepeatMode.Weekend => "周末",
            RepeatMode.Once => a.OnceDate is { } d ? DateTime.TryParse(d, out var dt) ? $"仅 {dt:MM月dd日}" : "仅一次" : "仅一次",
            _ => ""
        };
    }

    public static string RingModeText(string mode) => mode switch
    {
        "chime" => "仅提示音",
        "silent" => "静音弹窗",
        _ => "语音+提示音"
    };

    public static bool RepeatMatches(AlarmItem a, DateTime now)
    {
        return a.Repeat switch
        {
            RepeatMode.Daily => true,
            RepeatMode.Weekdays => now.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday,
            RepeatMode.Weekend => now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
            RepeatMode.Once => a.OnceDate != null && a.OnceDate == now.ToString("yyyy-MM-dd"),
            _ => false
        };
    }
}
