# Milestone 2 local review

Date: November 26, 2026. Scope: timer and system tray only. Windows x64; .NET SDK 10.0.404. No system software installed, commits made, or pushes performed.

## Existing work and Milestone 1 follow-up

Read the actual root AGENTS.md, README, global.json, source, workflow, and Milestone 1 review before editing. The repository was on main with no commits and all Milestone 1 files untracked; that existing work was preserved and extended. The origin was already the requested GitHub URL. The sandbox reported Git dubious ownership; a command-scoped `-c safe.directory=...` enabled read-only status without changing global Git configuration.

Before implementation, launched the Milestone 1 app and attempted native sizing. The available controls did not establish a verifiable minimum size. Actual Windows text/display scaling was not changed. After implementing the successor layout, verified that actual MainWindow at 360 × 360 through a disposable layout host, then with its content transformed to 150%. This host referenced the Release assemblies and was outside the repository; it did not create a tray adapter. Verified title/status wrapping, the longest 240:00 display, wrapping controls/labels, access keys, Tab/Shift+Tab, and scrolling down to the full medical notice/footer. These checks supersede the old layout's minimum-size gap for the current UI; the original Milestone 1 binary was not verified at minimum size. Actual OS text/DPI scaling and a real screen-reader session remain outstanding. The temporary host is removed after review.

## Implemented

- Core ReminderTimer with injected TimeProvider monotonic timestamps; Ready, Running, Paused, Due; elapsed-time reconciliation independent of UI ticks.
- Start/pause/resume/reset, session-only 20/30/60/custom whole-minute intervals (1–240), explicit validation, no automatic start/restart.
- Deadline/overshoot clamps to zero and emits one Due state transition per interval. Reset returns to Ready; another Start is explicit.
- MVVM commands and state; disabled invalid commands/settings; invalid text retained with an error, `--:--`, and disabled Start. The exact same commands are used by tray and window.
- Windows Forms NotifyIcon adapter with Open, Start/Resume, Pause, Reset, Exit, status, tooltip below 63 characters, and an original local eye icon. Explicit Hide leaves the app alive; Open calls Show/Activate on the original window. Main-window Close exits. Shutdown stops/detaches the dispatcher refresh and disposes tray resources.
- Accessible control names, access keys, wrapping layout, polite significant-state/validation live regions, and a countdown with live announcements off.
- Core and view-model xUnit tests; Windows workflow now runs the full suite. README and AGENTS guidance updated.
- The 20-second break remains clearly marked future functionality. No SQLite, tracking/history, autostart, idle/lock detection, power-state policy, camera, AI, telemetry, balloons, sounds, overlay, forced focus, or global hotkey was added.

## Files changed or added

Updated: `EyeCare4U.slnx`, `.github/workflows/build.yml`, `AGENTS.md`, `README.md`, `src/EyeCare4U.App/EyeCare4U.App.csproj`, `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, and `ViewModels/MainWindowViewModel.cs`.

Added: `src/EyeCare4U.Core/ReminderTimer.cs`, `TimerState.cs`, `IntervalInput.cs`; `src/EyeCare4U.App/ViewModels/RelayCommand.cs`, `Platform/TrayIcon.cs`, `Assets/EyeCare4U.ico`; `tests/EyeCare4U.Core.Tests` project with ReminderTimerTests and IntervalInputTests; `tests/EyeCare4U.App.Tests` project with MainWindowViewModelTests; this review record. Appended a follow-up link to `docs/MILESTONE-1-REVIEW.md` without changing historical results. Removed only the newly generated xUnit placeholder test. Existing SDK policy and ignore rules were retained.

Dependencies: built-in Windows Forms Windows Desktop framework; tests only: xunit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, xunit.runner.visualstudio 3.1.4. No application NuGet package added; template coverage collector was omitted as unnecessary.

## Commands and automated results

- `dotnet --list-sdks`: installed SDK 10.0.404.
- `git status --short --branch` / `git remote -v`: original main/no-commit state and intended origin, with no user files removed. Initial sandbox status needed a command-scoped safe.directory exception.
- `dotnet new xunit --name EyeCare4U.Core.Tests --output tests/EyeCare4U.Core.Tests --framework net10.0 --no-restore`: scaffolded the Core test project. Replaced generated placeholder tests with behavioral coverage.
- `dotnet sln EyeCare4U.slnx add ...`: added Core.Tests and App.Tests.
- `dotnet restore EyeCare4U.slnx`: succeeded for all four projects.
- `dotnet build EyeCare4U.slnx --configuration Release --no-restore`: succeeded, 0 warnings, 0 errors.
- `dotnet test EyeCare4U.slnx --configuration Release --no-build --no-restore`: final result 52 passed, 0 failed, 0 skipped (39 Core, 13 view-model).
- First Core-only run: 39 passed. First expanded run: 2 view-model tests failed because their exact elapsed-time assertions used a real clock; corrected those tests to inject manual time, then reran successfully. No real waits are used in automated timing tests.

Core coverage includes selected intervals, no automatic start, fractional elapsed time, wall-clock independence, repeated pause/resume cycles, reset from Running/Paused/Due, just-before/exact/overshot deadline, one Due event per interval, explicit subsequent interval, invalid/repeated transitions, rejected interval changes, and parser bounds/invalid input. View-model coverage includes invalid text retention/no fallback Start, correction, presets, state-dependent command availability, locked settings, paused time, ceiling display, Due/reset/start, and no repeated state notification on display ticks.

No GitHub workflow run occurred. Hosted CI remains unverified.

## Desktop evidence

Launched Release executable with Start-Process. Observed Ready and 20:00 without auto-start. Chose Custom, entered 241, saw the error, `--:--`, and disabled Start; corrected to 1 and started. Confirmed Running and disabled interval controls. Paused at 00:56 and observed the same value later. Resumed, allowed the interval to finish, and saw Due / “Time for an eye break” / 00:00. It stayed due until Reset; Reset restored Ready and 01:00. Used the native Close button; process and window disappeared.

After accessibility fixes, launched again and confirmed the accessibility tree includes the numeric countdown (`Remaining interval time: 20:00`), named interval selector, and named custom input. Earlier tree snapshots sometimes cached old text after dispatcher changes while screenshots showed the current state; a real assistive-technology session is still required to validate announcements.

On the final build, Start then Hide removed the window from the visible-window inventory and left process 38340 alive/responding. The tools could not rehydrate the hidden window or expose a notification-area surface, so restoring it and inspecting its running countdown through the tray could not be completed. Cleanup used `taskkill /PID 38340`, which reported a termination signal sent; a subsequent process lookup found no EyeCare4U.App process. This is not counted as tray Exit verification.

The 360 × 360 and 150% content-scale simulation described above passed visually. This is separate from actual Windows scaling.

## Outstanding manual checks and limits

- Directly use all tray commands while watching the main window; verify state and enabled actions stay synchronized.
- Hide during a countdown, wait, restore using tray Open, and verify time continued and the same window/timer remains. Repeat Open/double-click. Use tray Exit while visible and hidden.
- Visually confirm the tray icon disappears after both Close and Exit. Process termination through Close was checked; icon removal was not visually observed.
- Actual increased Windows text/display scaling, assistive technology/live announcements, and power-state scenarios. The 150% simulation does not replace OS scaling.
- Hosted CI requires a separately authorized push and actual GitHub run.

The architecture and cleanup paths were reviewed in source, but unperformed manual checks are not claimed as passing. UI refresh can be delayed while its dispatcher is busy. Timer time is not estimated or measured usage. There is no explicit suspend/resume policy (Milestone 3), and full reminders/break countdown belong to Milestone 4. Settings are not persisted.

## Launch and owner review

From PowerShell:

```powershell
Set-Location 'C:\Users\Brian\Documents\GitHub\EyeCare4U'
dotnet restore EyeCare4U.slnx
dotnet build EyeCare4U.slnx --configuration Release --no-restore
dotnet test EyeCare4U.slnx --configuration Release --no-build --no-restore
dotnet run --project src/EyeCare4U.App/EyeCare4U.App.csproj --configuration Release --no-build
```

Use Custom = 1 for a short review. Follow the numbered review steps in README, with particular attention to the outstanding tray/OS-scaling/screen-reader checks above. Milestone 3 has not been started. Do not commit or push without an explicit request.
