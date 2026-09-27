namespace Perch.App.Controls;

/// <summary>
/// The app icon's geometry on a 32-unit square: a rounded tile with a desk on it. Plain
/// numbers so that two renderers can share it: <see cref="AppIcon"/> draws it at runtime in
/// the live accent colour, and tools/IconGen draws the .ico and .icns files from it.
/// </summary>
public static class AppIconShape
{
    public const float Size = 32f;
    public const float TileRadius = 7f;

    /// <summary>The desk: its top, then its two legs. x, y, width, height.</summary>
    public static readonly (float X, float Y, float Width, float Height)[] Desk =
    {
        (6f, 12f, 20f, 3.5f),
        (8f, 15.5f, 2.5f, 9f),
        (21.5f, 15.5f, 2.5f, 9f)
    };

    /// <summary>The fixed colours baked into the icon files, so they do not depend on whose accent built them.</summary>
    public const uint BrandArgb = 0xFF005FB8;
    public const uint MarkArgb = 0xFFFFFFFF;
}
