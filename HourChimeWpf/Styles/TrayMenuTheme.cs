using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HourChime;

/// <summary>托盘右键菜单的深色配色，与应用主界面保持一致。</summary>
internal sealed class DarkMenuColorTable : ProfessionalColorTable
{
    private static Color C(string hex) => ColorTranslator.FromHtml(hex);

    public static readonly Color Bg = C("#151A22");
    public static readonly Color Border = C("#2A3242");
    public static readonly Color Hover = C("#232C3A");
    public static readonly Color Pressed = C("#1C2431");
    public static readonly Color Separator = C("#313B4C");
    public static readonly Color Text = C("#E8ECF3");
    public static readonly Color Sub = C("#93A0B4");
    public static readonly Color Muted = C("#5E6878");
    public static readonly Color Accent = C("#F2B24A");
    public static readonly Color Danger = C("#E0736B");

    public override Color ToolStripDropDownBackground => Bg;
    public override Color ImageMarginGradientBegin => Bg;
    public override Color ImageMarginGradientMiddle => Bg;
    public override Color ImageMarginGradientEnd => Bg;
    public override Color MenuBorder => Border;
    public override Color MenuItemBorder => Border;
    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color MenuItemPressedGradientBegin => Pressed;
    public override Color MenuItemPressedGradientMiddle => Pressed;
    public override Color MenuItemPressedGradientEnd => Pressed;
    public override Color SeparatorDark => Separator;
    public override Color SeparatorLight => Separator;
    public override Color CheckBackground => Hover;
    public override Color CheckSelectedBackground => Pressed;
    public override Color CheckPressedBackground => Pressed;
}

/// <summary>
/// 深色菜单渲染器：选中项高亮、勾选项用琥珀色勾号 + 加亮文字，让开关状态一眼可见。
/// （WinForms 默认主题下 ShowImageMargin=false 时勾号不显示，导致“点了没反应”的错觉）
/// </summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkMenuColorTable())
    {
        RoundedEdges = false;
    }

    /// <summary>给开关类项目打标记，选中时文字用强调色。</summary>
    public static bool IsToggle(ToolStripItem? item)
        => item is ToolStripMenuItem { CheckOnClick: true };

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var item = e.Item;
        bool disabled = item is null || !item.Enabled;
        bool on = item is ToolStripMenuItem { Checked: true };
        bool danger = item?.Tag as string == "danger";

        e.TextColor = disabled ? DarkMenuColorTable.Muted
            : item is ToolStripLabel ? DarkMenuColorTable.Sub
            : on ? DarkMenuColorTable.Accent
            : danger ? DarkMenuColorTable.Danger
            : DarkMenuColorTable.Text;

        base.OnRenderItemText(e);
    }

    /// <summary>所有开关项统一使用同一方框位置，保证「空心框」与「实心勾」完全对齐。</summary>
    private static Rectangle CheckRect(ToolStripItem item)
    {
        const int size = 14;
        return new Rectangle(6, (item.Height - size) / 2, size, size);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var box = CheckRect(e.Item);
        int size = box.Width;

        using (var path = Round(box, 4))
        {
            using var fill = new SolidBrush(DarkMenuColorTable.Accent);
            g.FillPath(fill, path);
        }

        // 深色对勾
        using var pen = new Pen(Color.FromArgb(26, 20, 8), Math.Max(1.6f, size * 0.16f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        float cx = box.X + box.Width * 0.30f, cy = box.Y + box.Height * 0.52f;
        float mx = box.X + box.Width * 0.45f, my = box.Y + box.Height * 0.68f;
        float rx = box.X + box.Width * 0.72f, ry = box.Y + box.Height * 0.34f;
        g.DrawLines(pen, new[] { new PointF(cx, cy), new PointF(mx, my), new PointF(rx, ry) });
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var item = e.Item;

        if (item.Selected || item.Pressed)
        {
            var rect = new Rectangle(3, 1, item.Width - 6, item.Height - 3);
            using var path = Round(rect, 5);
            using var fill = new SolidBrush(item.Pressed
                ? DarkMenuColorTable.Pressed
                : DarkMenuColorTable.Hover);
            g.FillPath(fill, path);
        }

        // 未勾选的开关项画空心框：点击后变成实心琥珀勾，状态变化一眼可见
        if (item is ToolStripMenuItem { CheckOnClick: true, Checked: false } && item.Enabled)
        {
            using var outline = Round(CheckRect(item), 4);
            using var pen = new Pen(Color.FromArgb(88, 102, 124), 1.3f);
            g.DrawPath(pen, outline);
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var g = e.Graphics;
        int y = e.Item.Height / 2;
        using var pen = new Pen(DarkMenuColorTable.Separator);
        // 两侧留白，虚线感更轻
        g.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        using var path = Round(r, 8);
        using var pen = new Pen(DarkMenuColorTable.Border);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath Round(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        if (r.Width <= d || r.Height <= d)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
