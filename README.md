# Perch

Sit/stand desk control for the IKEA IDÅSEN. A small Windows app that drives the desk to
an exact height over Bluetooth LE, with presets and an hourly stand schedule. Type a
height in cm, press Go.

Not affiliated with or endorsed by Inter IKEA Systems B.V. IDÅSEN is their trademark;
it appears here only to say which desk this controls.

- **Installer** — `Perch-<version>-setup.exe` from the
  [releases page](https://github.com/eaglespirittech/perch/releases). Installs per user, so
  there is no UAC prompt, and it adds a Start Menu entry, an uninstall entry and the
  `perch-cli` command line tool.
- **Portable** — `Perch-<version>-win-x64.exe` and `perch-cli-<version>-win-x64.exe` are
  the same two programs as single self-contained files. Nothing needs the .NET runtime.
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
- **Menu** (the dots, top right) — pick which paired desk to use, connect or disconnect,
  nudge the desk a centimetre either way, toggle **Start with Windows**, or open the
  settings folder.

## Closing and the notification area

Closing the window parks Perch next to the clock rather than quitting, because the
schedule only runs while the app does. From there:

- Double-click the icon, or right-click and choose **Open Perch**, to bring the window
  back. Its tooltip carries the current height.
- **Exit Perch**, on that same menu or on the window's own menu, quits for real.

Windows often files a new tray icon under the "Show hidden icons" chevron; drag it out
onto the taskbar to keep it in sight.

## Start with Windows

The menu item writes a per-user entry under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, so it needs no admin rights and
shows up in Task Manager's Startup tab, where it can also be disabled. Moving `Perch.exe`
does not break it: on each launch the entry is rewritten to point at wherever the exe is
running from. The entry starts Perch with `--minimized`, so signing in leaves it waiting
by the clock instead of opening a window.

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

## perch-cli

The installer puts `perch-cli.exe` next to the app and offers to add it to PATH, so it
works from any terminal. It is also published on its own as a portable exe.

```
perch-cli list                    # paired devices; the saved desk is marked *
perch-cli status                  # 109.0 cm
perch-cli set 110.5               # move and wait until the desk settles
perch-cli nudge -2                # relative move
perch-cli preset 1                # one of the two heights saved in the app
perch-cli stop
perch-cli watch                   # print height changes until Ctrl+C
perch-cli help
```

Options: `--device <name|id>`, `--timeout <seconds>` (default 60), `--json`, `--quiet`,
`--direct`.

**The app and the CLI share the desk.** A desk accepts one Bluetooth connection at a
time, so when the Perch app is running it owns that connection and the CLI hands the work
to it over a per-user named pipe. When the app is not running, the CLI drives the desk
itself. Commands behave the same either way, and `--json` reports which route was used.
`--direct` forces the Bluetooth route.

### For scripts and agents

`--json` puts a single JSON object on stdout; progress chatter goes to stderr, so stdout
stays parseable. `perch-cli help --json` returns a machine-readable description of every
command, argument, option and exit code.

```json
{ "ok": true, "height_cm": 110.5, "target_cm": 110.5, "device": "Desk 9770", "via": "app" }
```

Exit codes:

| Code | Meaning |
|------|---------|
| 0 | success |
| 1 | bad usage or arguments |
| 2 | no matching desk is paired with Windows |
| 3 | could not connect; something else holds the desk's connection |
| 4 | the move failed, timed out, or was interrupted |

A failure with `--json` still prints an object: `{"ok": false, "error": "...", "code": 3}`.

## Package managers

### Scoop

```powershell
scoop bucket add eaglespirit https://github.com/eaglespirittech/scoop-bucket
scoop install perch
```

The manifest lives in [eaglespirittech/scoop-bucket](https://github.com/eaglespirittech/scoop-bucket).
Its Excavator workflow watches this repository's releases every four hours and commits new
versions and hashes by itself, so a release reaches Scoop users without anyone editing JSON.

### WinGet

`.github/workflows/winget.yml` opens the manifest-update pull request against
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) whenever a release is
published, under the identifier `EagleSpirit.Perch`. Two things have to be set up once:

1. **A token.** Create a *classic* personal access token with the `public_repo` scope
   (fine-grained tokens are not supported by the action) and save it as a repository
   secret named `WINGET_TOKEN`. Until that exists the workflow deliberately does nothing.
2. **The first submission.** The action updates a package that already exists in
   winget-pkgs, so version one goes in by hand:

   ```powershell
   winget install Microsoft.WingetCreate
   wingetcreate new https://github.com/eaglespirittech/perch/releases/download/v0.2.0/Perch-0.2.0-setup.exe
   ```

   Answer its prompts, let it submit the pull request, and wait for a maintainer to merge
   it. After that every release is automatic.

## Build

```
dotnet build Perch.csproj -c Release
dotnet build cli/PerchCli.csproj -c Release
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
self-contained single-file exe of the app and of the CLI, builds the Inno Setup installer
(which carries both, sharing one copy of the runtime), writes a SHA-256 sidecar for each,
and attaches everything to a generated GitHub release:

```
git tag v1.0.0
git push origin v1.0.0
```

The tag must look like `v1.2.3` or the workflow stops before building. CI builds the
installer on every push too, so a broken `packaging/perch.iss` shows up before a release
rather than during one.

The app icon in `assets/perch.ico` is generated from the same code that draws the tray
icon at runtime. After changing `AppIcon.Draw`, regenerate it with:

```
dotnet run --project tools/IconGen
```
