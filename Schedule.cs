namespace IdasenDeskControl;

public enum DeskState
{
    /// <summary>Outside the day window, or the day is switched off: the schedule keeps its hands off.</summary>
    Idle,
    Sit,
    Stand
}

public readonly record struct ScheduleState(DeskState State, double TargetCm, double SitCm);

/// <summary>One weekday: when the schedule is live, and the stand slot inside each hour.</summary>
public sealed class DaySchedule
{
    public bool Enabled { get; set; }

    /// <summary>Minutes since midnight. Stored as ints so the JSON stays obvious and portable.</summary>
    public int FromMinute { get; set; } = 9 * 60;
    public int ToMinute { get; set; } = 17 * 60;

    /// <summary>Minute past each hour at which the desk goes up.</summary>
    public int StandAtMinute { get; set; } = 50;

    /// <summary>How long it stays up.</summary>
    public int StandMinutes { get; set; } = 10;

    public double StandCm { get; set; } = 110.0;
    public double SitCm { get; set; } = 72.0;

    public DaySchedule Clone() => (DaySchedule)MemberwiseClone();
}

public sealed class WeekSchedule
{
    /// <summary>Indexed by <see cref="DayOfWeek"/>, so 0 is Sunday.</summary>
    public List<DaySchedule> Days { get; set; } = CreateDefaultDays();

    static List<DaySchedule> CreateDefaultDays()
    {
        var days = new List<DaySchedule>();
        for (var i = 0; i < 7; i++)
        {
            var day = new DaySchedule();
            // Monday to Friday on by default; the weekend is nobody's business.
            day.Enabled = i is >= (int)DayOfWeek.Monday and <= (int)DayOfWeek.Friday;
            days.Add(day);
        }
        return days;
    }

    public DaySchedule this[DayOfWeek day] => Days[(int)day];

    /// <summary>Repairs a schedule that came back short or null from an older settings file.</summary>
    public WeekSchedule Normalized()
    {
        while (Days.Count < 7) Days.Add(new DaySchedule());
        if (Days.Count > 7) Days.RemoveRange(7, Days.Count - 7);
        return this;
    }

    public ScheduleState Evaluate(DateTime now)
    {
        var day = this[now.DayOfWeek];
        if (!day.Enabled) return new ScheduleState(DeskState.Idle, 0, day.SitCm);

        var time = now.TimeOfDay;
        if (time < TimeSpan.FromMinutes(day.FromMinute) || time >= TimeSpan.FromMinutes(day.ToMinute))
            return new ScheduleState(DeskState.Idle, 0, day.SitCm);

        // Stand slots are anchored to the top of each hour. Check the current hour and
        // the previous one, so a slot that runs past :59 still counts.
        var topOfHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        for (var hoursBack = 0; hoursBack <= 1; hoursBack++)
        {
            var start = topOfHour.AddHours(-hoursBack).AddMinutes(day.StandAtMinute);
            var end = start.AddMinutes(day.StandMinutes);
            if (now >= start && now < end)
                return new ScheduleState(DeskState.Stand, day.StandCm, day.SitCm);
        }

        return new ScheduleState(DeskState.Sit, day.SitCm, day.SitCm);
    }

    /// <summary>
    /// The next moment the desk would actually be asked to move, or null if nothing is
    /// scheduled in the coming week. Brute force by the minute - a week is 10080 checks.
    /// </summary>
    public (DateTime When, DeskState To)? NextMove(DateTime from)
    {
        var cursor = new DateTime(from.Year, from.Month, from.Day, from.Hour, from.Minute, 0);
        var state = Evaluate(from).State;

        for (var i = 1; i <= 7 * 24 * 60; i++)
        {
            var at = cursor.AddMinutes(i);
            var next = Evaluate(at).State;
            if (next != state && (next == DeskState.Stand || state == DeskState.Stand))
                return (at, next);
            state = next;
        }

        return null;
    }
}
