using System.Drawing.Drawing2D;
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
    public static Color AccentBright { get; private set; }
    public static Color AccentSoft { get; private set; }
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

    public static Color Success { get; private set; }
    public static Color Caution { get; private set; }
    public static Color Danger { get; private set; }
    public static Color DangerSoft { get; private set; }
    public static Color DangerSoftHover { get; private set; }

    public static Font Display { get; private set; } = null!;
    public static Font DisplayUnit { get; private set; } = null!;
    public static Font Title { get; private set; } = null!;
    public static Font Heading { get; private set; } = null!;
    public static Font Body { get; private set; } = null!;
    public static Font BodyStrong { get; private set; } = null!;
    public static Font Caption { get; private set; } = null!;
    public static Font CaptionStrong { get; private set; } = null!;
    public static Font Value { get; private set; } = null!;
    public static Font ValueLarge { get; private set; } = null!;
    public static Font Icon { get; private set; } = null!;
    public static Font IconSmall { get; private set; } = null!;

    public const int CardRadius = 12;
    public const int ControlRadius = 8;

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
        AccentBright = Shift(accent, dark ? 0.25 : 0.35);
        OnAccent = Luminance(accent.R, accent.G, accent.B) > 0.6 ? Color.FromArgb(23, 23, 23) : Color.White;

        // Neutrals carry a trace of the accent, which is what keeps the window from
        // reading as plain grey.
        if (dark)
        {
            Window = Mix(Color.FromArgb(28, 28, 30), accent, 0.04);
            Surface = Mix(Color.FromArgb(41, 41, 44), accent, 0.04);
            Border = Mix(Color.FromArgb(56, 56, 60), accent, 0.04);
            Field = Mix(Color.FromArgb(52, 52, 56), accent, 0.04);
            FieldHover = Mix(Color.FromArgb(62, 62, 66), accent, 0.05);
            FieldPressed = Mix(Color.FromArgb(47, 47, 51), accent, 0.05);
            Text = Color.FromArgb(245, 245, 247);
            TextSecondary = Color.FromArgb(166, 166, 172);
            TextDisabled = Color.FromArgb(104, 104, 110);
            Track = Mix(Color.FromArgb(66, 66, 70), accent, 0.06);

            Success = Color.FromArgb(108, 203, 95);
            Caution = Color.FromArgb(252, 200, 60);
            Danger = Color.FromArgb(255, 153, 164);
        }
        else
        {
            Window = Mix(Color.FromArgb(242, 243, 246), accent, 0.035);
            Surface = Color.White;
            Border = Mix(Color.FromArgb(226, 227, 232), accent, 0.04);
            Field = Mix(Color.FromArgb(246, 247, 249), accent, 0.03);
            FieldHover = Mix(Color.FromArgb(238, 239, 243), accent, 0.05);
            FieldPressed = Mix(Color.FromArgb(230, 231, 236), accent, 0.06);
            Text = Color.FromArgb(24, 24, 28);
            TextSecondary = Color.FromArgb(98, 98, 108);
            TextDisabled = Color.FromArgb(162, 162, 170);
            Track = Mix(Color.FromArgb(224, 225, 230), accent, 0.08);

            Success = Color.FromArgb(16, 124, 16);
            Caution = Color.FromArgb(196, 120, 0);
            Danger = Color.FromArgb(196, 43, 28);
        }

        AccentSoft = Mix(Surface, accent, dark ? 0.16 : 0.10);
        DangerSoft = Mix(Surface, Danger, dark ? 0.14 : 0.08);
        DangerSoftHover = Mix(Surface, Danger, dark ? 0.22 : 0.14);

        var display = PickFamily("Segoe UI Variable Display", "Segoe UI");
        var displayStrong = PickFamily("Segoe UI Variable Display Semib", "Segoe UI Semibold");
        var displayLight = PickFamily("Segoe UI Variable Display Light", "Segoe UI Light");
        var text = PickFamily("Segoe UI Variable Text", "Segoe UI");
        var textStrong = PickFamily("Segoe UI Variable Text Semibold", "Segoe UI Semibold");

        Display = new Font(displayStrong, 40F, FontStyle.Regular, GraphicsUnit.Point);
        DisplayUnit = new Font(displayLight, 18F, FontStyle.Regular, GraphicsUnit.Point);
        Title = new Font(displayStrong, 13F, FontStyle.Regular, GraphicsUnit.Point);
        Heading = new Font(textStrong, 10F, FontStyle.Regular, GraphicsUnit.Point);
        Body = new Font(text, 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        BodyStrong = new Font(textStrong, 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        Caption = new Font(text, 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        CaptionStrong = new Font(textStrong, 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        Value = new Font(displayStrong, 12.5F, FontStyle.Regular, GraphicsUnit.Point);
        ValueLarge = new Font(displayStrong, 15F, FontStyle.Regular, GraphicsUnit.Point);

        var icons = PickFamily("Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol");
        Icon = new Font(icons, 11F);
        IconSmall = new Font(icons, 9F);
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

    /// <summary>
    /// Dark title bar, rounded corners, and a caption painted the window colour so the
    /// frame and the content read as one surface. The colour attributes are Windows 11
    /// only; on Windows 10 they fail quietly and the standard frame stays.
    /// </summary>
    public static void ApplyWindowTrim(Form form)
    {
        if (!form.IsHandleCreated) return;

        var dark = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        var round = DwmWindowCornerRound;
        _ = DwmSetWindowAttribute(form.Handle, DwmwaWindowCornerPreference, ref round, sizeof(int));

        var caption = ColorRef(Window);
        _ = DwmSetWindowAttribute(form.Handle, DwmwaCaptionColor, ref caption, sizeof(int));

        var text = ColorRef(TextSecondary);
        _ = DwmSetWindowAttribute(form.Handle, DwmwaTextColor, ref text, sizeof(int));
    }

    /// <summary>Rounds a pop-up menu's corners the way Windows 11 rounds its own.</summary>
    public static void ApplyPopupTrim(Control popup)
    {
        if (!popup.IsHandleCreated) return;

        var round = DwmWindowCornerRoundSmall;
        _ = DwmSetWindowAttribute(popup.Handle, DwmwaWindowCornerPreference, ref round, sizeof(int));

        var border = ColorRef(Border);
        _ = DwmSetWindowAttribute(popup.Handle, DwmwaBorderColor, ref border, sizeof(int));
    }

    const int DwmwaUseImmersiveDarkMode = 20;
    const int DwmwaWindowCornerPreference = 33;
    const int DwmwaBorderColor = 34;
    const int DwmwaCaptionColor = 35;
    const int DwmwaTextColor = 36;
    const int DwmWindowCornerRound = 2;
    const int DwmWindowCornerRoundSmall = 3;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    static int ColorRef(Color color) => color.R | color.G << 8 | color.B << 16;

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

    /// <summary>Blends <paramref name="from"/> toward <paramref name="to"/>; 0 is all from, 1 all to.</summary>
    public static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * amount),
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    /// <summary>Converts a length in 96-DPI pixels to this control's pixels.</summary>
    public static float Dp(this Control control, float value) => value * control.DeviceDpi / 96f;

    /// <summary>The colour a child control should clear to so its corners disappear.</summary>
    public static Color Backdrop(Control control) => control.Parent switch
    {
        Card card => card.SurfaceColor,
        { } parent => parent.BackColor,
        null => Surface
    };

    /// <summary>A rounded rectangle path, used by every surface we draw.</summary>
    public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        radius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
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

    /// <summary>
    /// Soft drop shadows under a container's elevated cards. Child controls cannot paint
    /// outside their own bounds, so the window paints them from underneath instead.
    /// </summary>
    public static void PaintShadows(Graphics g, Control container)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var spread = container.Dp(8);
        var drop = container.Dp(2);
        var steps = (int)Math.Ceiling(spread);
        var peak = IsDark ? 34 : 14;

        foreach (var card in container.Controls.OfType<Card>())
        {
            if (!card.Visible || !card.Elevated) continue;

            var radius = container.Dp(card.Radius);
            for (var i = steps; i >= 1; i--)
            {
                var t = i / (float)steps;
                var alpha = (int)(peak * (1 - t) * (1 - t));
                if (alpha <= 0) continue;

                var bounds = RectangleF.Inflate(card.Bounds, i * 0.6f, i * 0.6f);
                bounds.Offset(0, drop);
                using var path = RoundedRect(bounds, radius + i * 0.6f);
                using var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
                g.FillPath(brush, path);
            }
        }
    }

    /// <summary>Dresses a menu (and its submenus) to match the window.</summary>
    public static void StyleMenu(ToolStripDropDown menu)
    {
        menu.RenderMode = ToolStripRenderMode.ManagerRenderMode;
        menu.BackColor = Surface;
        menu.ForeColor = Text;
        menu.Font = Body;
        menu.Padding = new Padding((int)menu.Dp(4));
        menu.Opened -= OnMenuOpened;
        menu.Opened += OnMenuOpened;

        foreach (ToolStripItem item in menu.Items)
        {
            if (item is ToolStripSeparator) continue;
            item.Padding = new Padding((int)menu.Dp(4), (int)menu.Dp(5), (int)menu.Dp(12), (int)menu.Dp(5));
            if (item is ToolStripMenuItem { HasDropDownItems: true } parent)
                StyleMenu(parent.DropDown);
        }
    }

    static void OnMenuOpened(object? sender, EventArgs e)
    {
        if (sender is Control popup) ApplyPopupTrim(popup);
    }
}
