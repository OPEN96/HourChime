using System.Threading;
using System.Windows;
using HourChime.Views;

namespace HourChime;

/// <summary>
/// 应用主体。这里不使用 App.xaml 的自动入口点（与 WinForms 托盘组件同时启用时会冲突），
/// 由 Program.Main 显式创建并合并主题资源。
/// </summary>
public sealed class App : System.Windows.Application
{
    private Mutex? _mutex;
    private TrayHost? _tray;
    private ToastWindow? _toast;

    public AppSettings Settings { get; private set; } = new();
    public ChimeScheduler Scheduler { get; private set; } = null!;
    public MainWindow? Main { get; private set; }
    public bool IsExiting { get; private set; }

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("Styles/Theme.xaml", UriKind.Relative)
        });
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 与 WinForms 版共用同一单实例锁，避免两个版本同时响铃
        _mutex = new Mutex(true, "HourChime.SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("「整点报时」已经在运行了。\n请查看屏幕右下角的系统托盘图标。",
                "整点报时", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Settings = SettingsStore.Load();
        Scheduler = new ChimeScheduler(Settings, new SpeechService());
        Scheduler.ToastRequested += OnToastRequested;

        _tray = new TrayHost(this);

        bool minimized = e.Args.Contains("/minimized", StringComparer.OrdinalIgnoreCase)
                         || Settings.StartMinimized;
        if (minimized) _tray.ShowStartupBalloon();
        else ShowMain();

        // 调试用：验证“试听”弹出的预览提醒卡片
        if (e.Args.Contains("/previewtoast", StringComparer.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(new Action(Scheduler.PreviewToast),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // 调试用：把托盘右键菜单固定显示出来，/traypreview:<png路径> 额外自渲染成图片后退出
        if (e.Args.FirstOrDefault(a => a.StartsWith("/traypreview", StringComparison.OrdinalIgnoreCase)) is { } trayArg)
        {
            int colon = trayArg.IndexOf(':');
            string? trayPng = colon > 0 ? trayArg[(colon + 1)..] : null;
            Dispatcher.BeginInvoke(new Action(() =>
                    _tray?.ShowMenuPreview(trayPng, trayPng is null ? null : Shutdown)),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        // 调试用：把主窗口自渲染成 PNG（不受 DWM/PrintWindow 影响）
        if (e.Args.FirstOrDefault(a => a.StartsWith("/snapshot:", StringComparison.OrdinalIgnoreCase)) is { } snapArg)
        {
            string snapPath = snapArg[10..];
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (Main == null) ShowMain();
                    await System.Threading.Tasks.Task.Delay(1500);
                    var w = Main!;
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        (int)w.ActualWidth, (int)w.ActualHeight, 96, 96,
                        System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(w);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    using var fs = System.IO.File.Create(snapPath);
                    enc.Save(fs);
                }
                catch (Exception ex) { CrashLog.Write("Snapshot", ex); }
                finally { Shutdown(); }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    public void ShowMain()
    {
        // 窗口被真正关闭过（用户关闭了“关闭时最小化到托盘”）时需要重建，
        // 对已关闭的 Window 调用 Show() 会抛异常
        if (Main == null || !Main.IsLoaded)
        {
            var w = new MainWindow(this);
            w.Closed += (_, _) =>
            {
                if (ReferenceEquals(Main, w)) Main = null;
            };
            Main = w;
            w.Show();
        }
        else
        {
            if (Main.WindowState == WindowState.Minimized)
                Main.WindowState = WindowState.Normal;
            Main.Show();
            Main.Activate();
        }
    }

    private void OnToastRequested(ToastRequest req)
    {
        try
        {
            _toast?.CloseQuietly();
            var w = new ToastWindow(req);
            w.Closed += (_, _) =>
            {
                if (ReferenceEquals(_toast, w)) _toast = null;
                req.OnClosed?.Invoke(w.DismissedByUser);
            };
            w.Show();
            _toast = w;
        }
        catch
        {
            req.OnClosed?.Invoke(true);
        }
    }

    public void ExitApp()
    {
        if (IsExiting) return;
        IsExiting = true;
        try { Scheduler?.Dispose(); } catch { }
        try { Scheduler?.Speech.Stop(); } catch { }
        try { Scheduler?.Speech.Dispose(); } catch { }
        try { _toast?.CloseQuietly(); } catch { }
        _tray?.Dispose();
        try { Main?.Close(); } catch { }
        Shutdown();
    }
}
