using System.Windows;
using System.Windows.Threading;

namespace HourChime;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("AppDomain", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Task", e.Exception);
            e.SetObserved();
        };

        var app = new App();
        app.DispatcherUnhandledException += (_, e) =>
        {
            CrashLog.Write("Dispatcher", e.Exception);
            e.Handled = true;   // 托盘工具优先保持存活
        };
        app.Run();
    }
}
