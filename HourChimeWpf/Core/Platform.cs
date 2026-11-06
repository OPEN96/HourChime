using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace HourChime;

/// <summary>开机自启、窗口圆角、配置目录等 Windows 平台辅助。</summary>
public static class Platform
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "HourChime";

    public static string ExePath =>
        Process.GetCurrentProcess().MainModule?.FileName
        ?? Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, "HourChime.exe");

    public static void SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (enabled) key.SetValue(RunValue, $"\"{ExePath}\"");
            else key.DeleteValue(RunValue, false);
        }
        catch
        {
            // 注册表不可写时忽略
        }
    }

    public static void OpenConfigFolder()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SettingsStore.Dir}\"")
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    // Windows 11: DWMWA_WINDOW_CORNER_PREFERENCE
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>让无边框窗口在 Windows 11 上使用系统圆角（Win10 上调用失败即忽略）。</summary>
    public static void EnableRoundedCorners(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int pref = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref pref, sizeof(int));
        }
        catch { }
    }
}
