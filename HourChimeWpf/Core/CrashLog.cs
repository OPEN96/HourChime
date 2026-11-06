namespace HourChime;

/// <summary>极简崩溃日志：写入 %APPDATA%\HourChime\crash.log，便于排查“该响没响/闪退”。</summary>
public static class CrashLog
{
    public static string FilePath => Path.Combine(SettingsStore.Dir, "crash.log");

    public static void Write(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Dir);
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\r\n{ex}\r\n\r\n";
            File.AppendAllText(FilePath, text);
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }
}
