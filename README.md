# Perch

Sit/stand desk control for the IKEA IDÅSEN. A small app for **Windows and macOS** that
drives the desk to an exact height over Bluetooth LE, with presets and an hourly stand
schedule. Type a height in cm, press Go. The `perch-cli` command line tool comes with it
on both.

Not affiliated with or endorsed by Inter IKEA Systems B.V. IDÅSEN is their trademark;
it appears here only to say which desk this controls.

<!-- Renders as a player on github.com only; elsewhere this block shows nothing. The
     asset is hosted by GitHub (uploaded via issue #1) rather than committed here. -->
<video src="https://github.com/user-attachments/assets/eb18d4ef-208f-42ef-8afd-a6ed73128190" controls></video>

### Windows

- **Installer** — `Perch-<version>-setup.exe` from the
  [releases page](https://github.com/eaglespirittech/perch/releases). Installs per user, so
  there is no UAC prompt, and it adds a Start Menu entry, an uninstall entry and the
  `perch-cli` command line tool.
- **Portable** — `Perch-<version>-win-x64.exe` and `perch-cli-<version>-win-x64.exe` are
  the same two programs as single self-contained files. Nothing needs the .NET runtime.
- Settings (chosen device, presets, schedule) live in `%APPDATA%\Perch\settings.json`.
  A settings file from the app's earlier name is picked up automatically on first run.

### macOS (12 Monterey or later)

- **Disk image** — `Perch-<version>-macos-arm64.dmg` for Apple silicon, or
  `-macos-x64.dmg` for Intel Macs. Drag Perch to Applications.
- **CLI only** — `perch-cli-<version>-macos-<arch>.tar.gz` holds `perch-cli` and
  `perch-ble`, its Bluetooth helper. Keep the two together, anywhere on your PATH.
- Settings live in `~/Library/Application Support/Perch/settings.json`.

Unless a release was signed with a Developer ID (see [Releasing](#releasing)), macOS
refuses to open it the first time. Right-click Perch in Applications and choose **Open**,
or clear the quarantine flag once:

```bash
xattr -dr com.apple.quarantine /Applications/Perch.app
```

## Before first use

**Windows**

1. Pair the desk in **Settings → Bluetooth & devices → Add device → Bluetooth**.
   Hold the small pairing button on the control box under the desktop until its LED
   blinks, then pick the desk from the list.
2. Close the IKEA *Desk Control* phone app. The desk accepts one Bluetooth connection
   at a time, and whoever holds it wins.

**macOS**

1. There is nothing to pair in System Settings: Perch finds the desk by scanning. Press a
   button on the desk to wake it, then pick it from the menu's **Desk** list. If macOS
   asks to pair, hold the pairing button on the control box and accept.
2. The first time, macOS asks whether Perch may use Bluetooth; allow it. (It lives under
   **System Settings → Privacy & Security → Bluetooth** after that.)
3. Close the IKEA *Desk Control* phone app, as above.

## Using it

The window follows the system light/dark setting and your accent colour, on either OS.
Set `PERCH_THEME=dark` or `PERCH_THEME=light` to override it.

- **Height** - the big readout is where the desk is now, next to a small drawing of the desk
  that rises and falls with it; the bar under it shows where that sits in the desk's
  62-127 cm travel.
- **Move to** - type a height, or step it with the minus and plus buttons, then press Go
  (Enter works too). **Stop** halts a move in progress.
- **Presets** - two slots; press one to move there. "Save current height" captures the
  height the desk is at now.
- **Menu** (the dots, top right) — pick which desk to use, connect or disconnect,
  nudge the desk a centimetre either way, toggle **Start with Windows** / **Open at
  Login**, or open the settings folder.

## Closing: the notification area and the menu bar

Closing the window parks Perch in the notification area (Windows) or the menu bar (macOS)
rather than quitting, because the schedule only runs while the app does. From there:

- Click the icon, or open its menu and choose **Open Perch**, to bring the window back.
  On Windows its tooltip carries the current height. On macOS, clicking Perch in the
  Dock works too.
- **Quit Perch**, on that same menu or on the window's own menu, quits for real (as does
  ⌘Q on macOS).

Windows often files a new tray icon under the "Show hidden icons" chevron; drag it out
onto the taskbar to keep it in sight.

## Start at sign-in

**Windows — Start with Windows.** The menu item writes a per-user entry under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, so it needs no admin rights and
shows up in Task Manager's Startup tab, where it can also be disabled. Moving `Perch.exe`
does not break it: on each launch the entry is rewritten to point at wherever the exe is
running from. The entry starts Perch with `--minimized`, so signing in leaves it waiting
by the clock instead of opening a window.

**macOS — Open at Login.** The menu item writes a per-user LaunchAgent,
`~/Library/LaunchAgents/com.eaglespirit.perch.plist`, which starts Perch.app minimised
into the menu bar. It needs no admin rights and appears under **System Settings →
General → Login Items**, where it can be switched off. Moving Perch.app is handled the
same way as on Windows.

## Uninstalling

**Windows.** Uninstall Perch from **Settings → Apps → Installed apps**. That also removes
the **Start with Windows** entry and, if you chose it, `perch-cli` from PATH. Your
settings stay in `%APPDATA%\Perch`; delete that folder too if you want them gone. The
portable exes install nothing: switch off **Start with Windows** in the menu, then delete
the files.

**macOS.** Switch off **Open at Login** in Perch's menu (or delete
`~/Library/LaunchAgents/com.eaglespirit.perch.plist`), quit Perch, and drag Perch.app to
the Bin. Settings live in `~/Library/Application Support/Perch`, and remove the
`/usr/local/bin/perch-cli` link if you made one.

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
  (or **Open at Login**) from the menu so it always is; there is no separate service.

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

The same command line tool on both systems.

- **Windows** — the installer puts `perch-cli.exe` next to the app and offers to add it
  to PATH, so it works from any terminal. It is also published on its own as a portable
  exe.
- **macOS** — it ships inside Perch.app; link it onto your PATH once:

  ```bash
  sudo ln -sf /Applications/Perch.app/Contents/MacOS/perch-cli /usr/local/bin/perch-cli
  ```

  Or use the standalone tarball. When the CLI drives the desk itself (the app is not
  running, or `--direct`), macOS asks the terminal you run it from for Bluetooth
  permission the first time.

```
perch-cli list                    # desks; the saved desk is marked *
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
to it over a per-user named pipe (a Unix domain socket on macOS) that only the same user
can connect to. When the app is not running, the CLI drives the desk
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
| 2 | no matching desk was found (not paired on Windows, not in range on macOS) |
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

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

The Windows programs and installer published on the
[releases page](https://github.com/eaglespirittech/perch/releases) are signed, from the
first release signed this way onwards. Only files built from this repository by its
[release workflow](.github/workflows/release.yml), on GitHub-hosted runners, are submitted
for signing: `Perch.exe`, `perch-cli.exe`, Perch's own `.dll` files and the installer.
Third-party components, such as the .NET runtime and Avalonia, are shipped as their
authors built them.

**Team roles**

- Committers and reviewers: [amir-khatibzadeh](https://github.com/amir-khatibzadeh)
- Approvers: [amir-khatibzadeh](https://github.com/amir-khatibzadeh)

Changes from anyone outside that list are reviewed by a committer before they are merged,
and every signing request is approved by hand.

**Privacy policy**

This program will not transfer any information to other networked systems unless
specifically requested by the user or the person installing or operating it. Perch talks
only to your desk, over Bluetooth, and makes no network connections of its own.

## How the code is organised

One codebase for both systems. Everything that can be shared is, and the few things that
cannot sit behind interfaces with one implementation per OS.

```
src/
  Perch.Core/        net9.0, no platform APIs. The Linak protocol and move loop
                     (DeskController), the session logic the window drives
                     (DeskSession), schedule, settings, the app/CLI pipe, and the
                     interfaces below.
  Perch.Platform/    One IPlatform per OS, chosen at build time:
    Windows/           WinRT Bluetooth, the Run registry key, Explorer
    MacOS/             CoreBluetooth through perch-ble, a LaunchAgent, Finder
  Perch.App/         The window, tray/menu bar icon and schedule editor, in Avalonia
                     (MIT licensed, runs on both).
  Perch.Cli/         perch-cli.
native/macos/perch-ble/
                     A small Swift program wrapping CoreBluetooth, spoken to over JSON
                     lines on stdin/stdout. .NET has no CoreBluetooth binding of its own.
tests/Perch.Tests/   Schedule, scheduler, protocol and the full move loop, the last
                     against a simulated desk. Runs on either OS.
```

The seams are `IBluetooth` / `IGattConnection` / `IGattCharacteristic` (find a desk,
connect, read, write and subscribe to a characteristic), `IAutoStart`, and `IPlatform`,
which gathers them. The app and the CLI target both `net9.0-windows10.0.19041.0` and
`net9.0`; the first gets the Windows implementation, the second the macOS one.

**No desk handy?** `PERCH_SIMULATOR=1` swaps in a simulated desk that speaks the real
protocol, for both the app and the CLI. It keeps its own settings folder and pipe, so it
never touches a real Perch that is running.

## Build

Needs the [.NET 9 SDK](https://dotnet.microsoft.com/download). On a Mac, also the Xcode
command line tools (`xcode-select --install`): the normal build compiles `perch-ble` too.

```bash
dotnet build Perch.sln -c Release
dotnet run --project tests/Perch.Tests -c Release
```

Run the app or the CLI on the machine you are on:

```bash
dotnet run --project src/Perch.App -f net9.0-windows10.0.19041.0    # Windows
dotnet run --project src/Perch.App -f net9.0                        # macOS
dotnet run --project src/Perch.Cli -f net9.0 -- status              # the CLI, on macOS
```

Package a release locally, into `dist/`:

```bash
pwsh packaging/windows/build.ps1 -Version 1.2.3        # installer + portable exes
packaging/macos/build.sh 1.2.3 osx-arm64               # .dmg + CLI tarball (or osx-x64)
```

`tests/Perch.Tests` is a plain console app that exits with the number of failed checks,
so CI needs no test framework.

## Releasing

`.github/workflows/ci.yml` builds the whole solution with warnings as errors, runs the
tests and packages the app on **both** a Windows and a macOS runner, for every push and
pull request. A broken installer script or app bundle shows up there rather than halfway
through a release.

`.github/workflows/release.yml` fires on a version tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

It re-runs the tests, then builds in parallel:

| Runner  | Artifacts |
|---------|-----------|
| Windows | `Perch-<v>-setup.exe`, `Perch-<v>-win-x64.exe`, `perch-cli-<v>-win-x64.exe` |
| macOS   | `Perch-<v>-macos-arm64.dmg`, `Perch-<v>-macos-x64.dmg`, `perch-cli-<v>-macos-{arm64,x64}.tar.gz` |

and publishes them all, each with a SHA-256 sidecar, as one generated GitHub release. The
tag must look like `v1.2.3` or the workflow stops before building.

**Signing on macOS.** Without secrets, the macOS builds are ad hoc signed: they run, but
macOS shows the first-launch prompt described under the macOS install. To sign with a
Developer ID and notarise instead, add these repository secrets:

| Secret | Value |
|--------|-------|
| `MACOS_CERTIFICATE` | base64 of a *Developer ID Application* certificate exported as .p12 |
| `MACOS_CERTIFICATE_PASSWORD` | the .p12's password |
| `MACOS_SIGN_IDENTITY` | e.g. `Developer ID Application: Eagle Spirit (TEAMID)` |
| `MACOS_NOTARY_APPLE_ID` | the Apple ID that notarises |
| `MACOS_NOTARY_PASSWORD` | an app-specific password for it |
| `MACOS_NOTARY_TEAM_ID` | the team id |

**Signing on Windows.** Through [SignPath Foundation](https://signpath.org), free for open
source; see [Code signing policy](#code-signing-policy). Once the project is set up there,
the release workflow signs the programs, builds the installer from them, signs that too,
and checks every signature, waiting at each step for an approver to approve the request in
SignPath. Until then it ships unsigned, as before. Setting it up:

1. Apply at [signpath.org/apply](https://signpath.org/apply). Once accepted, SignPath
   creates the organization; turn on multi-factor authentication there and on GitHub.
2. In SignPath, create a project with the slug `perch` for this repository, add the
   predefined **GitHub.com** trusted build system and link it to the project, and install
   the SignPath GitHub App on this repository.
3. Add two artifact configurations, pasting in
   [`binaries.xml`](packaging/windows/signpath/binaries.xml) as `binaries` and
   [`installer.xml`](packaging/windows/signpath/installer.xml) as `installer`.
4. Create a signing policy with the slug `release-signing` using the certificate
   SignPath Foundation provides, with yourself as approver.
5. Create an API token for a CI user that may submit to that policy, and add it to this
   repository as the secret `SIGNPATH_API_TOKEN`. Add the organization id as the
   repository **variable** `SIGNPATH_ORGANIZATION_ID`. (Different slugs go in the variables
   `SIGNPATH_PROJECT_SLUG` and `SIGNPATH_SIGNING_POLICY_SLUG`.)

The app icons, `assets/perch.ico` and `assets/perch.icns`, are generated from the same
geometry the app draws its icon with at runtime. After changing `AppIconShape`,
regenerate them with:

```bash
dotnet run --project tools/IconGen
```
