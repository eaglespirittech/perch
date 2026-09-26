using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Perch.App.Theme;

/// <summary>
/// Colours derived from the system light/dark setting and the user's accent colour, on
/// Windows and macOS alike. Published two ways: as brushes in the application resources
/// (so XAML uses DynamicResource and follows changes), and as <see cref="Current"/> for
/// controls that paint themselves.
/// </summary>
public sealed class Palette
{
    public bool IsDark { get; private init; }

    public Color Accent { get; private init; }
    public Color AccentHover { get; private init; }
    public Color AccentPressed { get; private init; }
    public Color AccentBright { get; private init; }
    public Color AccentSoft { get; private init; }
    public Color OnAccent { get; private init; }

    public Color Window { get; private init; }
    public Color Surface { get; private init; }
    public Color Border { get; private init; }
    public Color Field { get; private init; }
    public Color FieldHover { get; private init; }
    public Color FieldPressed { get; private init; }
    public Color Text { get; private init; }
    public Color TextSecondary { get; private init; }
    public Color TextDisabled { get; private init; }
    public Color Track { get; private init; }

    public Color Success { get; private init; }
    public Color Caution { get; private init; }
    public Color Danger { get; private init; }
    public Color DangerSoft { get; private init; }
    public Color DangerSoftHover { get; private init; }

    public static Palette Current { get; private set; } = Build(false, Color.FromRgb(0, 95, 184));

    /// <summary>Raised after the system switches between light and dark, or changes accent.</summary>
    public static event Action? Changed;

    /// <summary>Reads the system colours, applies them, and keeps following them.</summary>
    public static void Initialize(Application app)
    {
        Apply(app);
        if (app.PlatformSettings is { } settings)
            settings.ColorValuesChanged += (_, _) => Apply(app);
    }

    static void Apply(Application app)
    {
        var fallbackAccent = Color.FromRgb(0, 95, 184);
        var dark = false;
        var accent = fallbackAccent;

        if (app.PlatformSettings?.GetColorValues() is { } values)
        {
            dark = values.ThemeVariant == PlatformThemeVariant.Dark;
            // On a dark background the plain accent is often too dim to read against.
            accent = dark && values.AccentColor2 != default ? values.AccentColor2 : values.AccentColor1;
            if (accent == default) accent = fallbackAccent;
        }

        // Escape hatch for anyone who wants to override the system setting.
        dark = Environment.GetEnvironmentVariable("PERCH_THEME")?.ToLowerInvariant() switch
        {
            "dark" => true,
            "light" => false,
            _ => dark
        };

        Current = Build(dark, accent);
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Current.Publish(app.Resources);
        Changed?.Invoke();
    }

    static Palette Build(bool dark, Color accent)
    {
        var surface = dark ? Mix(Color.FromRgb(41, 41, 44), accent, 0.04) : Colors.White;
        var danger = dark ? Color.FromRgb(255, 153, 164) : Color.FromRgb(196, 43, 28);

        // Neutrals carry a trace of the accent, which is what keeps the window from
        // reading as plain grey.
        return new Palette
        {
            IsDark = dark,
            Accent = accent,
            AccentHover = Shift(accent, dark ? -0.06 : 0.08),
            AccentPressed = Shift(accent, dark ? -0.12 : 0.16),
            AccentBright = Shift(accent, dark ? 0.25 : 0.35),
            AccentSoft = Mix(surface, accent, dark ? 0.16 : 0.10),
            OnAccent = Luminance(accent) > 0.6 ? Color.FromRgb(23, 23, 23) : Colors.White,

            Window = dark ? Mix(Color.FromRgb(28, 28, 30), accent, 0.04) : Mix(Color.FromRgb(242, 243, 246), accent, 0.035),
            Surface = surface,
            Border = dark ? Mix(Color.FromRgb(56, 56, 60), accent, 0.04) : Mix(Color.FromRgb(226, 227, 232), accent, 0.04),
            Field = dark ? Mix(Color.FromRgb(52, 52, 56), accent, 0.04) : Mix(Color.FromRgb(246, 247, 249), accent, 0.03),
            FieldHover = dark ? Mix(Color.FromRgb(62, 62, 66), accent, 0.05) : Mix(Color.FromRgb(238, 239, 243), accent, 0.05),
            FieldPressed = dark ? Mix(Color.FromRgb(47, 47, 51), accent, 0.05) : Mix(Color.FromRgb(230, 231, 236), accent, 0.06),
            Text = dark ? Color.FromRgb(245, 245, 247) : Color.FromRgb(24, 24, 28),
            TextSecondary = dark ? Color.FromRgb(166, 166, 172) : Color.FromRgb(98, 98, 108),
            TextDisabled = dark ? Color.FromRgb(104, 104, 110) : Color.FromRgb(162, 162, 170),
            Track = dark ? Mix(Color.FromRgb(66, 66, 70), accent, 0.06) : Mix(Color.FromRgb(224, 225, 230), accent, 0.08),

            Success = dark ? Color.FromRgb(108, 203, 95) : Color.FromRgb(16, 124, 16),
            Caution = dark ? Color.FromRgb(252, 200, 60) : Color.FromRgb(196, 120, 0),
            Danger = danger,
            DangerSoft = Mix(surface, danger, dark ? 0.14 : 0.08),
            DangerSoftHover = Mix(surface, danger, dark ? 0.22 : 0.14)
        };
    }

    /// <summary>Writes every colour as a "Perch.Name" brush, which the XAML styles refer to.</summary>
    void Publish(IResourceDictionary resources)
    {
        foreach (var property in typeof(Palette).GetProperties())
        {
            if (property.PropertyType != typeof(Color)) continue;
            resources[$"Perch.{property.Name}"] = new SolidColorBrush((Color)property.GetValue(this)!);
        }

        // Fluent's own controls (text selection, toggle switches, focus) take the same accent.
        resources["SystemAccentColor"] = Accent;
        resources["SystemAccentColorLight1"] = AccentBright;
        resources["SystemAccentColorDark1"] = AccentPressed;
    }

    static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    /// <summary>Positive lightens toward white, negative darkens toward black.</summary>
    public static Color Shift(Color color, double amount)
    {
        static byte Channel(byte channel, double amount) => (byte)(amount >= 0
            ? channel + (255 - channel) * amount
            : channel * (1 + amount));

        return Color.FromArgb(color.A, Channel(color.R, amount), Channel(color.G, amount), Channel(color.B, amount));
    }

    /// <summary>Blends <paramref name="from"/> toward <paramref name="to"/>; 0 is all from, 1 all to.</summary>
    public static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);
        return Color.FromArgb(
            Lerp(from.A, to.A, amount), Lerp(from.R, to.R, amount), Lerp(from.G, to.G, amount), Lerp(from.B, to.B, amount));
    }

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}
