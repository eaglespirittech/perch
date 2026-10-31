using Perch.Bluetooth;
using Perch.Desk;
using Perch.Scheduling;
using Perch.Updates;

var failures = 0;

void Check(string label, object expected, object actual)
{
    var ok = expected.ToString() == actual.ToString();
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label,-46} expected {expected}, got {actual}");
}

// ---- schedule --------------------------------------------------------------

// Monday 2026-09-14. Window 09:00-17:00, stand at :50 for 10 minutes.
var week = new WeekSchedule { Mode = ScheduleMode.Hourly };
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
var empty = new WeekSchedule { Mode = ScheduleMode.Hourly };
foreach (var d in empty.Days) d.Enabled = false;
Check("Nothing enabled", "null", empty.NextMove(M(9, 0))?.ToString() ?? "null");

// Rotation: sit 10, stand 5, from 09:00.
week.Mode = ScheduleMode.Rotation;
mon.RotationSitMinutes = 10;
mon.RotationStandMinutes = 5;
Check("Rotate: Mon 08:59 before the window", DeskState.Idle, week.Evaluate(M(8, 59)).State);
Check("Rotate: Mon 09:00 sits first", DeskState.Sit, week.Evaluate(M(9, 0)).State);
Check("Rotate: Mon 09:09 still sitting", DeskState.Sit, week.Evaluate(M(9, 9)).State);
Check("Rotate: Mon 09:10 stands", DeskState.Stand, week.Evaluate(M(9, 10)).State);
Check("Rotate: Mon 09:10 target height", 110d, week.Evaluate(M(9, 10)).TargetCm);
Check("Rotate: Mon 09:14 still standing", DeskState.Stand, week.Evaluate(M(9, 14)).State);
Check("Rotate: Mon 09:15 sits again", DeskState.Sit, week.Evaluate(M(9, 15)).State);
Check("Rotate: Mon 09:25 second stand", DeskState.Stand, week.Evaluate(M(9, 25)).State);
Check("Rotate: Mon 16:55 last stand", DeskState.Stand, week.Evaluate(M(16, 55)).State);
Check("Rotate: Mon 17:00 window closes", DeskState.Idle, week.Evaluate(M(17, 0)).State);

var rotateNext = week.NextMove(M(9, 12));
Check("Rotate: next move from 09:12 when", M(9, 15), rotateNext!.Value.When);
Check("Rotate: next move from 09:12 what", DeskState.Sit, rotateNext!.Value.To);

// Counting starts at the window, not the hour.
mon.FromMinute = 9 * 60 + 7;
Check("Rotate: window at 09:07 stands at 09:17", DeskState.Stand, week.Evaluate(M(9, 17)).State);
Check("Rotate: window at 09:07 sits at 09:16", DeskState.Sit, week.Evaluate(M(9, 16)).State);
mon.FromMinute = 9 * 60;

// The mode survives a trip through settings.json as a word, and old files read as hourly.
var json = System.Text.Json.JsonSerializer.Serialize(week);
Check("Rotate: mode saved by name", true, json.Contains("\"Mode\":\"Rotation\""));
var roundTrip = System.Text.Json.JsonSerializer.Deserialize<WeekSchedule>(json)!;
Check("Rotate: mode read back", ScheduleMode.Rotation, roundTrip.Mode);
Check("Rotate: sit minutes read back", 10, roundTrip[DayOfWeek.Monday].RotationSitMinutes);
Check("Rotate: new schedules rotate", ScheduleMode.Rotation, new WeekSchedule().Mode);
Check("Rotate: old settings stay hourly", ScheduleMode.Hourly,
    Perch.Settings.Parse("{\"Schedule\":{\"Days\":[]}}")!.Schedule.Mode);
Check("Rotate: settings without a schedule rotate", ScheduleMode.Rotation, Perch.Settings.Parse("{}")!.Schedule.Mode);
Check("Rotate: saved mode wins", ScheduleMode.Rotation,
    Perch.Settings.Parse("{\"Schedule\":{\"Mode\":\"Rotation\"}}")!.Schedule.Mode);

// Scheduler moves the desk on rotation edges just like hourly ones.
var rotateClock = new FakeClock(M(9, 5));
var rotateMoves = new List<string>();
using (var scheduler = new Scheduler(rotateClock, startTimer: false) { Schedule = week, Enabled = true })
{
    scheduler.MoveRequested += (cm, reason) => rotateMoves.Add($"{reason} {cm}");
    scheduler.Evaluate();
    rotateClock.Now = M(9, 10);
    scheduler.Evaluate();
    rotateClock.Now = M(9, 15);
    scheduler.Evaluate();
    Check("Rotate: scheduler up then down", "scheduled stand 110,scheduled sit 72", string.Join(",", rotateMoves));
}
week.Mode = ScheduleMode.Hourly;

// People type times every which way.
Check("ParseTime 9", 540, ScheduleText.ParseTime("9", 0));
Check("ParseTime 9:30", 570, ScheduleText.ParseTime("9:30", 0));
Check("ParseTime 0930", 570, ScheduleText.ParseTime("0930", 0));
Check("ParseTime nonsense falls back", 42, ScheduleText.ParseTime("soon", 42));
Check("ParseHeight clamps", 127d, ScheduleText.ParseHeight("200", 72));
Check("Time formats", "07:05", ScheduleText.Time(425));

// ---- scheduler: edge triggered ---------------------------------------------

var clock = new FakeClock(M(9, 45));
var moves = new List<string>();
using (var scheduler = new Scheduler(clock, startTimer: false) { Schedule = week, Enabled = true })
{
    scheduler.MoveRequested += (cm, reason) => moves.Add($"{reason} {cm}");

    scheduler.Evaluate();
    Check("Scheduler: first look only adopts", 0, moves.Count);

    clock.Now = M(9, 50);
    scheduler.Evaluate();
    Check("Scheduler: slot opens", "scheduled stand 110", moves.LastOrDefault() ?? "none");

    clock.Now = M(9, 55);
    scheduler.Evaluate();
    Check("Scheduler: no repeat mid-slot", 1, moves.Count);

    clock.Now = M(10, 0);
    scheduler.Evaluate();
    Check("Scheduler: slot closes", "scheduled sit 72", moves.LastOrDefault() ?? "none");

    // Starting mid-slot must not make the desk lurch.
    moves.Clear();
    clock.Now = M(10, 52);
    scheduler.Schedule = week;
    scheduler.Evaluate();
    Check("Scheduler: edit mid-slot is silent", 0, moves.Count);

    // The day window closing while standing brings the desk down.
    clock.Now = M(16, 50);
    scheduler.Evaluate();
    clock.Now = M(17, 0);
    scheduler.Evaluate();
    Check("Scheduler: window closes while up", "scheduled sit 72", moves.LastOrDefault() ?? "none");
}

// ---- protocol --------------------------------------------------------------

var encoded = LinakProtocol.EncodeTarget(110.5);
Check("Encode 110.5 cm", "F2-12", BitConverter.ToString(encoded)); // 4850 counts = 0x12F2, little endian
Check("Encode 110.5 cm raw", 4850, encoded[0] | (encoded[1] << 8));
Check("Encode clamps below range", 0, BitConverter.ToUInt16(LinakProtocol.EncodeTarget(10)));
LinakProtocol.TryDecodeHeight(new byte[] { 0xF2, 0x12, 0x00, 0x00 }, out var decoded, out _);
Check("Decode 4850 counts", 110.5, decoded);
Check("Decode rejects short value", false, LinakProtocol.TryDecodeHeight(new byte[] { 1, 2 }, out _, out _));

// ---- device picking --------------------------------------------------------

var devices = new[] { new BleDevice("a", "Keyboard"), new BleDevice("b", "Desk 9770"), new BleDevice("c", "Mouse") };
Check("Pick: saved wins", "c", DeskPicker.Pick(devices, "c")?.Id ?? "none");
Check("Pick: desk-like name", "b", DeskPicker.Pick(devices, null)?.Id ?? "none");
Check("Pick: explicit name", "a", DeskPicker.Pick(devices, "b", "keyb")?.Id ?? "none");
Check("Pick: nothing desk-like", "none", DeskPicker.Pick(new[] { devices[0] }, null)?.Id ?? "none");

// ---- updates ---------------------------------------------------------------

const string releaseJson = """
    {
      "tag_name": "v1.4.0",
      "html_url": "https://github.com/eaglespirittech/perch/releases/tag/v1.4.0",
      "draft": false,
      "prerelease": false,
      "assets": [
        { "name": "Perch-1.4.0-setup.exe", "size": 52000000,
          "browser_download_url": "https://github.com/eaglespirittech/perch/releases/download/v1.4.0/Perch-1.4.0-setup.exe" },
        { "name": "Perch-1.4.0-setup.exe.sha256", "size": 88,
          "browser_download_url": "https://github.com/eaglespirittech/perch/releases/download/v1.4.0/Perch-1.4.0-setup.exe.sha256" },
        { "name": "plain.exe", "size": 1, "browser_download_url": "http://example.com/plain.exe" }
      ]
    }
    """;
var release = Release.Parse(releaseJson)!;
Check("Update: version from the tag", "1.4.0.0", release.Version);
Check("Update: version text has no v", "1.4.0", release.VersionText);
Check("Update: asset found by name", 52000000L, release.Asset("Perch-1.4.0-setup.exe")!.Size);
Check("Update: plain http assets ignored", "null", release.Asset("plain.exe")?.Name ?? "null");
Check("Update: newer than 1.3.9", true, release.Version > Release.Normalize(new Version(1, 3, 9, 0)));
Check("Update: not newer than itself", false, release.Version > Release.Normalize(new Version(1, 4, 0, 0)));
Check("Update: drafts skipped", "null", Release.Parse("""{ "tag_name": "v9.0.0", "draft": true }""")?.ToString() ?? "null");
Check("Update: prereleases skipped", "null", Release.Parse("""{ "tag_name": "v9.0.0", "prerelease": true }""")?.ToString() ?? "null");
Check("Update: odd tags skipped", "null", Release.Parse("""{ "tag_name": "nightly" }""")?.ToString() ?? "null");
Check("Update: four-part version", "1.2.3.4", Release.ParseVersion("1.2.3.4")!);

var hashA = new string('a', 62) + "Bc";
var hashB = new string('b', 64);
var checksums = $"{hashA}  Perch-1.4.0-setup.exe\n{hashB} *Perch-1.4.0-win-x64.exe\nnot a hash  plain.exe\n";
Check("Update: checksum found in the list", new string('a', 62) + "bc",
    ReleaseSignature.FindChecksum(checksums, "Perch-1.4.0-setup.exe") ?? "null");
Check("Update: binary-mode line read", hashB, ReleaseSignature.FindChecksum(checksums, "Perch-1.4.0-win-x64.exe") ?? "null");
Check("Update: junk is not a checksum", "null", ReleaseSignature.FindChecksum(checksums, "plain.exe") ?? "null");
Check("Update: unlisted file has no checksum", "null", ReleaseSignature.FindChecksum(checksums, "other.exe") ?? "null");

// Signed by a stand-in key the way the release workflow signs: ECDSA P-256, DER signature.
using (var signer = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256))
{
    var list = System.Text.Encoding.UTF8.GetBytes(checksums);
    var signature = signer.SignData(list, System.Security.Cryptography.HashAlgorithmName.SHA256,
        System.Security.Cryptography.DSASignatureFormat.Rfc3279DerSequence);
    var publicKey = signer.ExportSubjectPublicKeyInfoPem();

    Check("Update: signed list verifies", true, ReleaseSignature.Verify(list, signature, publicKey));
    var tampered = (byte[])list.Clone();
    tampered[0] ^= 1;
    Check("Update: altered list fails", false, ReleaseSignature.Verify(tampered, signature, publicKey));
    Check("Update: other key's signature fails", false, ReleaseSignature.Verify(list, signature));
    Check("Update: garbage signature fails", false, ReleaseSignature.Verify(list, [1, 2, 3], publicKey));
}

// ---- the move loop, against the simulated desk -----------------------------

var sim = new SimulatedBluetooth { SpeedCmPerSecond = 40 };
using (var desk = new DeskController(sim))
{
    await desk.ConnectAsync(SimulatedBluetooth.DeviceId);
    Check("Desk: connected", true, desk.IsConnected);
    Check("Desk: reads starting height", "72.0", desk.CurrentCm?.ToString("0.0") ?? "none");

    await desk.MoveToAsync(100, CancellationToken.None, TimeSpan.FromSeconds(10));
    Check("Desk: arrives within tolerance", true, Math.Abs(sim.HeightCm - 100) <= 0.15);
    Check("Desk: stops at the end", "stop", sim.Log.LastOrDefault() ?? "none");

    await desk.MoveToAsync(500, CancellationToken.None, TimeSpan.FromSeconds(10));
    Check("Desk: target clamped to the top", "127.0", sim.HeightCm.ToString("0.0"));

    // Something in the way: the loop must give up rather than push forever.
    await desk.MoveToAsync(90, CancellationToken.None, TimeSpan.FromSeconds(10));
    sim.Obstacle = 110;
    string outcome;
    try
    {
        await desk.MoveToAsync(120, CancellationToken.None, TimeSpan.FromSeconds(3));
        outcome = "arrived";
    }
    catch (Exception ex) when (ex is IOException or TimeoutException)
    {
        outcome = "gave up";
    }
    Check("Desk: gives up at an obstacle", "gave up", outcome);
    Check("Desk: stopped at the obstacle", "110.0", sim.HeightCm.ToString("0.0"));
}

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) failed.");
return failures;

/// <summary>A clock the scheduler tests can move by hand.</summary>
sealed class FakeClock(DateTime now) : TimeProvider
{
    public DateTime Now { get; set; } = now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
}
