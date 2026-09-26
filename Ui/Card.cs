using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace Perch.Ui;

/// <summary>A rounded surface panel. Everything in the window sits on one of these.</summary>
public class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>Corner radius in 96-DPI pixels.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = Theme.CardRadius;

    /// <summary>Overrides the surface colour, for field-style wells.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? Fill { get; set; }

    /// <summary>Overrides the hairline border, e.g. to show focus.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? Stroke { get; set; }

    /// <summary>Whether the window draws a soft shadow under this card.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Elevated { get; set; }

    public Color SurfaceColor => Fill ?? Theme.Surface;

    /// <summary>Children clear to this, so their rounded corners blend into the card.</summary>
    [AllowNull]
    public override Color BackColor
    {
        get => SurfaceColor;
        set { }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Backdrop(this));

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Theme.RoundedRect(bounds, this.Dp(Radius));
        PaintSurface(g, path, bounds);

        using var pen = new Pen(Stroke ?? Theme.Border, Stroke is null ? 1f : this.Dp(1.5f));
        g.DrawPath(pen, path);

        base.OnPaint(e);
    }

    /// <summary>Fills the card body. Overridden by cards with a richer background.</summary>
    protected virtual void PaintSurface(Graphics g, GraphicsPath path, RectangleF bounds)
    {
        using var fill = new SolidBrush(SurfaceColor);
        g.FillPath(fill, path);
    }
}
