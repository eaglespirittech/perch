using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Perch.Ui;

/// <summary>
/// Draws the app icon rather than shipping a .ico, so it picks up the accent colour and
/// stays legible against both a light and a dark taskbar.
/// </summary>
public static class AppIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Colours default to the live theme. The icon baked into the exe passes fixed ones
    /// instead, so the file icon does not depend on whose accent colour built it.
    /// </summary>
    public static Bitmap Draw(int size, Color? accent = null, Color? mark = null)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = size / 32f;

        using (var path = Theme.RoundedRect(new RectangleF(0, 0, size, size), 7 * scale))
        using (var brush = new SolidBrush(accent ?? Theme.Accent))
            g.FillPath(brush, path);

        // A desk: top surface and two legs.
        using var pen = new SolidBrush(mark ?? Theme.OnAccent);
        g.FillRectangle(pen, 6 * scale, 12 * scale, 20 * scale, 3.5f * scale);
        g.FillRectangle(pen, 8 * scale, 15.5f * scale, 2.5f * scale, 9 * scale);
        g.FillRectangle(pen, 21.5f * scale, 15.5f * scale, 2.5f * scale, 9 * scale);

        return bitmap;
    }

    public static Icon Create(int size = 32)
    {
        using var bitmap = Draw(size);
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
