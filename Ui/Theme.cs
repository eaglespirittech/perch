using System.Runtime.InteropServices;
using Windows.UI.ViewManagement;

namespace Perch.Ui;

/// <summary>
/// Palette, type ramp and window trim, all following the system light/dark setting and
/// the user's accent colour. Controls read these statics while painting, so a theme
/// change is just a reload plus an Invalidate.
/// </summary>
public static class Theme
{
    public static bool IsDark { get; private set; }

    public static Color Accent { get; private set; }
    public static Color AccentHover { get; private set; }
    public static Color AccentPressed { get; private set; }
    public static Color OnAccent { get; private set; }

    public static Color Window { get; private set; }
    public static Color Surface { get; private set; }
    public static Color Border { get; private set; }
    public static Color Field { get; private set; }
    public static Color FieldHover { get; private set; }
    public static Color FieldPressed { get; private set; }
    public static Color Text { get; private set; }
    public static Color TextSecondary { get; private set; }
    public static Color TextDisabled { get; private set; }
    public static Color Track { get; private set; }

    public static Font Display { get; private set; } = null!;
    public static Font Title { get; private set; } = null!;
    public static Font Body { get; private set; } = null!;
    public static Font BodyStrong { get; private set; } = null!;
    public static Font Caption { get; private set; } = null!;
    public static Font Value { get; private set; } = null!;
    public static Font Icon { get; private set; } = null!;

    public const int CardRadius = 8;
    public const int ControlRadius = 6;

    static UISettings? _settings;

    /// <summary>Raised when Windows switches between light and dark, or changes accent.</summary>
    public static event Action? Changed;

    public static void Load()
    {
        var accent = Color.FromArgb(0, 95, 184);
        var dark = false;

        try
        {
            _settings ??= new UISettings();
            var background = _settings.GetColorValue(UIColorType.Background);
            dark = Luminance(background.R, background.G, background.B) < 0.5;

            // Escape hatch for anyone who wants to override the system setting.
            dark = Environment.GetEnvironmentVariable("PERCH_THEME")?.ToLowerInvariant() switch
            {
                "dark" => true,
                "light" => false,
                _ => dark
            };

            // On a dark background the plain accent is often too dim to read against.
            var raw = _settings.GetColorValue(dark ? UIColorType.AccentLight2 : UIColorType.Accent);
            accent = Color.FromArgb(raw.R, raw.G, raw.B);
        }
        catch
        {
            // No WinRT (or a locked-down session): the defaults above are fine.
        }

        IsDark = dark;
        Accent = accent;
        AccentHover = Shift(accent, dark ? -0.06 : 0.08);
        AccentPressed = Shift(accent, dark ? -0.12 : 0.16);
        OnAccent = Luminance(accent.R, accent.G, accent.B) > 0.6 ? Color.FromArgb(23, 23, 23) : Color.White;

        if (dark)
        {
            Window = Color.FromArgb(32, 32, 32);
            Surface = Color.FromArgb(43, 43, 43);
            Border = Color.FromArgb(58, 58, 58);
            Field = Color.FromArgb(55, 55, 55);
            FieldHover = Color.FromArgb(63, 63, 63);
            FieldPressed = Color.FromArgb(50, 50, 50);
            Text = Color.FromArgb(245, 245, 245);
            TextSecondary = Color.FromArgb(170, 170, 170);
            TextDisabled = Color.FromArgb(110, 110, 110);
            Track = Color.FromArgb(70, 70, 70);
        }
        else
        {
            Window = Color.FromArgb(243, 243, 243);
            Surface = Color.White;
            Border = Color.FromArgb(229, 229, 229);
            Field = Color.FromArgb(249, 249, 249);
            FieldHover = Color.FromArgb(242, 242, 242);
            FieldPressed = Color.FromArgb(235, 235, 235);
            Text = Color.FromArgb(26, 26, 26);
            TextSecondary = Color.FromArgb(96, 96, 96);
            TextDisabled = Color.FromArgb(160, 160, 160);
            Track = Color.FromArgb(224, 224, 224);
        }

        var display = PickFamily("Segoe UI Variable Display", "Segoe UI");
        var text = PickFamily("Segoe UI Variable Text", "Segoe UI");

        Display = new Font(display, 40F, FontStyle.Regular, GraphicsUnit.Point);
        Title = new Font(display, 14F, FontStyle.Regular, GraphicsUnit.Point);
        Body = new Font(text, 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        BodyStrong = new Font(text, 9.5F, FontStyle.Bold, GraphicsUnit.Point);
        Caption = new Font(text, 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        Value = new Font(text, 12.5F, FontStyle.Regular, GraphicsUnit.Point);
        Icon = new Font(PickFamily("Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol"), 11F);
    }

    /// <summary>Starts listening for system theme changes. Call once, after the first Load.</summary>
    public static void Watch()
    {
        if (_settings is null) return;
        _settings.ColorValuesChanged += (_, _) =>
        {
            Load();
            Changed?.Invoke();
        };
    }

    /// <summary>Dark title bar and rounded corners, so the frame matches the content.</summary>
    public static void ApplyWindowTrim(Form form)
    {
        if (!form.IsHandleCreated) return;

        var dark = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        var round = DwmWindowCornerRound;
        _ = DwmSetWindowAttribute(form.Handle, DwmwaWindowCornerPreference, ref round, sizeof(int));
    }

    const int DwmwaUseImmersiveDarkMode = 20;
    const int DwmwaWindowCornerPreference = 33;
    const int DwmWindowCornerRound = 2;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    static string PickFamily(params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                using var family = new FontFamily(name);
                return name;
            }
            catch (ArgumentException)
            {
                // Not installed; try the next one.
            }
        }
        return "Segoe UI";
    }

    static double Luminance(byte r, byte g, byte b) => (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;

    /// <summary>Positive lightens toward white, negative darkens toward black.</summary>
    public static Color Shift(Color color, double amount)
    {
        static int Mix(int channel, double amount) => amount >= 0
            ? (int)(channel + (255 - channel) * amount)
            : (int)(channel * (1 + amount));

        return Color.FromArgb(color.A, Mix(color.R, amount), Mix(color.G, amount), Mix(color.B, amount));
    }

    /// <summary>A rounded rectangle path, used by every surface we draw.</summary>
    public static System.Drawing.Drawing2D.GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;

        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
