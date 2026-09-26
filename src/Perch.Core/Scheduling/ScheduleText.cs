using System.Globalization;
using Perch.Desk;

namespace Perch.Scheduling;

/// <summary>
/// How the schedule editor turns what people type into schedule values and back.
/// Forgiving on the way in (9, 9:00, 09:00 and 0900 are all nine o'clock), tidy on the
/// way out.
/// </summary>
public static class ScheduleText
{
    public static string Time(int minutesSinceMidnight)
    {
        var clamped = Math.Clamp(minutesSinceMidnight, 0, 24 * 60 - 1);
        return $"{clamped / 60:00}:{clamped % 60:00}";
    }

    public static string Centimetres(double cm) => cm.ToString("0.0", CultureInfo.CurrentCulture);

    public static string Whole(int value) => value.ToString(CultureInfo.CurrentCulture);

    /// <summary>Accepts 9, 9:00, 09:00 and 0900, because people type all four.</summary>
    public static int ParseTime(string text, int fallback)
    {
        text = text.Trim();
        if (text.Length == 0) return fallback;

        int hours, minutes;
        var colon = text.IndexOf(':');

        if (colon >= 0)
        {
            if (!int.TryParse(text[..colon], out hours)) return fallback;
            if (!int.TryParse(text[(colon + 1)..], out minutes)) minutes = 0;
        }
        else if (int.TryParse(text, out var digits))
        {
            if (text.Length >= 3) { hours = digits / 100; minutes = digits % 100; }
            else { hours = digits; minutes = 0; }
        }
        else
        {
            return fallback;
        }

        return Math.Clamp(hours, 0, 23) * 60 + Math.Clamp(minutes, 0, 59);
    }

    public static int ParseWhole(string text, int fallback, int min, int max) =>
        int.TryParse(text.Trim(), out var value) ? Math.Clamp(value, min, max) : fallback;

    public static double ParseHeight(string text, double fallback)
    {
        text = text.Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var cm) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out cm))
            return fallback;

        return Math.Clamp(cm, DeskController.MinCm, DeskController.MaxCm);
    }
}
