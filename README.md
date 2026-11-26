# EyeCare4U

A Windows desktop portfolio project for configurable eye-break reminders during work or gaming.

**Current status: Milestone 3.** The app has a reminder countdown, system tray integration, session-only estimated active PC time, and automatic holds for detected idle, lock, suspend, and monitoring uncertainty. It launches Ready, without automatically starting a reminder. The planned 20-second break is still a future feature. No camera, AI, database, persistent settings/history, autostart, per-app tracking, notifications, sound, overlays, or telemetry are implemented.

## Technology and structure

C#, .NET 10 LTS, WPF/XAML, and straightforward MVVM. No MVVM framework or service container.

- `src/EyeCare4U.Core`: reminder state machine; activity accounting and credited-activity clock; input/power counter validation; interval/threshold parsing. No WPF or Windows API dependency.
- `src/EyeCare4U.App`: views, view model, application lifecycle, Windows input/session/power adapter, and Windows Forms NotifyIcon adapter. Window and tray share one ActivitySession, reminder, and view model.
- `tests/EyeCare4U.Core.Tests`: deterministic timer, usage, policy, parsing, and counter tests.
- `tests/EyeCare4U.App.Tests`: view-model validation, commands, holds, and state-notification tests, without launching a window.
- `.github/workflows/build.yml`: Windows restore, Release build, and full tests. Check the corresponding GitHub Actions run independently of local results.
- `Assets/EyeCare4U.ico` in App: original application-owned eye icon, drawn locally with no downloaded branding.

No new NuGet dependency was introduced for Milestone 3. Windows Forms is a built-in Windows Desktop framework reference; Windows monitoring uses native APIs. Test dependencies remain xunit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, and xunit.runner.visualstudio 3.1.4.

## Prerequisites and commands

Use supported Windows (Windows 11 recommended) and **.NET SDK 10.0.404**, x64 on an x64 machine, including the Windows Desktop components from the .NET SDK installer. A runtime alone is insufficient. Visual Studio is optional; no system software is installed by this repository.

[Download .NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Microsoft's [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) lists .NET 10 LTS support through November 14, 2028, verified during Milestone 1. `global.json` selects 10.0.404 and allows newer stable patches in the 10.0.4xx feature band (`latestPatch`), without prerelease, feature-band, or major-version roll-forward. CI reads the same file. Update the baseline deliberately and validate locally and in CI together.

```powershell
Set-Location 'C:\Users\Brian\Documents\GitHub\EyeCare4U'
dotnet --version
dotnet restore EyeCare4U.slnx
dotnet build EyeCare4U.slnx --configuration Release --no-restore
dotnet test EyeCare4U.slnx --configuration Release --no-build --no-restore
dotnet run --project src/EyeCare4U.App/EyeCare4U.App.csproj --configuration Release --no-build
```

Restore can require NuGet access. Building the whole solution and running the App tests/UI require Windows; Core is UI-independent.

## Estimated activity: exact policy

“Estimated active PC time — this session” begins at the first reliable launch sample and ends when the app exits. It continues independently of Ready, Running, Paused, or Due. Totals and settings are not saved. Reopening the same hidden window does not begin a new session.

The input-idle threshold defaults to **120 seconds**. In Ready, enter a whole number from **30 through 900 seconds**. Whitespace is accepted; signs, fractions, overflow, and out-of-range values are rejected. Invalid text stays visible with an accessible error and the last applied threshold displayed; Start is disabled until corrected. Monitoring continues under the explicitly displayed applied setting. Valid changes settle the old policy, preserve accumulated totals, and establish a fresh baseline under the new policy. They never recalculate history. No threshold changes are allowed while Running (including held), Paused, or Due.

The pre-threshold grace period counts as estimated activity. With two reliable samples no more than five seconds apart, only the portion before the known idle cutoff is credited. For example, at 119.5 seconds idle with a 120-second threshold, a one-second sample credits 0.5 seconds. If new input arrives after a cutoff, only the union of the previous grace window and the window beginning at the latest input is credited. Input occurring earlier within the polling interval cannot be reconstructed; conservative undercounting is possible. The previous sample being active never justifies crediting the whole interval. Repeated samples cannot count time twice.

Locked, suspended, unavailable, and uncertain intervals are excluded. At detected lock/unlock or power boundaries, the unresolved interval since the previous sample is discarded, rather than guessing its active portion. Polling also queries current lock state to handle startup/recovery and missed lock notifications; a change observed only by polling discards that boundary interval. Recovery establishes a fresh baseline without backfilling unknown time. Starting already idle/locked never fabricates activity before launch.

**This is input-based estimation, not evidence of presence, screen use, looking away, or attention.** Reading, watching video, and controller-only gameplay may be classified as idle despite real use. Recent or injected input can overestimate activity. Lack of input does not establish that an eye break occurred. There are no controller hooks or camera features.

## Monitoring failures, counters, and gaps

A single one-second dispatcher cadence polls monitoring and refreshes both window and tray. Actions also settle a sample before changing timer intent. Elapsed durations use monotonic timestamps, not tick subtraction or wall-clock time.

- Native failure, unknown/disconnected session state, notification-registration failure, counter regression, or implausible input timing produces recoverable **Monitoring unavailable**. Totals and timer remaining are preserved; retries use the same cadence.
- A delay **up to five seconds** can be reconciled when samples remain consistent. A longer or negative monotonic gap is entirely excluded, displayed as unavailable, and followed by a fresh reliable baseline. A monitoring stall is not presumed to be active use.
- GetLastInputInfo's 32-bit tick is validated against 64-bit uptime across rollover. Regressed/future timestamps and input ages over 24 hours are treated as uncertain. After a discontinuity, fresh changed input may be required. At startup/recovery after 49.7 days uptime, unchanged input is ambiguous across a full wrap; monitoring waits for a changed input tick observed within a sample interval.
- Uptime and awake-only interrupt time are compared to help detect missed sleep notifications and discontinuities. A difference over 100 ms discards that unresolved interval; the allowance accounts for coarse counters/separate API calls. Sub-resolution transitions and platform-specific power behavior cannot be reconstructed perfectly.
- Resume discards previous baselines and re-evaluates lock/input state. Sleep duration is not added to activity or reminder time. Automatic resume is not evidence that a person is present. The additional user-resume message is ignored as a duplicate wake.

The fallback intentionally undercounts around failures, long UI stalls, and lifecycle events, rather than inventing activity. Real Windows lock, hibernate, sleep, Modern Standby, RDP, and recovery behavior still require owner-machine verification. No live status is claimed while the machine is actually suspended; a notice after wake reports baseline handling.

## Reminder states and automatic holds

| State | Actions | Behavior |
| --- | --- | --- |
| Ready | Configure; Start | Full selected interval, no running intent |
| Running | Pause; Reset | Armed by the user; consumes only credited activity |
| Paused | Resume; Reset | “Paused by you”; automatic recovery cannot resume it |
| Due | Reset | Zero; “Time for an eye break”; no automatic restart |

Choose 20, 30, 60, or a custom 1–240 whole-minute interval in Ready. Invalid interval input remains visible, displays `--:--`, and disables Start. Reset restores the full selected interval, returns Ready, and clears running intent. From Due, Reset and then Start are explicit user actions. Repeated invalid state commands do not restart the timer. Displayed seconds round upward so 00:00 does not appear before the credited-activity deadline.

Automatic blockers freeze the reminder's credited-activity clock; they do not impersonate a manual Pause. Running while held means **armed and waiting**, not consuming. Start/Resume while blocked is allowed and displays why it is waiting. Counting continues automatically only after all blockers clear while the user still intends Running. Unlocking while still idle cannot resume consumption; waking cannot override manual Pause. Inactivity never resets the interval or marks a break completed. Due remains Due through monitoring changes.

Core ReminderTimer still accepts injected time and is independently tested. In the application its time source is the ActivitySession clock, which advances only by conservatively credited activity; Windows APIs stay outside Core.

## Window, tray, and accessibility

Hide to tray keeps monitoring and an armed reminder alive, subject to automatic holds. The existing window is restored by tray Open or double-click. The shared tray menu provides status, estimated usage, Open, Start/Resume, Pause, Reset, and Exit. Tooltips are capped at 63 characters. Only a user Open request activates the window; Due does not force focus.

Main-window Close exits the app. Shutdown stops/detaches polling, unsubscribes native hooks, unregisters session/power notifications before window destruction, and disposes tray resources. No hooks or polling remain after exit.

Controls support Tab/Shift+Tab, scrolling, and access keys: Alt+S Start, Alt+P Pause, Alt+R Resume, Alt+E Reset, Alt+M interval, Alt+C custom minutes, Alt+H Hide, and Alt+T idle threshold. Labels/settings have accessible names and visible validation. Status/error changes are polite live regions; countdown and estimated-time values are not live regions. No announcements are deliberately emitted every second. Minimum window size is 360 × 360; content scrolls and controls/labels wrap. Actual Windows scaling and screen-reader behavior remain unverified.

## Practical manual review

1. Launch and confirm Ready/20:00, estimated activity beginning near zero, monitoring status, and default 120-second threshold. Leave the timer Ready and confirm estimated time can accumulate.
2. Try thresholds `29`, `901`, `1.5`, and text; confirm visible errors, the unchanged applied threshold, and disabled Start. Correct to `30`; verify prior totals remain. Set Custom interval to `1`.
3. Start and provide keyboard/mouse input. Stop input longer than 30 seconds. Confirm Waiting for activity, frozen remaining time, and frozen estimated total after the grace period. Provide input and confirm automatic continuation without resetting.
4. Pause manually. Provide input and confirm estimated activity can grow while the reminder remains Paused by you. Repeat through owner-controlled lock/unlock and sleep/wake; never rely on simulation as proof of Windows behavior.
5. During a run, lock, wait, unlock; check no locked time was credited. Repeat with sleep/hibernate/Modern Standby as available. After wake, verify a fresh baseline, no sleep jump, no immediate Due, and no auto-resume from manual Pause. Test overlapping idle/lock conditions. Start/Resume while blocked should arm and wait.
6. Reach Due using actual active time, then confirm input/unlock/recovery cannot restart it. Reset restores Ready/full interval. Settings return to defaults on a new app launch.
7. Exercise tray commands against the visible window. Hide during a run, wait, restore, and verify the same session/window and policy-aware countdown. Repeat Open/double-click, Close, and tray Exit (including Exit while hidden); visually check icon removal.
8. Check the new section at minimum window size, actual increased Windows text/display scaling, keyboard-only operation, and a screen reader. An owner-controlled delayed UI/debugger pause can exercise the five-second gap fallback; do not interpret that simulation as sleep verification.

Preserved Milestone 2 outstanding checks: direct tray synchronization; hidden countdown/restoration; tray Exit and visual icon removal; actual Windows scaling; screen-reader behavior. See [Milestone 3 review](docs/MILESTONE-3-REVIEW.md) for current automated/manual evidence, limitations, and publication details. Hosted CI must be checked on the actual pushed commit.

## Seven-milestone roadmap

1. Desktop foundation — implemented.
2. Timer and system tray — implemented; outstanding manual checks preserved.
3. Session-only estimated usage and idle/lock/sleep-aware timing — implemented; platform checks distinguished from deterministic tests.
4. Fuller reminders — future: 20-second break, notifications, sounds/overlays with user control and accessibility.
5. Local persistence/preferences — future: SQLite, minimal history, retention/deletion, optional autostart.
6. Release readiness/evidence — future: packaging, reliability/accessibility, carefully sourced research, owner-selected license.
7. Optional computer vision — future only: explicit opt-in, local processing, consent, and documented limits. No camera or AI scaffolding before this milestone.

## Evidence, privacy, and API references

The 20-minute default and planned 20-second break are configurable reminders, not medical treatment or a medically optimal schedule. Break-timing recommendations and research evidence will be documented carefully in a later milestone; this app makes no medical-effectiveness claim.

**Implemented:** local session-only input-age/lock/power processing and an accumulated duration. No keystrokes, mouse coordinates, screenshots, foreground titles, process inventories, or browsing history are recorded. Session-query buffers are freed immediately; only connection/lock flags are read into managed code. Nothing is persisted or sent as application telemetry. Development tools/GitHub Actions have their own network behavior. No camera features existed in Milestones 1–2 or exist now.

**Future intentions:** continue minimal collection/local processing, explain any future retained data and deletion controls before introducing them, and describe usage values as estimates. Persistent privacy controls and optional camera processing are not implemented promises.

Microsoft documentation consulted for the adapter and conservative policy:

- [GetLastInputInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo): current-session scope and non-monotonic input timestamps.
- [GetTickCount64](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-gettickcount64) and [QueryUnbiasedInterruptTime](https://learn.microsoft.com/en-us/windows/win32/api/realtimeapiset/nf-realtimeapiset-queryunbiasedinterrupttime): elapsed counters and awake-only time.
- [WTS session query](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsquerysessioninformationw), [extended session flags](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/ns-wtsapi32-wtsinfoex_level1_w), and [session notification registration](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification). Modern supported Windows flag meanings are used, not Windows 7's reversed flags.
- [Power notification registration](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registersuspendresumenotification), [power messages](https://learn.microsoft.com/en-us/windows/win32/power/wm-powerbroadcast), and [automatic resume limitations](https://learn.microsoft.com/en-us/windows/win32/power/pbt-apmresumeautomatic).

No release/license is selected. The owner will choose a license before public release. Standing milestone commit/push authorization and safety/review requirements are recorded in AGENTS.md. No work beyond Milestone 3 is included.
