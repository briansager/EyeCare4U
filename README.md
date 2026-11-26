# EyeCare4U

A Windows desktop portfolio project that helps users remember eye breaks during work or gaming.

**Current status: Milestone 2 implemented locally.** A session-only countdown and system tray integration are available. The app starts Ready and waits for an explicit Start. Timer time is elapsed reminder time, not measured screen usage. The 20-second break countdown remains a future feature. No camera features, AI dependencies, tracking, storage, notifications, sounds, overlays, or autostart exist in this milestone.

## Technology and structure

- C#, .NET 10 LTS, WPF/XAML, and straightforward MVVM; no MVVM framework or service container.
- `EyeCare4U.slnx`: App, Core, and two test projects.
- `src/EyeCare4U.Core`: UI-independent countdown state machine and whole-minute input validation. Uses injected `TimeProvider` timestamps; no WPF or Windows dependency.
- `src/EyeCare4U.App`: views, view model, commands, application lifecycle, and a small Windows Forms `NotifyIcon` adapter. Window and tray use the same view model and Core timer instance.
- `src/EyeCare4U.App/Assets/EyeCare4U.ico`: original application-owned eye icon, drawn locally, with 16/32/48-pixel images. No downloaded branding.
- `tests/EyeCare4U.Core.Tests`: deterministic timer and input-parser behavior tests.
- `tests/EyeCare4U.App.Tests`: view-model command, validation, and state-notification tests; Windows target, without launching a window.
- `.github/workflows/build.yml`: Windows restore, Release build, and tests on pushes and pull requests. Hosted CI results must be checked separately on GitHub; a local build does not verify hosted CI.

Application code has no additional NuGet packages. Windows Forms is a Windows Desktop framework reference. Tests use xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, and xunit.runner.visualstudio 3.1.4. SQLite is deferred.

## Prerequisites and SDK policy

Use a supported Windows installation (Windows 11 recommended) and **.NET SDK 10.0.404**, x64 on an x64 machine, including Windows Desktop targeting/runtime components supplied by the .NET SDK installer for Windows. A runtime alone is insufficient. The command-line SDK is sufficient; Visual Studio is optional. No system software is installed by this repository.

[Download the .NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Microsoft lists .NET 10 as an active LTS release supported through November 14, 2028 in its [official support policy](https://dotnet.microsoft.com/en-us/platform/support/policy), verified November 26, 2026 during Milestone 1.

`global.json` selects SDK `10.0.404`, permits newer stable patches within the **10.0.4xx feature band** (`latestPatch`), and disallows prerelease SDKs. It does not advance to another feature band or major version. Keep current with supported patches. CI reads the same file using `actions/setup-dotnet`; update the baseline deliberately and validate locally and in CI together.

```powershell
Set-Location 'C:\Users\Brian\Documents\GitHub\EyeCare4U'
dotnet --version
dotnet restore EyeCare4U.slnx
dotnet build EyeCare4U.slnx --configuration Release --no-restore
dotnet test EyeCare4U.slnx --configuration Release --no-build --no-restore
dotnet run --project src/EyeCare4U.App/EyeCare4U.App.csproj --configuration Release --no-build
```

The SDK must resolve to 10.0.404 or a later stable patch in that feature band. Restore can require NuGet access. Building the solution and running the App tests/UI require Windows. Core is UI-independent.

## Timer behavior

| State | Available actions | Remaining time |
| --- | --- | --- |
| Ready | Choose interval; Start | Full selected interval |
| Running | Pause; Reset | Monotonic elapsed time reduces remaining time |
| Paused | Resume; Reset | Preserved until Resume |
| Due | Reset | Zero; “Time for an eye break” |

Choose 20, 30, or 60 minutes, or Custom with a whole number from 1 through 240. Whitespace around a whole number is accepted. Fractions, signs, out-of-range values, nonnumeric input, and overflow are rejected. Invalid custom text remains visible with an error; the countdown shows `--:--` and Start is disabled in both the window and tray. No fallback interval is silently started.

Interval changes are allowed only in Ready. Reset restores the full selected interval and returns to Ready without starting. From Due, explicitly Reset and then Start to begin another interval. Repeated or invalid Core state commands return false and do not restart the interval; repeated Reset is harmless. Pause at or beyond the deadline transitions to Due instead of pausing at zero. A Due state change is emitted once per interval. Core callers use `Refresh()` to reconcile elapsed deadlines; reading `Remaining` clamps at zero without firing state events.

`TimeProvider.System` supplies monotonic timestamps; elapsed time is computed from those timestamps. The WPF dispatcher refreshes the display/state every 250 ms rather than subtracting a second per tick. A busy UI may delay the visible update, but the next refresh reconciles elapsed time. Displayed seconds round upward so `00:00` is not shown before the deadline. Core has no scheduler or disposable timer resources; application exit stops and detaches the dispatcher refresh.

**Limits:** settings are session-only and return to 20 minutes at the next launch. Time while the app is closed is not restored. There is no explicit suspend/resume, idle, lock, or activity policy; those belong to Milestone 3. Do not interpret timer time as screen use, presence, looking away, or attention. OS/power-state behavior has not been validated. No automatic restart, break countdown, notification balloon, sound, forced focus, overlay, or global hotkey is provided.

## Window and tray

**Hide to tray** hides the existing window while the app and countdown remain active. Use the icon's context menu: Open EyeCare4U, Start or Resume, Pause, Reset, and Exit. Commands are enabled according to the shared state. Due and remaining time appear in the tray status/tooltip. Double-clicking the icon also restores the existing window. The tooltip remains below 63 characters. Open only activates the window after a user request; reaching Due does not force focus.

The main window's Close button exits, even while running. Tray Exit shuts down the application. Shutdown disposes the icon, menu, and icon image and stops the dispatcher refresh. A new app launch creates a new session; opening the tray window does not create a new timer.

Controls support Tab/Shift+Tab and access keys (Alt+S Start, Alt+P Pause, Alt+R Resume, Alt+E Reset, Alt+M interval, Alt+C custom input, Alt+H Hide). Scroll or use Page Up/Page Down and Ctrl+Home/Ctrl+End when focus is outside an input. Content scrolls vertically at the 360 × 360 minimum window size; buttons wrap. Status changes are polite live-region events while visible; the countdown is not a live region and is not deliberately announced every second. A screen-reader session remains an outstanding manual check.

## Review steps and validation status

1. Launch using the commands above; confirm Ready and 20:00 without automatic start.
2. Try presets, then Custom. Enter `0`, `241`, `1.5`, and text; confirm the error and disabled Start. Correct it to `1`.
3. Start, wait a few seconds, Pause, wait, Resume, and Reset. Confirm settings are locked while Running/Paused/Due and Reset does not start.
4. Run a one-minute interval to zero; confirm Due stays at zero. Explicitly Reset and Start for another interval.
5. Right-click the tray icon (possibly in Windows' hidden-icons area). Exercise tray commands while watching the window. Hide during a run, wait, and use Open to restore; confirm elapsed time continued and no second window/timer appeared. Repeat Open/double-click several times.
6. Exercise both the window Close button and tray Exit on separate launches, including Exit while hidden. Confirm the process terminates and its icon disappears.
7. Check minimum size, keyboard access, actual increased Windows text/display scaling, and a screen reader.

Local results: restore and Release build passed with 0 warnings/errors; **52 tests passed** (39 Core, 13 view model). Window controls, invalid input, paused display stability, Due, Reset, Close/process termination, and Hide/process survival were checked. The actual window was also displayed in a temporary layout-only review host at 360 × 360 and with a 150% content-scale simulation; wrapping and keyboard reachability passed. That host was not the tray application, and its simulation is not actual Windows text/DPI scaling.

Outstanding: direct tray-menu synchronization, observing countdown progress while hidden/restored through the tray, repeated Open/double-click, tray Exit, visual icon removal, actual Windows scaling, and screen-reader announcements. The available desktop tools expose app windows but not the notification-area surface. Hide was observed; the hidden test process was then closed with a termination signal because it could not be restored through those tools. Check the corresponding GitHub Actions run separately for hosted CI status. See [Milestone 2 review](docs/MILESTONE-2-REVIEW.md) for evidence and limitations.

## Seven-milestone roadmap

1. **Desktop foundation (implemented):** App/Core separation, MVVM shell, SDK policy, documentation, Windows build workflow.
2. **Timer and system tray (implemented locally):** countdown, start/pause/resume/reset, session-only intervals, tray adapter, deterministic tests. Manual tray checks listed above remain outstanding.
3. **Estimated usage and power-state policy (future):** conservative usage estimates, inactivity/lock limitations, explicit suspend/resume behavior. Input inactivity cannot establish absence, looking away, or attention.
4. **Fuller break reminders (future):** 20-second break feature, notifications, optional sound/overlay behavior with user control and keyboard accessibility.
5. **Local persistence and preferences (future):** SQLite, saved settings, minimal local history, retention/deletion controls, optional autostart.
6. **Release readiness and evidence (future):** accessibility/reliability review, packaging, carefully sourced timing/research documentation, owner-selected license before public release.
7. **Optional computer vision exploration (future):** only here consider opt-in local camera processing, consent, privacy controls, and documented limitations. No earlier camera or AI scaffolding.

## Timing, evidence, and privacy

The default 20-minute interval and planned 20-second break are configurable reminders, not medical treatment or a medically optimal schedule. Recommended break timing and research evidence will be documented carefully in a later milestone. No research conclusions or medical-effectiveness claims are established here.

**Implemented now:** reminder timing and session-only settings run locally. The app does not collect input activity, estimate screen use, access a camera, store history/settings, or send telemetry. It contains no application networking or database code. Development tools and GitHub Actions have their own network behavior. No camera features existed in Milestone 1, and none exist in Milestone 2.

**Future intentions:** prefer local processing and minimal data collection, and explain measurements, retention, and deletion before introducing collection. Future usage values must be described as estimates; inactivity does not prove absence or looking away. Computer vision is deferred to Milestone 7. These future privacy controls are not implemented features.

No release is published and no license has been selected. The owner will choose a license before public release. The owner has authorized routine milestone commits and pushes after validation and a staged-content review; see AGENTS.md. Documented outstanding manual checks remain distinct from automated validation.
