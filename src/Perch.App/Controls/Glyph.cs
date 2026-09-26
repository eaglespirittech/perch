using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Perch.App.Controls;

/// <summary>
/// A line icon, drawn from a path on a 24-unit grid. Vector paths rather than an icon
/// font, because Segoe Fluent Icons only exists on Windows. Takes its colour from the
/// surrounding text, so an icon in a button follows the button's state.
/// </summary>
public sealed class Glyph : Control
{
    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<Glyph, string?>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Glyph>();

    static Glyph()
    {
        AffectsRender<Glyph>(KindProperty, ForegroundProperty);
        AffectsMeasure<Glyph>(KindProperty);
    }

    /// <summary>One of the names in <see cref="Icons"/>, e.g. "plus" or "clock".</summary>
    public string? Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsNaN(Width) ? 16 : Width, double.IsNaN(Height) ? 16 : Height);

    public override void Render(DrawingContext context)
    {
        if (Kind is null || !Icons.TryGet(Kind, out var icon) || Foreground is not { } brush) return;

        var size = Math.Min(Bounds.Width, Bounds.Height);
        var scale = size / 24.0;
        var offset = new Vector((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset)))
        {
            if (icon.Stroke is { } stroke)
                context.DrawGeometry(null, new Pen(brush, 1.9, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), stroke);
            if (icon.Fill is { } fill)
                context.DrawGeometry(brush, null, fill);
        }
    }
}

/// <summary>The app's icon set: a stroked outline and/or a filled shape per name.</summary>
public static class Icons
{
    public sealed record Icon(Geometry? Stroke, Geometry? Fill);

    static readonly Dictionary<string, Icon> All = new()
    {
        ["more"] = Filled("M5,10.5 a1.5,1.5 0 1,0 0.01,0 Z M12,10.5 a1.5,1.5 0 1,0 0.01,0 Z M19,10.5 a1.5,1.5 0 1,0 0.01,0 Z"),
        ["minus"] = Stroked("M6,12 H18"),
        ["plus"] = Stroked("M12,6 V18 M6,12 H18"),
        ["forward"] = Stroked("M5,12 H19 M13,6 L19,12 L13,18"),
        ["stop"] = Filled("M8,7 H16 A1,1 0 0 1 17,8 V16 A1,1 0 0 1 16,17 H8 A1,1 0 0 1 7,16 V8 A1,1 0 0 1 8,7 Z"),
        ["save"] = Stroked("M12,4 V14 M7.5,9.5 L12,14 L16.5,9.5 M5,19 H19"),
        ["clock"] = Stroked("M12,3.5 A8.5,8.5 0 1 1 11.99,3.5 Z M12,7.5 V12 L15,14"),
        ["edit"] = Stroked("M4.5,19.5 L5.5,15.5 L15.5,5.5 A2.1,2.1 0 0 1 18.5,8.5 L8.5,18.5 Z M13.5,7.5 L16.5,10.5"),
        ["copy"] = Stroked("M9,9 H18 A1,1 0 0 1 19,10 V19 A1,1 0 0 1 18,20 H9 A1,1 0 0 1 8,19 V10 A1,1 0 0 1 9,9 Z M5,15 V5 A1,1 0 0 1 6,4 H15"),
        ["chevron"] = Stroked("M9.5,6 L15.5,12 L9.5,18"),
        ["up"] = Stroked("M12,19 V5 M6,11 L12,5 L18,11"),
        ["down"] = Stroked("M12,5 V19 M6,13 L12,19 L18,13"),
        ["check"] = Stroked("M5,12.5 L10,17.5 L19,7"),
        ["info"] = new(Parse("M12,3.5 A8.5,8.5 0 1 1 11.99,3.5 Z M12,11 V16.5"),
                       Parse("M12,6.8 a1.2,1.2 0 1,0 0.01,0 Z")),
        ["link"] = Stroked("M9.5,14.5 L14.5,9.5 M11,7 L12.5,5.5 A3.5,3.5 0 0 1 18.5,11.5 L17,13 M13,17 L11.5,18.5 A3.5,3.5 0 0 1 5.5,12.5 L7,11"),
        ["pause"] = Stroked("M9,6 V18 M15,6 V18")
    };

    public static bool TryGet(string name, out Icon icon) => All.TryGetValue(name, out icon!);

    static Icon Stroked(string data) => new(Parse(data), null);
    static Icon Filled(string data) => new(null, Parse(data));
    static Geometry Parse(string data) => Geometry.Parse(data);
}
