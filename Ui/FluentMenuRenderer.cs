using System.Drawing.Drawing2D;

namespace Perch.Ui;

/// <summary>
/// Repaints ContextMenuStrip so the menu matches the rest of the window instead of
/// falling back to the classic grey chrome.
/// </summary>
public sealed class FluentMenuRenderer : ToolStripProfessionalRenderer
{
    public FluentMenuRenderer() : base(new Colors()) { RoundedEdges = false; }

    sealed class Colors : ProfessionalColorTable
    {
        public Colors() => UseSystemColors = false;

        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(Theme.Surface);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        if (!item.Selected || !item.Enabled)
            return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new RectangleF(2, 1, item.Width - 4, item.Height - 2);
        using var path = Theme.RoundedRect(bounds, 5);
        using var brush = new SolidBrush(Theme.FieldHover);
        g.FillPath(brush, path);

        // The Windows 11 selection pill: a short accent bar on the leading edge.
        var bar = new RectangleF(bounds.Left + 1, bounds.Top + bounds.Height * 0.3f, 3, bounds.Height * 0.4f);
        using var barPath = Theme.RoundedRect(bar, 1.5f);
        using var accent = new SolidBrush(Theme.Accent);
        g.FillPath(accent, barPath);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextDisabled;
        e.TextFont = Theme.Body;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.TextSecondary;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var box = e.ImageRectangle;
        using var pen = new Pen(Theme.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[]
        {
            new PointF(box.Left + 3, box.Top + box.Height / 2f),
            new PointF(box.Left + box.Width / 2f - 1, box.Bottom - 4),
            new PointF(box.Right - 3, box.Top + 3)
        });
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }
}
