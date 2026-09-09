using Perch;

var failures = 0;

void Check(string label, object expected, object actual)
{
    var ok = expected.ToString() == actual.ToString();
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label,-46} expected {expected}, got {actual}");
}

// Monday 2026-09-14. Window 09:00-17:00, stand at :50 for 10 minutes.
var week = new WeekSchedule();
foreach (var d in week.Days) d.Enabled = false;
var mon = week[DayOfWeek.Monday];
mon.Enabled = true;
mon.FromMinute = 9 * 60;
mon.ToMinute = 17 * 60;
mon.StandAtMinute = 50;
mon.StandMinutes = 10;
mon.StandCm = 110;
mon.SitCm = 72;

DateTime M(int h, int m) => new(2026, 9, 14, h, m, 0);

Check("Mon 08:00 before the window", DeskState.Idle, week.Evaluate(M(8, 0)).State);
Check("Mon 09:00 window opens", DeskState.Sit, week.Evaluate(M(9, 0)).State);
Check("Mon 09:49 just before the slot", DeskState.Sit, week.Evaluate(M(9, 49)).State);
Check("Mon 09:50 slot starts", DeskState.Stand, week.Evaluate(M(9, 50)).State);
Check("Mon 09:59 still standing", DeskState.Stand, week.Evaluate(M(9, 59)).State);
Check("Mon 10:00 slot ends", DeskState.Sit, week.Evaluate(M(10, 0)).State);
Check("Mon 16:55 last slot", DeskState.Stand, week.Evaluate(M(16, 55)).State);
Check("Mon 17:00 window closes mid-slot", DeskState.Idle, week.Evaluate(M(17, 0)).State);
Check("Mon 09:50 target height", 110d, week.Evaluate(M(9, 50)).TargetCm);
Check("Mon 10:00 target height", 72d, week.Evaluate(M(10, 0)).TargetCm);
Check("Mon 17:00 return height still known", 72d, week.Evaluate(M(17, 0)).SitCm);

// A disabled day is left alone entirely.
Check("Tue 09:50 day switched off", DeskState.Idle, week.Evaluate(new DateTime(2026, 9, 15, 9, 50, 0)).State);

// A slot that runs past the top of the hour.
mon.StandAtMinute = 55;
mon.StandMinutes = 10;
Check("Spill: Mon 09:56 in slot", DeskState.Stand, week.Evaluate(M(9, 56)).State);
Check("Spill: Mon 10:03 still in slot", DeskState.Stand, week.Evaluate(M(10, 3)).State);
Check("Spill: Mon 10:05 slot over", DeskState.Sit, week.Evaluate(M(10, 5)).State);

// Next move lookahead.
mon.StandAtMinute = 50;
mon.StandMinutes = 10;
var next = week.NextMove(M(9, 0));
Check("Next move from Mon 09:00 when", M(9, 50), next!.Value.When);
Check("Next move from Mon 09:00 what", DeskState.Stand, next!.Value.To);

var down = week.NextMove(M(9, 52));
Check("Next move from Mon 09:52 when", M(10, 0), down!.Value.When);
Check("Next move from Mon 09:52 what", DeskState.Sit, down!.Value.To);

// Nothing enabled at all.
var empty = new WeekSchedule();
foreach (var d in empty.Days) d.Enabled = false;
Check("Nothing enabled", "null", empty.NextMove(M(9, 0))?.ToString() ?? "null");

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) failed.");
return failures;
