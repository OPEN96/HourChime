using System.Drawing;
using System.Windows.Forms;

namespace HourChime;

/// <summary>系统托盘图标与右键菜单（借用 WinForms NotifyIcon，WPF 无内置托盘 API）。</summary>
public sealed class TrayHost : IDisposable
{
    private readonly App _app;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _miPause;
    private readonly ToolStripMenuItem _miHourly;
    private readonly ToolStripMenuItem _miHalf;
    private readonly ToolStripLabel _header;
    private readonly Font _font = new("Microsoft YaHei UI", 9.5f);
    private readonly Font _headerFont = new("Microsoft YaHei UI", 8.5f);
    private bool _disposed;

    public TrayHost(App app)
    {
        _app = app;

        _header = new ToolStripLabel("")
        {
            Font = _headerFont,
            ForeColor = DarkMenuColorTable.Sub,
            Padding = new Padding(9, 3, 12, 5),
            Margin = Padding.Empty
        };

        _miHourly = new ToolStripMenuItem("整点报时")
        {
            CheckOnClick = true,
            Checked = app.Settings.HourlyEnabled
        };
        _miHourly.CheckedChanged += OnHourlyMenuChanged;

        _miHalf = new ToolStripMenuItem("半点也播报")
        {
            CheckOnClick = true,
            Checked = app.Settings.HalfHourlyEnabled
        };
        _miHalf.CheckedChanged += OnHalfHourlyMenuChanged;

        _miPause = new ToolStripMenuItem("暂停提醒 30 分钟");
        _miPause.Click += (_, _) => app.Scheduler.TogglePause(30);

        _menu = new ContextMenuStrip
        {
            // 注意：不要设置 RenderMode，否则会把 Renderer 重置回系统默认主题
            Renderer = new DarkMenuRenderer(),
            // 必须保留图标/勾选预留区，否则 CheckOnClick 的勾号不显示（“点了没反应”的根源）
            ShowImageMargin = true,
            ShowCheckMargin = true,
            ImageScalingSize = new Size(18, 18),
            Font = _font,
            BackColor = DarkMenuColorTable.Bg,
            ForeColor = DarkMenuColorTable.Text,
            DropShadowEnabled = true,
            Padding = new Padding(2, 4, 2, 4),
            MinimumSize = new Size(224, 0)
        };

        _menu.Items.Add(_header);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("打开主窗口", (_, _) => app.ShowMain()));
        _menu.Items.Add(Item("立即报时（试听）", (_, _) => app.Scheduler.AnnounceNow()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_miHourly);
        _menu.Items.Add(_miHalf);
        _menu.Items.Add(_miPause);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("打开配置文件目录", (_, _) => Platform.OpenConfigFolder()));
        _menu.Items.Add(new ToolStripSeparator());

        var exit = Item("退出", (_, _) => app.ExitApp());
        exit.Tag = "danger";
        _menu.Items.Add(exit);

        _menu.Opening += (_, _) => UpdateHeader();

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "整点报时",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _icon.DoubleClick += (_, _) => app.ShowMain();

        app.Scheduler.Tick += OnTick;
        app.Scheduler.StateChanged += SyncMenu;

        UpdateHeader();
    }

    private ToolStripMenuItem Item(string text, EventHandler onClick)
    {
        var mi = new ToolStripMenuItem(text) { Padding = new Padding(0, 3, 0, 3) };
        mi.Click += onClick;
        return mi;
    }

    private static Icon LoadIcon()
    {
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Platform.ExePath);
            if (icon != null) return icon;
        }
        catch { }
        return SystemIcons.Application;
    }

    /// <summary>菜单顶部状态行：当前状态 + 下一个闹钟。</summary>
    private void UpdateHeader()
    {
        if (_disposed) return;
        try
        {
            string state = _app.Scheduler.IsPaused
                ? $"已暂停至 {_app.Scheduler.PausedUntil:HH:mm}"
                : !_app.Settings.HourlyEnabled
                    ? "报时已关闭"
                    : _app.Settings.HalfHourlyEnabled ? "整点+半点中" : "整点报时中";

            var next = _app.Scheduler.NextAlarm(DateTime.Now);
            string nextText = next is { } n
                ? $"下一个 {(n.At.Date == DateTime.Today ? n.At.ToString("HH:mm") : n.At.ToString("M月d日 HH:mm"))}"
                : "无闹钟";

            _header.Text = $"{state}  ·  {nextText}";
        }
        catch { }
    }

    private void OnTick(DateTime now)
    {
        if (_disposed) return;
        try
        {
            string status = _app.Scheduler.IsPaused ? "已暂停"
                : _app.Settings.HourlyEnabled
                    ? (_app.Settings.HalfHourlyEnabled ? "整点+半点" : "整点报时")
                    : "报时已关闭";
            _icon.Text = $"整点报时 · {status}";
        }
        catch
        {
            // 提示文本异常忽略
        }

        string pauseText = _app.Scheduler.IsPaused
            ? $"恢复提醒（暂停至 {_app.Scheduler.PausedUntil:HH:mm}）"
            : "暂停提醒 30 分钟";
        if (_miPause.Text != pauseText) _miPause.Text = pauseText;

        // 菜单打开时才需要刷新头部，避免每秒重排
        if (_menu.Visible) UpdateHeader();
    }

    private void SyncMenu()
    {
        if (_disposed) return;
        if (_miHourly.Checked != _app.Settings.HourlyEnabled)
        {
            // 避免触发 CheckedChanged 再次写配置
            _miHourly.CheckedChanged -= OnHourlyMenuChanged;
            _miHourly.Checked = _app.Settings.HourlyEnabled;
            _miHourly.CheckedChanged += OnHourlyMenuChanged;
        }
        if (_miHalf.Checked != _app.Settings.HalfHourlyEnabled)
        {
            _miHalf.CheckedChanged -= OnHalfHourlyMenuChanged;
            _miHalf.Checked = _app.Settings.HalfHourlyEnabled;
            _miHalf.CheckedChanged += OnHalfHourlyMenuChanged;
        }
        _miHalf.Enabled = _app.Settings.HourlyEnabled;
        UpdateHeader();
    }

    private void OnHourlyMenuChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _app.Settings.HourlyEnabled = _miHourly.Checked;
        _app.Scheduler.Save();
        // 窗口可能已被真正关闭（关闭时最小化到托盘处于关闭状态），此时不要再碰界面
        if (_app.Main is { IsLoaded: true } main) main.SyncFromSettings();
    }

    private void OnHalfHourlyMenuChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _app.Settings.HalfHourlyEnabled = _miHalf.Checked;
        _app.Scheduler.Save();
        if (_app.Main is { IsLoaded: true } main) main.SyncFromSettings();
    }

    /// <summary>
    /// 调试用：把菜单固定显示在屏幕上，便于截图核对样式。
    /// 传入 savePng 时用控件自渲染输出，比外部抓窗口稳定；完成后回调 onDone（可用于自动退出）。
    /// </summary>
    public void ShowMenuPreview(string? savePng = null, Action? onDone = null)
    {
        try
        {
            UpdateHeader();
            var wa = Screen.PrimaryScreen!.WorkingArea;
            _menu.Show(new Point(wa.Left + 120, wa.Top + 120));
            CrashLog.Write("TrayMenuShown", null);

            if (string.IsNullOrWhiteSpace(savePng))
            {
                onDone?.Invoke();
                return;
            }

            string path = savePng;
            _menu.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await Task.Delay(400);
                    _menu.Refresh();
                    using var bmp = new Bitmap(Math.Max(1, _menu.Width), Math.Max(1, _menu.Height));
                    _menu.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    CrashLog.Write("TrayMenuSnapshot", null);
                }
                catch (Exception ex)
                {
                    CrashLog.Write("TrayMenuSnapshot", ex);
                }
                finally
                {
                    onDone?.Invoke();
                }
            }));
        }
        catch (Exception ex)
        {
            CrashLog.Write("TrayMenuShow", ex);
            onDone?.Invoke();
        }
    }

    public void ShowStartupBalloon()
    {
        try
        {
            _icon.BalloonTipTitle = "整点报时已在后台运行";
            _icon.BalloonTipText = "整点会自动为您报时。双击托盘图标可打开设置。";
            _icon.ShowBalloonTip(3000);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _app.Scheduler.Tick -= OnTick; } catch { }
        try { _app.Scheduler.StateChanged -= SyncMenu; } catch { }
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _font.Dispose();
        _headerFont.Dispose();
    }
}
