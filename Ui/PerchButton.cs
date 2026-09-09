using System.Drawing.Drawing2D;

namespace Perch.Ui;

public enum ButtonKind
{
    /// <summary>Accent filled. One per view, on the thing you most likely want.</summary>
    Primary,

    /// <summary>Neutral fill with a hairline border.</summary>
    Standard,

    /// <summary>No fill until hovered. For low-frequency actions.</summary>
    Subtle
}

/// <summary>
/// A flat, rounded button drawn by hand. Derives from Button so focus, keyboard
/// activation and accessibility keep working; only the painting is ours.
/// </summary>
public class PerchButton : Button
{
    bool _hovered;
    bool _pressed;

    public PerchButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind { get; set; } = ButtonKind.Standard;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = Theme.ControlRadius;

    /// <summary>Draw the text in the icon font instead of the body font.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsGlyph { get; set; }

    /// <summary>Sizes the button to its text, which is what keeps labels from clipping.</summary>
    public void SizeToText(int horizontalPadding = 20, int height = 34, int minimumWidth = 0)
    {
        var font = IsGlyph ? Theme.Icon : Theme.Body;
        var text = TextRenderer.MeasureText(Text, font).Width;
        Size = new Size(Math.Max(text + horizontalPadding * 2, minimumWidth), height);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Surface);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Theme.RoundedRect(bounds, Radius);

        var (fill, border, text) = Colors();

        if (fill.A > 0)
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }

        if (border.A > 0)
        {
            using var pen = new Pen(border);
            g.DrawPath(pen, path);
        }

        if (Focused && Enabled && ShowFocusCues)
        {
            var focus = RectangleF.Inflate(bounds, -2.5f, -2.5f);
            using var focusPath = Theme.RoundedRect(focus, Math.Max(Radius - 2, 1));
            using var focusPen = new Pen(text, 1.5f) { DashStyle = DashStyle.Dot };
            g.DrawPath(focusPen, focusPath);
        }

        TextRenderer.DrawText(g, Text, IsGlyph ? Theme.Icon : Theme.Body, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    (Color Fill, Color Border, Color Text) Colors()
    {
        if (!Enabled)
            return Kind == ButtonKind.Subtle
                ? (Color.Transparent, Color.Transparent, Theme.TextDisabled)
                : (Theme.Field, Theme.Border, Theme.TextDisabled);

        return Kind switch
        {
            ButtonKind.Primary => (
                _pressed ? Theme.AccentPressed : _hovered ? Theme.AccentHover : Theme.Accent,
                Color.Transparent,
                Theme.OnAccent),

            ButtonKind.Subtle => (
                _pressed ? Theme.FieldPressed : _hovered ? Theme.FieldHover : Color.Transparent,
                Color.Transparent,
                Theme.Text),

            _ => (
                _pressed ? Theme.FieldPressed : _hovered ? Theme.FieldHover : Theme.Field,
                Theme.Border,
                Theme.Text)
        };
    }
}
