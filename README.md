# Perch

Sit/stand desk control for the IKEA IDÅSEN. A small Windows app that drives the desk to
an exact height over Bluetooth LE, with presets and an hourly stand schedule. Type a
height in cm, press Go.

Not affiliated with or endorsed by Inter IKEA Systems B.V. IDÅSEN is their trademark;
it appears here only to say which desk this controls.

- `dist\Perch.exe` — self-contained single file, no .NET runtime needed.
- Settings (chosen device, presets, schedule) live in `%APPDATA%\Perch\settings.json`.
  A settings file from the app's earlier name is picked up automatically on first run.

## Before first use

1. Pair the desk in **Settings → Bluetooth & devices → Add device → Bluetooth**.
   Hold the small pairing button on the control box under the desktop until its LED
   blinks, then pick the desk from the list.
2. Close the IKEA *Desk Control* phone app. The desk accepts one Bluetooth connection
   at a time, and whoever holds it wins.

## Using it

The window follows the Windows light/dark setting and your accent colour. Set
`PERCH_THEME=dark` or `PERCH_THEME=light` to override it.

- **Height** - the big readout is where the desk is now; the bar under it shows where that
  sits in the desk's 62-127 cm travel.
- **Move to** - type a height, or step it with the minus and plus buttons, then press Go
  (Enter works too). **Stop** halts a move in progress.
- **Presets** - two slots; "Save current" captures the height the desk is at now.
- **Menu** (the dots, top right) - pick which paired desk to use, connect or disconnect,
  nudge the desk a centimetre either way, toggle **Start with Windows**, or open the
  settings folder.

## Start with Windows

The menu item writes a per-user entry under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, so it needs no admin rights and
shows up in Task Manager's Startup tab, where it can also be disabled. Moving `Perch.exe`
does not break it: on each launch the entry is rewritten to point at wherever the exe is
running from.

## Schedule

Flip **Run the schedule** on and press **Edit schedule** for a row per weekday:

| Column | Meaning |
|--------|---------|
| Day | whether the schedule touches that day at all |
| From / Until | the window during the day when it applies |
| Stand at | the minute past *every* hour when the desk goes up |
| For (min) | how long it stays up |
| Stand cm | the height it goes up to |
| Return cm | the height it drops back to afterwards |

So `09:00`–`17:00`, stand at `50` for `10`, up `110`, return `72` means: every hour
between 9 and 5, stand from :50 to :00 at 110 cm, then back to 72 cm.

**Copy** clones a finished row onto other days — all of them, just the weekdays, just
the weekend, or one named day.

Details worth knowing:

- The schedule is **edge triggered**. It moves the desk when a slot opens or closes and at
  no other time, so if you push the desk somewhere else mid-slot it stays there until the
  next boundary rather than fighting you.
- Starting the app (or saving an edit) in the middle of a slot does **not** cause a move.
  The current state is adopted quietly; the next boundary is the first thing that acts.
- A slot may run past the top of the hour (`stand at 55` for `10` minutes ends at `:05`).
- If the day window closes while the desk is up, it comes back down at that point.
- A scheduled move reconnects to the saved desk first if the connection has dropped, and
  waits its turn if you happen to be driving the desk by hand at that moment.
- The app has to be running for the schedule to fire. Switch on **Start with Windows**
  from the menu so it always is; there is no separate service or tray component.

## How it talks to the desk

The desk is a rebranded Linak controller with three vendor GATT services:

| Service    | Characteristic | Use                                              |
|------------|----------------|--------------------------------------------------|
| `99fa0001` | `99fa0002`     | control: wake up `FE 00`, stop `FF 00`           |
| `99fa0020` | `99fa0021`     | notifies height + speed                          |
| `99fa0030` | `99fa0031`     | target height; the desk drives itself there       |

Height is a little-endian `uint16` in 0.1 mm above the 620 mm bottom stop; speed is a
little-endian `int16`. Moving means writing the target to `99fa0031` about five times a
second — the controller stops the moment that stream pauses, which is what keeps the
anti-collision behaviour intact. `MoveToAsync` re-sends until the height settles within
1.5 mm of the target, re-wakes the controller if it stalls, and gives up after 60 s.

## tools\DeskProbe

Read-only console helper for when the GUI will not connect:

```
DeskProbe                  # list paired Bluetooth LE devices
DeskProbe LIFT             # connect to the first name match, print its height
DeskProbe LIFT --watch     # keep printing height changes
```

## Build

```
dotnet build Perch.csproj -c Release
dotnet run --project tests/ScheduleTests/ScheduleTests.csproj -c Release
dotnet publish Perch.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

`tests/ScheduleTests` is a plain console app that asserts the schedule state machine and
exits with the number of failures, so CI needs no test framework.

## Releasing

`.github/workflows/ci.yml` builds both projects with warnings as errors and runs the
tests on every push and pull request.

`.github/workflows/release.yml` fires on a version tag. It re-runs the tests, publishes a
self-contained single-file exe stamped with the tag version, writes a SHA-256 sidecar, and
attaches both to a generated GitHub release:

```
git tag v1.0.0
git push origin v1.0.0
```

The tag must look like `v1.2.3` or the workflow stops before building.
