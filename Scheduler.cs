namespace Perch;

/// <summary>
/// Watches the clock and asks for a move only when the schedule crosses a boundary.
/// Deliberately edge triggered rather than level triggered: if you shove the desk
/// somewhere else by hand mid-slot, it stays there until the next boundary instead of
/// being dragged back every few seconds.
/// </summary>
public sealed class Scheduler : IDisposable
{
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 15_000 };
    WeekSchedule _schedule = new();
    bool _enabled;
    DeskState? _last;
    double _returnCm;

    /// <summary>Target height, and a short phrase for the status bar.</summary>
    public event Action<double, string>? MoveRequested;

    /// <summary>Fires on every tick so the UI can redraw the countdown.</summary>
    public event Action? Ticked;

    public Scheduler()
    {
        _timer.Tick += (_, _) => Evaluate();
        _timer.Start();
    }

    public WeekSchedule Schedule
    {
        get => _schedule;
        set
        {
            _schedule = value.Normalized();
            _last = null; // re-seed rather than fire off an edit as if it were a boundary
            Ticked?.Invoke();
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            _last = null;
            Ticked?.Invoke();
        }
    }

    public (DateTime When, DeskState To)? NextMove =>
        _enabled ? _schedule.NextMove(DateTime.Now) : null;

    void Evaluate()
    {
        if (!_enabled)
        {
            _last = null;
            Ticked?.Invoke();
            return;
        }

        var state = _schedule.Evaluate(DateTime.Now);

        // First look after start-up or an edit: adopt the current state silently, so
        // launching the app mid-slot does not make the desk lurch.
        if (_last is null)
        {
            _last = state.State;
            _returnCm = state.SitCm;
            Ticked?.Invoke();
            return;
        }

        if (state.State != _last)
        {
            if (state.State == DeskState.Stand)
            {
                _returnCm = state.SitCm;
                MoveRequested?.Invoke(state.TargetCm, "scheduled stand");
            }
            else if (_last == DeskState.Stand)
            {
                // Leaving a stand slot, including when the day window closes underneath it.
                MoveRequested?.Invoke(_returnCm, "scheduled sit");
            }

            _last = state.State;
        }

        Ticked?.Invoke();
    }

    public void Dispose() => _timer.Dispose();
}
