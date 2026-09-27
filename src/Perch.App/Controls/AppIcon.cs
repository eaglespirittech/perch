using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Perch.App.Theme;

namespace Perch.App.Controls;

/// <summary>
/// Draws the app icon at runtime rather than shipping bitmaps, so it picks up the accent
/// colour and stays legible against a light or dark taskbar or menu bar.
/// </summary>
public static class AppIcon
{
    /// <summary>The coloured tile, for the window, the header and the Windows tray.</summary>
    public static Bitmap Draw(int pixels) => Render(pixels, template: false);

    /// <summary>
    /// A black silhouette with the desk cut out, for the macOS menu bar: macOS recolours
    /// "template" images itself to suit a light or dark bar.
    /// </summary>
    public static Bitmap DrawTemplate(int pixels) => Render(pixels, template: true);

    public static WindowIcon WindowIcon(int pixels = 64)
    {
        using var bitmap = Draw(pixels);
        return ToWindowIcon(bitmap);
    }

    public static WindowIcon ToWindowIcon(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    static Bitmap Render(int pixels, bool template)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(pixels, pixels), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var scale = pixels / AppIconShape.Size;
            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            {
                var tile = new RoundedRect(new Rect(0, 0, AppIconShape.Size, AppIconShape.Size), AppIconShape.TileRadius);

                if (template)
                {
                    // Tile and desk as one even-odd path, so the desk becomes a hole.
                    var shape = new PathGeometry { FillRule = FillRule.EvenOdd };
                    AddRoundedRect(shape, tile.Rect, AppIconShape.TileRadius);
                    foreach (var (x, y, w, h) in AppIconShape.Desk)
                        AddRoundedRect(shape, new Rect(x, y, w, h), 0);
                    context.DrawGeometry(Brushes.Black, null, shape);
                }
                else
                {
                    var palette = Palette.Current;
                    context.DrawRectangle(new SolidColorBrush(palette.Accent), null, tile);
                    var mark = new SolidColorBrush(palette.OnAccent);
                    foreach (var (x, y, w, h) in AppIconShape.Desk)
                        context.DrawRectangle(mark, null, new Rect(x, y, w, h));
                }
            }
        }

        return bitmap;
    }

    static void AddRoundedRect(PathGeometry path, Rect r, double radius)
    {
        var figure = new PathFigure { StartPoint = new Point(r.Left + radius, r.Top), IsClosed = true, IsFilled = true };
        var segments = new PathSegments();
        void Line(double x, double y) => segments.Add(new LineSegment { Point = new Point(x, y) });
        void Arc(double x, double y) => segments.Add(new ArcSegment
        {
            Point = new Point(x, y),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise
        });

        Line(r.Right - radius, r.Top);
        if (radius > 0) Arc(r.Right, r.Top + radius);
        Line(r.Right, r.Bottom - radius);
        if (radius > 0) Arc(r.Right - radius, r.Bottom);
        Line(r.Left + radius, r.Bottom);
        if (radius > 0) Arc(r.Left, r.Bottom - radius);
        Line(r.Left, r.Top + radius);
        if (radius > 0) Arc(r.Left + radius, r.Top);

        figure.Segments = segments;
        path.Figures ??= new PathFigures();
        path.Figures.Add(figure);
    }
}
