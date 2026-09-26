using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Perch.Ui;

public enum ButtonKind
{
    /// <summary>Accent filled. One per view, on the thing you most likely want.</summary>
    Primary,

    /// <summary>Neutral fill with a hairline border.</summary>
    Standard,

    /// <summary>No fill until hovered. For low-frequency actions.</summary>
    Subtle,

    /// <summary>A soft red tint, for actions that interrupt something.</summary>
    Danger
}

/// <summary>
/// A flat, rounded button drawn by hand. Derives from Button so focus, keyboard
/// activation and accessibility keep working; only the painting is ours.
/// </summary>
public class PerchButton : Button
{
    readonly System.Windows.Forms.Timer _fade = new() { Interval = 15 };
    float _hover; // 0 = resting, 1 = fully hovered; eased so the fill fades in
    bool _hovered;
    bool _pressed;

    public PerchButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;

        _fade.Tick += (_, _) =>
        {
            var target = _hovered ? 1f : 0f;
            _hover += (target - _hover) * 0.3f;
            if (Math.Abs(target - _hover) < 0.02f)
            {
                _hover = target;
                _fade.Stop();
            }
            Invalidate();
        };
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind
    {
        get => _kind;
        set { _kind = value; Invalidate(); }
    }
    ButtonKind _kind = ButtonKind.Standard;

    /// <summary>Corner radius in 96-DPI pixels.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = Theme.ControlRadius;

    /// <summary>Draw the text in the icon font instead of the body font.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsGlyph { get; set; }

    /// <summary>An icon-font character drawn ahead of the text.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Glyph { get; set; }

    /// <summary>Overrides the text colour, e.g. for quiet secondary actions.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? TextColor { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Font? TextFont { get; set; }

    /// <summary>Sizes the button to its text, which is what keeps labels from clipping.</summary>
    public void SizeToText(int horizontalPadding = 20, int height = 34, int minimumWidth = 0)
    {
        var font = IsGlyph ? Theme.Icon : TextFont ?? Theme.Body;
        var text = TextRenderer.MeasureText(Text, font).Width;
        Size = new Size(Math.Max(text + horizontalPadding * 2, minimumWidth), height);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        _fade.Start();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        _fade.Start();
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

    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled) { _hovered = false; _hover = 0; }
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Backdrop(this));

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Theme.RoundedRect(bounds, this.Dp(Radius));

        var (fill, border, text) = Colors();

        if (fill.A > 0)
        {
            if (Kind == ButtonKind.Primary && Enabled && !_pressed)
            {
                // A faint top-down sheen gives the accent a little depth.
                using var brush = new LinearGradientBrush(bounds, Theme.Shift(fill, 0.08), fill, LinearGradientMode.Vertical);
                g.FillPath(brush, path);
            }
            else
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }
        }

        if (border.A > 0)
        {
            using var pen = new Pen(border);
            g.DrawPath(pen, path);
        }

        if (Focused && Enabled && ShowFocusCues)
        {
            var focus = RectangleF.Inflate(bounds, -1f, -1f);
            using var focusPath = Theme.RoundedRect(focus, Math.Max(this.Dp(Radius) - 1, 1));
            using var focusPen = new Pen(Kind == ButtonKind.Primary ? Theme.OnAccent : Theme.Text, this.Dp(2f));
            g.DrawPath(focusPen, focusPath);
        }

        PaintContent(g, text);
    }

    /// <summary>Draws what sits on the button. Overridden by buttons with richer content.</summary>
    protected virtual void PaintContent(Graphics g, Color text)
    {
        const TextFormatFlags centred = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

        if (IsGlyph)
        {
            TextRenderer.DrawText(g, Text, Theme.Icon, ClientRectangle, text, centred | TextFormatFlags.HorizontalCenter);
            return;
        }

        var font = TextFont ?? Theme.Body;
        if (string.IsNullOrEmpty(Glyph))
        {
            TextRenderer.DrawText(g, Text, font, ClientRectangle, text, centred | TextFormatFlags.HorizontalCenter);
            return;
        }

        var icon = font.Size < Theme.Body.Size ? Theme.IconSmall : Theme.Icon;
        var gap = (int)this.Dp(8);
        var glyphSize = TextRenderer.MeasureText(g, Glyph, icon, Size.Empty, TextFormatFlags.NoPadding);
        var textSize = TextRenderer.MeasureText(g, Text, font, Size.Empty, TextFormatFlags.NoPadding);
        var left = (Width - glyphSize.Width - gap - textSize.Width) / 2;

        TextRenderer.DrawText(g, Glyph, icon, new Rectangle(left, 0, glyphSize.Width, Height), text, centred);
        TextRenderer.DrawText(g, Text, font,
            new Rectangle(left + glyphSize.Width + gap, 0, Width - left - glyphSize.Width - gap, Height), text, centred);
    }

    protected (Color Fill, Color Border, Color Text) Colors()
    {
        if (!Enabled)
            return Kind == ButtonKind.Subtle
                ? (Color.Transparent, Color.Transparent, Theme.TextDisabled)
                : (Theme.Field, Theme.Border, Theme.TextDisabled);

        var t = _hover;
        return Kind switch
        {
            ButtonKind.Primary => (
                _pressed ? Theme.AccentPressed : Theme.Mix(Theme.Accent, Theme.AccentHover, t),
                Color.Transparent,
                Theme.OnAccent),

            ButtonKind.Subtle => (
                _pressed ? Theme.FieldPressed : Color.FromArgb((int)(255 * t), Theme.FieldHover),
                Color.Transparent,
                TextColor ?? Theme.Text),

            ButtonKind.Danger => (
                _pressed ? Theme.Mix(Theme.DangerSoftHover, Theme.Danger, 0.15) : Theme.Mix(Theme.DangerSoft, Theme.DangerSoftHover, t),
                Theme.Mix(Theme.DangerSoft, Theme.Danger, 0.25),
                Theme.Danger),

            _ => (
                _pressed ? Theme.FieldPressed : Theme.Mix(Theme.Field, Theme.FieldHover, t),
                Theme.Border,
                TextColor ?? Theme.Text)
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _fade.Dispose();
        base.Dispose(disposing);
    }
}
