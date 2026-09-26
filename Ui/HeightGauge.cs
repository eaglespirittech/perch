using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Perch.Ui;

/// <summary>
/// The hero readout: current height in large type, a small side-on drawing of the desk
/// that rises and falls with it, and a track showing where that sits in the desk's
/// travel. Reads at a glance from across the room, which is the point.
/// </summary>
public class HeightGauge : Card
{
    readonly System.Windows.Forms.Timer _tween = new() { Interval = 15 };
    double? _value;
    double? _shown; // what is drawn; glides toward _value so the number counts rather than jumps
    double? _target;
    string? _note;

    public HeightGauge()
    {
        Elevated = true;
        _tween.Tick += (_, _) =>
        {
            if (_value is not { } value || _shown is not { } shown)
            {
                _tween.Stop();
                return;
            }

            var next = shown + (value - shown) * 0.2;
            if (Math.Abs(value - next) < 0.02)
            {
                next = value;
                _tween.Stop();
            }

            _shown = next;
            Invalidate();
        };
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Minimum { get; set; } = DeskController.MinCm;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Maximum { get; set; } = DeskController.MaxCm;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double? Value
    {
        get => _value;
        set
        {
            _value = value;
            if (value is null || _shown is null)
            {
                _shown = value;
                _tween.Stop();
            }
            else
            {
                _tween.Start();
            }
            Invalidate();
        }
    }

    /// <summary>Where a move in progress is heading, drawn as a ghost desk and a ring on the track.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double? Target
    {
        get => _target;
        set { _target = value; Invalidate(); }
    }

    /// <summary>A short remark shown under the number while the desk is still, e.g. "At Preset 1".</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Note
    {
        get => _note;
        set { _note = value; Invalidate(); }
    }

    float Fraction(double cm) => (float)Math.Clamp((cm - Minimum) / (Maximum - Minimum), 0, 1);

    RectangleF Illustration => new(Width - this.Dp(22 + 108), this.Dp(16), this.Dp(108), Height - this.Dp(32));

    protected override void PaintSurface(Graphics g, GraphicsPath path, RectangleF bounds)
    {
        using (var wash = new LinearGradientBrush(bounds, Theme.AccentSoft, Theme.Surface, 25f))
            g.FillPath(wash, path);

        // A soft pool of accent light behind the desk drawing.
        var art = Illustration;
        var centre = new PointF(art.X + art.Width / 2, art.Y + art.Height * 0.55f);
        var reach = this.Dp(120);
        using var glowPath = new GraphicsPath();
        glowPath.AddEllipse(centre.X - reach, centre.Y - reach, reach * 2, reach * 2);
        using var glow = new PathGradientBrush(glowPath)
        {
            CenterColor = Color.FromArgb(Theme.IsDark ? 46 : 34, Theme.Accent),
            SurroundColors = new[] { Color.FromArgb(0, Theme.Accent) }
        };

        var clip = g.Clip;
        g.SetClip(path, CombineMode.Intersect);
        g.FillPath(glow, glowPath);
        g.Clip = clip;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var left = (int)this.Dp(22);
        var top = (int)this.Dp(18);

        TextRenderer.DrawText(g, "CURRENT HEIGHT", Theme.CaptionStrong, new Point(left, top),
            Theme.TextSecondary, TextFormatFlags.NoPadding);

        var number = _shown is { } cm ? cm.ToString("0.0", CultureInfo.InvariantCulture) : "––.–";
        var numberTop = top + (int)this.Dp(12);
        var numberSize = TextRenderer.MeasureText(g, number, Theme.Display, Size.Empty, TextFormatFlags.NoPadding);

        TextRenderer.DrawText(g, number, Theme.Display, new Point(left - (int)this.Dp(2), numberTop),
            _value is null ? Theme.TextDisabled : Theme.Text, TextFormatFlags.NoPadding);

        var unitTop = numberTop + (int)(Ascent(g, Theme.Display) - Ascent(g, Theme.DisplayUnit));
        TextRenderer.DrawText(g, "cm", Theme.DisplayUnit,
            new Point(left - (int)this.Dp(2) + numberSize.Width + (int)this.Dp(6), unitTop),
            Theme.TextSecondary, TextFormatFlags.NoPadding);

        DrawStatus(g, left, numberTop + numberSize.Height + (int)this.Dp(2));
        DrawTrack(g, left);
        DrawDesk(g);
    }

    static float Ascent(Graphics g, Font font)
    {
        var family = font.FontFamily;
        return font.GetHeight(g) * family.GetCellAscent(font.Style) / family.GetLineSpacing(font.Style);
    }

    void DrawStatus(Graphics g, int left, int y)
    {
        string glyph, text;
        Color colour;

        if (_value is null)
        {
            (glyph, text, colour) = ("", "Connect to see the height", Theme.TextSecondary);
        }
        else if (_target is { } target)
        {
            var up = target >= _value;
            (glyph, text, colour) = (up ? "" : "",
                $"Moving {(up ? "up" : "down")} to {target.ToString("0.0", CultureInfo.InvariantCulture)} cm", Theme.Accent);
        }
        else if (_note is not null)
        {
            (glyph, text, colour) = ("", _note, Theme.Success);
        }
        else
        {
            (glyph, text, colour) = ("", "At rest", Theme.TextSecondary);
        }

        var glyphSize = TextRenderer.MeasureText(g, glyph, Theme.IconSmall, Size.Empty, TextFormatFlags.NoPadding);
        var textSize = TextRenderer.MeasureText(g, text, Theme.Caption, Size.Empty, TextFormatFlags.NoPadding);
        var lineHeight = Math.Max(glyphSize.Height, textSize.Height);

        TextRenderer.DrawText(g, glyph, Theme.IconSmall,
            new Rectangle(left, y, glyphSize.Width, lineHeight), colour,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, text, Theme.Caption,
            new Rectangle(left + glyphSize.Width + (int)this.Dp(6), y, textSize.Width + 4, lineHeight), colour,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
    }

    void DrawTrack(Graphics g, int left)
    {
        var height = this.Dp(6);
        var right = Illustration.Left - this.Dp(20);
        var width = right - left;
        var y = Height - this.Dp(46);
        var track = new RectangleF(left, y, width, height);

        using (var path = Theme.RoundedRect(track, height / 2f))
        using (var brush = new SolidBrush(Theme.Track))
            g.FillPath(brush, path);

        if (_target is { } target)
        {
            var x = left + width * Fraction(target);
            var r = this.Dp(5);
            using var pen = new Pen(Theme.Accent, this.Dp(2f));
            g.DrawEllipse(pen, x - r, y + height / 2 - r, r * 2, r * 2);
        }

        if (_shown is { } cm)
        {
            var fraction = Fraction(cm);
            var filled = new RectangleF(left, y, Math.Max(width * fraction, height), height);
            using (var path = Theme.RoundedRect(filled, height / 2f))
            using (var brush = new LinearGradientBrush(RectangleF.Inflate(track, 1, 0), Theme.AccentBright, Theme.Accent, 0f))
                g.FillPath(brush, path);

            // The thumb: a lifted knob with a ring of accent.
            var x = left + width * fraction;
            var knob = this.Dp(8);
            var centreY = y + height / 2;
            using (var shadow = new SolidBrush(Color.FromArgb(Theme.IsDark ? 90 : 40, 0, 0, 0)))
                g.FillEllipse(shadow, x - knob, centreY - knob + this.Dp(1.5f), knob * 2, knob * 2);
            using (var face = new SolidBrush(Theme.IsDark ? Theme.Text : Theme.Surface))
                g.FillEllipse(face, x - knob, centreY - knob, knob * 2, knob * 2);
            using (var ring = new SolidBrush(Theme.Accent))
                g.FillEllipse(ring, x - knob * 0.5f, centreY - knob * 0.5f, knob, knob);
        }

        var labelY = (int)(y + height + this.Dp(8));
        TextRenderer.DrawText(g, $"{Minimum:0} cm", Theme.Caption, new Point(left, labelY),
            Theme.TextSecondary, TextFormatFlags.NoPadding);

        var maxText = $"{Maximum:0} cm";
        var maxWidth = TextRenderer.MeasureText(g, maxText, Theme.Caption, Size.Empty, TextFormatFlags.NoPadding).Width;
        TextRenderer.DrawText(g, maxText, Theme.Caption, new Point((int)right - maxWidth, labelY),
            Theme.TextSecondary, TextFormatFlags.NoPadding);
    }

    /// <summary>A side-on standing desk: telescoping leg, top, and a monitor on it.</summary>
    void DrawDesk(Graphics g)
    {
        var art = Illustration;
        var cx = art.X + art.Width / 2;
        var floor = art.Bottom - this.Dp(4);
        var connected = _value is not null;

        var metal = connected
            ? Theme.Mix(Theme.Surface, Theme.TextSecondary, Theme.IsDark ? 0.55 : 0.45)
            : Theme.Mix(Theme.Surface, Theme.TextDisabled, 0.5);
        var metalDark = Theme.Shift(metal, Theme.IsDark ? 0.12 : -0.12);

        using (var pen = new Pen(Theme.Mix(Theme.Surface, Theme.Track, 0.9), this.Dp(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(pen, art.Left + this.Dp(4), floor, art.Right - this.Dp(4), floor);

        // Travel runs from the lowest top position to the highest, leaving room above
        // for the monitor.
        var highest = art.Top + this.Dp(30);
        var lowest = floor - this.Dp(42);
        float TopAt(double cm) => lowest - (lowest - highest) * Fraction(cm);

        var deskY = TopAt(_shown ?? Minimum);
        var slabWidth = this.Dp(92);
        var slabHeight = this.Dp(7);
        var footHeight = this.Dp(5);

        if (_target is { } target)
        {
            var ghost = new RectangleF(cx - slabWidth / 2, TopAt(target), slabWidth, slabHeight);
            using var path = Theme.RoundedRect(ghost, this.Dp(3));
            using var pen = new Pen(Color.FromArgb(170, Theme.Accent), this.Dp(1.5f)) { DashStyle = DashStyle.Dash };
            g.DrawPath(pen, path);
        }

        // Foot, then the two-stage column.
        var foot = new RectangleF(cx - this.Dp(30), floor - footHeight, this.Dp(60), footHeight);
        using (var path = Theme.RoundedRect(foot, footHeight / 2))
        using (var brush = new SolidBrush(metalDark))
            g.FillPath(brush, path);

        var columnBottom = foot.Top;
        var columnTop = deskY + slabHeight;
        var split = columnBottom - (columnBottom - columnTop) * 0.5f;

        using (var brush = new SolidBrush(metal))
            g.FillRectangle(brush, cx - this.Dp(4), columnTop, this.Dp(8), split - columnTop + 1);
        using (var brush = new SolidBrush(metalDark))
            g.FillRectangle(brush, cx - this.Dp(6), split, this.Dp(12), columnBottom - split);

        // The top.
        var slab = new RectangleF(cx - slabWidth / 2, deskY, slabWidth, slabHeight);
        using (var path = Theme.RoundedRect(slab, this.Dp(3)))
        {
            using var brush = connected
                ? new LinearGradientBrush(slab, Theme.AccentBright, Theme.Accent, 0f)
                : new LinearGradientBrush(slab, metal, metal, 0f);
            g.FillPath(brush, path);
        }

        // A monitor on it.
        var screen = new RectangleF(cx - this.Dp(4), deskY - this.Dp(26), this.Dp(34), this.Dp(20));
        var body = Theme.IsDark ? Theme.Mix(Theme.Surface, Theme.Text, 0.3) : Theme.Mix(Theme.Surface, Theme.Text, 0.78);
        using (var brush = new SolidBrush(body))
        {
            g.FillRectangle(brush, screen.X + screen.Width / 2 - this.Dp(1.5f), screen.Bottom, this.Dp(3), this.Dp(5));
            g.FillRectangle(brush, screen.X + screen.Width / 2 - this.Dp(7), deskY - this.Dp(1.5f), this.Dp(14), this.Dp(1.5f));
            using var path = Theme.RoundedRect(screen, this.Dp(2.5f));
            g.FillPath(brush, path);
        }

        var display = RectangleF.Inflate(screen, -this.Dp(2), -this.Dp(2));
        using (var path = Theme.RoundedRect(display, this.Dp(1.5f)))
        using (var brush = new LinearGradientBrush(display,
                   connected ? Theme.Mix(Theme.Accent, Theme.Surface, 0.35) : Theme.Mix(body, Theme.Surface, 0.4),
                   connected ? Theme.Accent : body, 60f))
            g.FillPath(brush, path);

        // And a mug, for scale.
        var mug = new RectangleF(cx - this.Dp(36), deskY - this.Dp(9), this.Dp(8), this.Dp(9));
        using (var path = Theme.RoundedRect(mug, this.Dp(1.5f)))
        using (var brush = new SolidBrush(body))
            g.FillPath(brush, path);
        using (var pen = new Pen(body, this.Dp(1.5f)))
            g.DrawArc(pen, mug.Right - this.Dp(2), mug.Top + this.Dp(2), this.Dp(5), this.Dp(4.5f), -90, 180);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tween.Dispose();
        base.Dispose(disposing);
    }
}
