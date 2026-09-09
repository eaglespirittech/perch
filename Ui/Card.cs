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

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = Theme.CardRadius;

    /// <summary>Overrides the surface colour, for field-style wells.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color? Fill { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Window);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Theme.RoundedRect(bounds, Radius);
        using var fill = new SolidBrush(Fill ?? Theme.Surface);
        using var pen = new Pen(Theme.Border);

        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        base.OnPaint(e);
    }
}
