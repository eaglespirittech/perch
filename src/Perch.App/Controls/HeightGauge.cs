using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Perch.App.Theme;
using Perch.Desk;

namespace Perch.App.Controls;

/// <summary>
/// The hero readout: current height in large type, a small side-on drawing of the desk
/// that rises and falls with it, and a track showing where that sits in the desk's
/// travel. Reads at a glance from across the room, which is the point.
/// </summary>
public sealed class HeightGauge : Control
{
    public static readonly StyledProperty<double?> ValueProperty =
        AvaloniaProperty.Register<HeightGauge, double?>(nameof(Value));

    public static readonly StyledProperty<double?> TargetProperty =
        AvaloniaProperty.Register<HeightGauge, double?>(nameof(Target));

    public static readonly StyledProperty<string?> NoteProperty =
        AvaloniaProperty.Register<HeightGauge, string?>(nameof(Note));

    const double Minimum = DeskController.MinCm;
    const double Maximum = DeskController.MaxCm;

    readonly DispatcherTimer _tween = new() { Interval = TimeSpan.FromMilliseconds(15) };
    double? _shown; // what is drawn; glides toward Value so the number counts rather than jumps

    static HeightGauge()
    {
        AffectsRender<HeightGauge>(TargetProperty, NoteProperty);
    }

    public HeightGauge()
    {
        _tween.Tick += (_, _) =>
        {
            if (Value is not { } value || _shown is not { } shown)
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
            InvalidateVisual();
        };

        Palette.Changed += InvalidateVisual;
    }

    public double? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Where a move in progress is heading, drawn as a ghost desk and a ring on the track.</summary>
    public double? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>A short remark shown under the number while the desk is still, e.g. "At Preset 1".</summary>
    public string? Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ValueProperty) return;

        if (Value is null || _shown is null)
        {
            _shown = Value;
            _tween.Stop();
        }
        else
        {
            _tween.Start();
        }

        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _tween.Stop();
    }

    static double Fraction(double cm) => Math.Clamp((cm - Minimum) / (Maximum - Minimum), 0, 1);

    Rect Illustration => new(Bounds.Width - 22 - 108, 16, 108, Bounds.Height - 32);

    static Typeface Face(FontWeight weight) => new(FontFamily.Default, FontStyle.Normal, weight);

    static FormattedText Text(string text, double size, FontWeight weight, Color color) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face(weight), size, new SolidColorBrush(color));

    public override void Render(DrawingContext g)
    {
        var p = Palette.Current;
        var bounds = new Rect(Bounds.Size);

        // A wash of accent from the top left, and a soft pool of light behind the desk.
        g.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.45, RelativeUnit.Relative),
            GradientStops = { new GradientStop(p.AccentSoft, 0), new GradientStop(p.Surface, 1) }
        }, null, bounds);

        var art = Illustration;
        var centre = new Point(art.X + art.Width / 2, art.Y + art.Height * 0.55);
        const double reach = 120;
        g.DrawEllipse(new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Palette.WithAlpha(p.Accent, (byte)(p.IsDark ? 46 : 34)), 0),
                new GradientStop(Palette.WithAlpha(p.Accent, 0), 1)
            }
        }, null, centre, reach, reach);

        const double left = 22;
        const double top = 18;

        g.DrawText(Text("CURRENT HEIGHT", 11, FontWeight.SemiBold, p.TextSecondary), new Point(left, top));

        var number = Text(
            _shown is { } cm ? cm.ToString("0.0", CultureInfo.InvariantCulture) : "––.–",
            52, FontWeight.SemiBold, Value is null ? p.TextDisabled : p.Text);
        var numberTop = top + 8;
        g.DrawText(number, new Point(left - 2, numberTop));

        var unit = Text("cm", 24, FontWeight.Light, p.TextSecondary);
        g.DrawText(unit, new Point(left - 2 + number.Width + 6, numberTop + number.Baseline - unit.Baseline));

        DrawStatus(g, p, left, numberTop + number.Height);
        DrawTrack(g, p, left);
        DrawDesk(g, p);
    }

    void DrawStatus(DrawingContext g, Palette p, double left, double y)
    {
        string glyph, text;
        Color colour;

        if (Value is not { } value)
            (glyph, text, colour) = ("link", "Connect to see the height", p.TextSecondary);
        else if (Target is { } target)
        {
            var up = target >= value;
            (glyph, text, colour) = (up ? "up" : "down",
                $"Moving {(up ? "up" : "down")} to {target.ToString("0.0", CultureInfo.InvariantCulture)} cm", p.Accent);
        }
        else if (Note is { } note)
            (glyph, text, colour) = ("check", note, p.Success);
        else
            (glyph, text, colour) = ("pause", "At rest", p.TextSecondary);

        var label = Text(text, 12, FontWeight.Normal, colour);
        const double iconSize = 13;
        DrawIcon(g, glyph, new Rect(left, y + (label.Height - iconSize) / 2, iconSize, iconSize), colour);
        g.DrawText(label, new Point(left + iconSize + 6, y));
    }

    void DrawTrack(DrawingContext g, Palette p, double left)
    {
        const double height = 6;
        var right = Illustration.Left - 20;
        var width = right - left;
        var y = Bounds.Height - 46;
        var track = new Rect(left, y, width, height);

        g.DrawRectangle(new SolidColorBrush(p.Track), null, new RoundedRect(track, height / 2));

        if (Target is { } target)
        {
            var x = left + width * Fraction(target);
            g.DrawEllipse(null, new Pen(new SolidColorBrush(p.Accent), 2), new Point(x, y + height / 2), 5, 5);
        }

        if (_shown is { } cm)
        {
            var fraction = Fraction(cm);
            var filled = new Rect(left, y, Math.Max(width * fraction, height), height);
            g.DrawRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(p.AccentBright, 0), new GradientStop(p.Accent, 1) }
            }, null, new RoundedRect(filled, height / 2));

            // The thumb: a lifted knob with a ring of accent.
            var x = left + width * fraction;
            var centreY = y + height / 2;
            const double knob = 8;
            g.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(p.IsDark ? 90 : 40), 0, 0, 0)), null,
                new Point(x, centreY + 1.5), knob, knob);
            g.DrawEllipse(new SolidColorBrush(p.IsDark ? p.Text : p.Surface), null, new Point(x, centreY), knob, knob);
            g.DrawEllipse(new SolidColorBrush(p.Accent), null, new Point(x, centreY), knob / 2, knob / 2);
        }

        var labelY = y + height + 8;
        g.DrawText(Text($"{Minimum:0} cm", 11.5, FontWeight.Normal, p.TextSecondary), new Point(left, labelY));
        var max = Text($"{Maximum:0} cm", 11.5, FontWeight.Normal, p.TextSecondary);
        g.DrawText(max, new Point(right - max.Width, labelY));
    }

    /// <summary>A side-on standing desk: telescoping leg, top, and a monitor on it.</summary>
    void DrawDesk(DrawingContext g, Palette p)
    {
        var art = Illustration;
        var cx = art.X + art.Width / 2;
        var floor = art.Bottom - 4;
        var connected = Value is not null;

        var metal = connected
            ? Palette.Mix(p.Surface, p.TextSecondary, p.IsDark ? 0.55 : 0.45)
            : Palette.Mix(p.Surface, p.TextDisabled, 0.5);
        var metalDark = Palette.Shift(metal, p.IsDark ? 0.12 : -0.12);

        g.DrawLine(new Pen(new SolidColorBrush(Palette.Mix(p.Surface, p.Track, 0.9)), 2, lineCap: PenLineCap.Round),
            new Point(art.Left + 4, floor), new Point(art.Right - 4, floor));

        // Travel runs from the lowest top position to the highest, leaving room above
        // for the monitor.
        var highest = art.Top + 30;
        var lowest = floor - 42;
        double TopAt(double cm) => lowest - (lowest - highest) * Fraction(cm);

        var deskY = TopAt(_shown ?? Minimum);
        const double slabWidth = 92;
        const double slabHeight = 7;
        const double footHeight = 5;

        if (Target is { } target)
        {
            var ghost = new Rect(cx - slabWidth / 2, TopAt(target), slabWidth, slabHeight);
            g.DrawRectangle(null,
                new Pen(new SolidColorBrush(Palette.WithAlpha(p.Accent, 170)), 1.5, new DashStyle(new double[] { 3, 2 }, 0)),
                new RoundedRect(ghost, 3));
        }

        // Foot, then the two-stage column.
        var foot = new Rect(cx - 30, floor - footHeight, 60, footHeight);
        g.DrawRectangle(new SolidColorBrush(metalDark), null, new RoundedRect(foot, footHeight / 2));

        var columnBottom = foot.Top;
        var columnTop = deskY + slabHeight;
        var split = columnBottom - (columnBottom - columnTop) * 0.5;
        g.DrawRectangle(new SolidColorBrush(metal), null, new Rect(cx - 4, columnTop, 8, split - columnTop + 1));
        g.DrawRectangle(new SolidColorBrush(metalDark), null, new Rect(cx - 6, split, 12, columnBottom - split));

        // The top.
        var slab = new Rect(cx - slabWidth / 2, deskY, slabWidth, slabHeight);
        IBrush slabBrush = connected
            ? new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(p.AccentBright, 0), new GradientStop(p.Accent, 1) }
            }
            : new SolidColorBrush(metal);
        g.DrawRectangle(slabBrush, null, new RoundedRect(slab, 3));

        // A monitor on it.
        var screen = new Rect(cx - 4, deskY - 26, 34, 20);
        var body = new SolidColorBrush(p.IsDark ? Palette.Mix(p.Surface, p.Text, 0.3) : Palette.Mix(p.Surface, p.Text, 0.78));
        g.DrawRectangle(body, null, new Rect(screen.X + screen.Width / 2 - 1.5, screen.Bottom, 3, 5));
        g.DrawRectangle(body, null, new Rect(screen.X + screen.Width / 2 - 7, deskY - 1.5, 14, 1.5));
        g.DrawRectangle(body, null, new RoundedRect(screen, 2.5));

        var display = screen.Deflate(2);
        g.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(connected ? Palette.Mix(p.Accent, p.Surface, 0.35) : Palette.Mix(body.Color, p.Surface, 0.4), 0),
                new GradientStop(connected ? p.Accent : body.Color, 1)
            }
        }, null, new RoundedRect(display, 1.5));

        // And a mug, for scale.
        var mug = new Rect(cx - 36, deskY - 9, 8, 9);
        g.DrawRectangle(body, null, new RoundedRect(mug, 1.5));
        var handle = new StreamGeometry();
        using (var ctx = handle.Open())
        {
            ctx.BeginFigure(new Point(mug.Right - 0.5, mug.Top + 2), false);
            ctx.ArcTo(new Point(mug.Right - 0.5, mug.Top + 6.5), new Size(2.5, 2.25), 0, false, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        g.DrawGeometry(null, new Pen(body, 1.5), handle);
    }

    static void DrawIcon(DrawingContext g, string kind, Rect rect, Color colour)
    {
        if (!Icons.TryGet(kind, out var icon)) return;
        var brush = new SolidColorBrush(colour);
        var scale = rect.Width / 24.0;
        using (g.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(rect.X, rect.Y)))
        {
            if (icon.Stroke is { } stroke)
                g.DrawGeometry(null, new Pen(brush, 2.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), stroke);
            if (icon.Fill is { } fill)
                g.DrawGeometry(brush, null, fill);
        }
    }
}
