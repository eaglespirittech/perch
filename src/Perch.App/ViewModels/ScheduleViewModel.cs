using CommunityToolkit.Mvvm.ComponentModel;
using Perch.Scheduling;
using static Perch.Scheduling.ScheduleText;

namespace Perch.App.ViewModels;

/// <summary>The schedule editor: one editable row per weekday, Monday first.</summary>
public sealed class ScheduleViewModel : ObservableObject
{
    public static readonly DayOfWeek[] DisplayOrder =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    string _error = string.Empty;

    public ScheduleViewModel(WeekSchedule schedule)
    {
        schedule = schedule.Normalized();
        Days = DisplayOrder.Select(day => new DayRow(day, schedule[day])).ToList();
    }

    public IReadOnlyList<DayRow> Days { get; }

    public string Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public DayRow this[DayOfWeek day] => Days.First(d => d.Day == day);

    public void CopyTo(DayRow source, IEnumerable<DayOfWeek> targets)
    {
        var values = source.Read();
        foreach (var target in targets) this[target].Load(values.Clone());
    }

    /// <summary>The finished schedule, or null with <see cref="Error"/> set when something needs fixing.</summary>
    public WeekSchedule? TryBuild(out DayRow? problem)
    {
        var result = new WeekSchedule();
        foreach (var row in Days)
        {
            var value = row.Read();
            if (value.Enabled && value.ToMinute <= value.FromMinute)
            {
                Error = $"{row.Name}: the active window has to end after it starts.";
                problem = row;
                return null;
            }
            result.Days[(int)row.Day] = value;
        }

        Error = string.Empty;
        problem = null;
        return result;
    }
}

public sealed class DayRow : ObservableObject
{
    bool _enabled;
    string _from = string.Empty, _until = string.Empty, _standAt = string.Empty,
        _standFor = string.Empty, _standCm = string.Empty, _sitCm = string.Empty;

    public DayRow(DayOfWeek day, DaySchedule value)
    {
        Day = day;
        Load(value);
    }

    public DayOfWeek Day { get; }
    public string Name => Day.ToString();

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public string From { get => _from; set => SetProperty(ref _from, value); }
    public string Until { get => _until; set => SetProperty(ref _until, value); }
    public string StandAt { get => _standAt; set => SetProperty(ref _standAt, value); }
    public string StandFor { get => _standFor; set => SetProperty(ref _standFor, value); }
    public string StandCm { get => _standCm; set => SetProperty(ref _standCm, value); }
    public string SitCm { get => _sitCm; set => SetProperty(ref _sitCm, value); }

    public void Load(DaySchedule value)
    {
        Enabled = value.Enabled;
        From = Time(value.FromMinute);
        Until = Time(value.ToMinute);
        StandAt = Whole(Math.Clamp(value.StandAtMinute, 0, 59));
        StandFor = Whole(Math.Clamp(value.StandMinutes, 1, 59));
        StandCm = Centimetres(value.StandCm);
        SitCm = Centimetres(value.SitCm);
    }

    public DaySchedule Read() => new()
    {
        Enabled = Enabled,
        FromMinute = ParseTime(From, 9 * 60),
        ToMinute = ParseTime(Until, 17 * 60),
        StandAtMinute = ParseWhole(StandAt, 50, 0, 59),
        StandMinutes = ParseWhole(StandFor, 10, 1, 59),
        StandCm = ParseHeight(StandCm, 110),
        SitCm = ParseHeight(SitCm, 72)
    };

    /// <summary>Rewrites every field in its tidy form, e.g. after a field loses focus.</summary>
    public void Normalise() => Load(Read());
}
