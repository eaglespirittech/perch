using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Perch.Ui;

/// <summary>An icon on a soft accent square, used to mark what a card is about.</summary>
public class GlyphBadge : Control
{
    public GlyphBadge()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph { get; set; } = string.Empty;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Backdrop(this));

        using (var path = Theme.RoundedRect(new RectangleF(0, 0, Width, Height), this.Dp(10)))
        using (var brush = new SolidBrush(Theme.AccentSoft))
            g.FillPath(brush, path);

        TextRenderer.DrawText(g, Glyph, Theme.Icon, ClientRectangle, Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
