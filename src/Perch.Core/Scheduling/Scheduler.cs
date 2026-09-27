namespace Perch.Scheduling;

/// <summary>
/// Watches the clock and asks for a move only when the schedule crosses a boundary.
/// Deliberately edge triggered rather than level triggered: if you shove the desk
/// somewhere else by hand mid-slot, it stays there until the next boundary instead of
/// being dragged back every few seconds.
///
/// Events are raised on a thread-pool thread; a UI has to marshal them itself.
/// </summary>
public sealed class Scheduler : IDisposable
{
    static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    readonly TimeProvider _time;
    readonly ITimer? _timer;
    readonly object _lock = new();
    WeekSchedule _schedule = new();
    bool _enabled;
    DeskState? _last;
    double _returnCm;

    /// <summary>Target height, and a short phrase for the status bar.</summary>
    public event Action<double, string>? MoveRequested;

    /// <summary>Fires on every tick so the UI can redraw the countdown.</summary>
    public event Action? Ticked;

    /// <param name="time">The clock. Tests pass their own and call <see cref="Evaluate"/> by hand.</param>
    /// <param name="startTimer">False for tests, which drive <see cref="Evaluate"/> themselves.</param>
    public Scheduler(TimeProvider? time = null, bool startTimer = true)
    {
        _time = time ?? TimeProvider.System;
        if (startTimer) _timer = _time.CreateTimer(_ => Evaluate(), null, Interval, Interval);
    }

    DateTime Now => _time.GetLocalNow().DateTime;

    public WeekSchedule Schedule
    {
        get => _schedule;
        set
        {
            lock (_lock)
            {
                _schedule = value.Normalized();
                _last = null; // re-seed rather than fire off an edit as if it were a boundary
            }
            Ticked?.Invoke();
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            lock (_lock)
            {
                _enabled = value;
                _last = null;
            }
            Ticked?.Invoke();
        }
    }

    public (DateTime When, DeskState To)? NextMove =>
        _enabled ? _schedule.NextMove(Now) : null;

    public void Evaluate()
    {
        (double Cm, string Reason)? move = null;

        lock (_lock)
        {
            if (!_enabled)
            {
                _last = null;
            }
            else
            {
                var state = _schedule.Evaluate(Now);

                // First look after start-up or an edit: adopt the current state silently, so
                // launching the app mid-slot does not make the desk lurch.
                if (_last is null)
                {
                    _last = state.State;
                    _returnCm = state.SitCm;
                }
                else if (state.State != _last)
                {
                    if (state.State == DeskState.Stand)
                    {
                        _returnCm = state.SitCm;
                        move = (state.TargetCm, "scheduled stand");
                    }
                    else if (_last == DeskState.Stand)
                    {
                        // Leaving a stand slot, including when the day window closes underneath it.
                        move = (_returnCm, "scheduled sit");
                    }

                    _last = state.State;
                }
            }
        }

        if (move is { } m) MoveRequested?.Invoke(m.Cm, m.Reason);
        Ticked?.Invoke();
    }

    public void Dispose() => _timer?.Dispose();
}
