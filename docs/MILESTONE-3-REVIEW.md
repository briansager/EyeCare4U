# Milestone 3 review: estimated activity and automatic holds

Date: November 26, 2026. Scope: Milestone 3 only. Baseline inspected: `4bfff46c1723396bf04986cb53eb9a53e7b795bf` on main, clean working tree, matching origin/main. Read AGENTS.md, README, both existing milestone review records, SDK configuration, actual timer/view-model/platform source, and existing tests before editing. No unrelated user changes were present. Standing milestone publication authorization remains in AGENTS.md.

## Implemented and significant design choices

- Session-only estimated active PC time starts with the first reliable launch sample, independently of reminder Ready/Running/Paused/Due. No activity is inferred before launch or restored from a previous app process.
- ActivitySession in Core owns a credited-activity TimeProvider and the existing ReminderTimer. The credited clock advances only by accepted activity; the reminder's explicit states retain user intent. The original timer guarantees and 52 baseline tests are retained.
- IActivitySource is the small platform boundary. WindowsActivitySource owns native input/session/power reading and HWND subscriptions; Core contains no WPF or Windows APIs. The application uses one session/view model/timer for window and tray.
- A one-second dispatcher cadence performs sampling and visible refresh. User commands settle monitoring before changing timer intent, preventing activity before Start or after Pause from being charged to the reminder. Native sampling latency participates in gap detection.
- Threshold defaults to 120 seconds; session-only whole-second input 30–900, editable only in Ready. A valid change settles the old policy, keeps totals, and establishes a fresh baseline prospectively. Invalid text is retained, never applied, with accessible error and the old applied setting shown. Start is disabled until corrected; monitoring continues under the applied valid threshold.
- Idle/lock/uncertainty/power holds freeze the credited clock while an interval remains armed Running. Manual Pause is separate and always wins recovery. Start/Resume while blocked arms and waits; Reset clears intent and restores Ready/full interval. Due never restarts because of input, unlock, wake, or recovery. Inactivity never resets the interval or completes an eye break.
- Updated monitoring section and tray status show estimates and reasons; settings, totals, and all other data are session-only. Numeric estimates/countdown are not live regions; significant status/errors use polite announcements only on actual property changes. Alt+T accesses the idle-threshold field, avoiding Start's access key.
- Cleanup stops/detaches the dispatcher and unregisters/removes native hooks before window destruction; OnExit is an idempotent fallback. Tray resources retain their existing disposal behavior.

## Exact counting, failure, and resume policies

For two consistent, unlocked samples separated by at most five seconds, credit the known pre-threshold grace portion. Crossing from 119.5 to 120.5 idle seconds at the default threshold credits 0.5 seconds. If the last-input timestamp changes, credit the union of the old grace window and the window beginning at the latest observed input, without double-counting overlap. For an old cutoff at t=1 and a new input at t=4 in a five-second sample, credit two seconds, not five. Unknown earlier input history within the interval is not reconstructed, so undercounting is possible.

Startup and recovery establish baselines without backfilling. Failure, lock/unlock, and power boundaries conservatively discard the unresolved interval since the preceding sample. Polling re-queries session lock state, and lock-state transitions seen only by polling also discard their boundary interval. Locked and suspended samples add no time. Estimated time still grows during Ready/Paused/Due when monitoring considers the session active.

A gap greater than five seconds, negative monotonic delta, invalid age, inconsistent input-age progression, or input timestamp regression produces Monitoring unavailable and discards the entire unresolved interval. The next reliable sample is a baseline only. An ordinary consistent delay of up to five seconds is reconciled using actual elapsed time and cutoff boundaries. No one-second-per-tick subtraction is used, and wall-clock changes are irrelevant.

The adapter validates 32-bit last-input ticks against 64-bit uptime, including rollover. Future, regressed, or older-than-24-hour input timing is uncertain. After a discontinuity, fresh input can be required. At uptime of at least one full 32-bit wrap (49.7 days), startup/recovery cannot disambiguate an unchanged tick, so it waits for a changed tick observed within the sampled interval. These conservative exclusions preserve totals and do not silently become active readings.

Uptime versus awake-only interrupt-time deltas detect missed sleep notifications or counter discrepancies over 100 ms; uncertainty is excluded. The 100 ms allowance accommodates resolution and separate API calls. Native/API boundary precision is finite; sub-resolution transitions and device-specific power behavior are not proven by this heuristic. Resume clears baselines and queries lock/input again; sleep is never backfilled, and the delivered resume does not itself establish activity. PBT_APMRESUMEAUTOMATIC is handled, with the following PBT_APMRESUMESUSPEND message ignored as a second wake. Duplicate signals/samples cannot add time twice. No live suspended-machine display is claimed; a post-wake notice reports baseline handling.

Reading, video, and controller-only play can be classified as idle. Recent/injected input can overestimate real activity. Neither input inactivity nor this estimate establishes presence, looking away, attention, or eye rest. No controller hooks or camera access were added.

## Files and dependencies

Added Core files: `ActivityReading.cs` (sample/interface/status/signal), `ActivitySession.cs`, `IdleThresholdInput.cs`, `InputTickTracker.cs`, `PowerTickTracker.cs`.

Added App adapter: `src/EyeCare4U.App/Platform/WindowsActivitySource.cs`. Updated App startup/cleanup, main XAML/code-behind, MainWindowViewModel, and TrayIcon. Updated README and AGENTS.md.

Added tests: `tests/EyeCare4U.Core.Tests/ActivitySessionTests.cs`, `InputTickTrackerTests.cs`, and `tests/EyeCare4U.App.Tests/MonitoringViewModelTests.cs`. Existing tests were retained without changes. Added this review record. No SDK/project/package/workflow version change was needed; existing Windows CI already restores, builds, and tests the full solution.

No new dependency. The adapter uses GetLastInputInfo, GetTickCount64, QueryUnbiasedInterruptTime, WTSQuerySessionInformationW (current session, session/connection flags only), WTS notification registration, and suspend/resume registration. WTS native buffers are freed immediately, without marshaling user/domain/window-station names. Only input age/timing, lock/power state, and an accumulated duration are processed in memory. No keystroke content, coordinates, screenshots, titles, process inventories, browsing history, persistence, or telemetry is recorded by the app.

## Automated validation

Environment: Windows x64, .NET SDK 10.0.404; existing global.json selection policy unchanged.

Executed successfully on the final implementation:

```powershell
dotnet restore EyeCare4U.slnx
dotnet build EyeCare4U.slnx --configuration Release --no-restore
dotnet test EyeCare4U.slnx --configuration Release --no-build --no-restore
```

Results: restore succeeded; Release build succeeded with 0 warnings and 0 errors; **114 tests passed, 0 failed, 0 skipped** (93 Core, 21 App/view-model). This includes the original 52 and 62 new cases. Tests advance injected time and simulated source/signals, with no real waits, Windows locking, or machine suspension.

Coverage includes active accumulation independent of reminder state; initial/grace/exact/partial idle boundaries; new-input recovery windows; no prelaunch accounting; lock/unlock and missed boundary messages; sleep/resume and missed suspend; manual pause; overlapping conditions and alternate event order; blocked Start/Resume; Reset/Due; API unavailability and recovery; duplicate polls/events; wall-clock changes; malformed/regressed samples; 32-bit rollover/high-uptime ambiguity; awake-counter discrepancies; five-second gap cutoff and slow-source latency; prospective threshold changes; valid/invalid settings; and non-repeating significant-state property notifications.

The first expanded run exposed one test expectation that treated a 1000-second idle gap recovery as Active despite no new input. Corrected that test to provide fresh simulated input for the intended active-recovery case; production behavior correctly retained Idle. Subsequent full suites passed. Later malformed-data and slow-source regression cases also pass.

Automated tests/simulations do not validate actual Windows session/power message delivery, native ABI behavior across every architecture, tray behavior, actual OS scaling, or screen-reader announcements. Hosted status must be checked on the actual pushed commit; the existing workflow will run after publication.

## Actual desktop checks

Used the computer-use skill to inspect the built application against real native readings, not an injected monitoring source:

- Launch showed Ready/20:00 with an increasing estimate while the reminder was not started, and “Active — estimated from recent input.”
- Entered 29 seconds; the visible error appeared, Start disabled, applied threshold stayed at 120. Corrected to 30; previous estimated activity remained intact, and the applied setting changed to 30.
- Started the reminder; threshold and interval controls disabled. Left input inactive for more than 30 seconds. Observed automatic waiting at remaining **19:30**, estimate **00:01:13**; another observation showed both unchanged.
- Clicked Pause while held. Input became Active, but the timer stayed **Paused by you / 19:30**. Estimated usage grew to **00:01:43** while reminder remaining stayed unchanged, then idle held the estimate again.
- Resumed explicitly, allowed another idle hold (**19:01**, estimate **00:02:13**), then pressed Tab without a timer command. A later observation showed Active/Running, remaining **18:36**, estimate **00:02:38**, confirming automatic continuation with existing running intent.
- Reset returned Ready/20:00, enabled settings, and retained accumulated activity. Main-window Close ended the process; the post-close process lookup returned none. Immediate UI inventory can be stale; process absence was checked separately.

These observations preceded final cosmetic cleanup (compact empty text blocks, unique threshold access key, simpler waiting text) and the extra sampling-latency/malformed-input safeguards. Those final changes were rebuilt and covered by the final automated suite; a subsequent final launch/layout smoke check is recorded next. The native monitoring path and user-intent design remained the same.

Final smoke check: the compact-message style initially hid button/AccessText labels because it was applied globally. Scoped it to the optional monitoring/error messages, rebuilt, and reran all 114 tests successfully. Relaunched the final build and visually confirmed Start/Pause/Resume/Reset labels, the threshold label, compact monitoring status, fresh session estimate, and threshold reset to 120. Alt+T focused the threshold input. Closed with Alt+F4 and confirmed no app process remained. This visual regression was corrected before staging/publication; actual OS scaling and screen-reader checks remain outstanding.

## Outstanding checks, preserved from Milestone 2 and extended

- Actual lock/unlock, startup while already locked, sleep/hibernate/Modern Standby, long uptime/rollover, session disconnect/reconnect, registration/native failures, and recovery on the owner's machine. Simulated tests cover the policy but are not proof of Windows behavior.
- Direct tray synchronization, hidden policy-aware countdown and restoration of the same window/session, repeated Open/double-click, tray Exit, and visual icon removal after Close/Exit. The available desktop surface exposes app windows but not the notification area; no passing tray claim is made.
- Actual increased Windows text/display scaling and a real screen-reader session. Prior layout-only scaling simulation is not OS scaling verification. The new monitoring section should also be reviewed at 360 × 360.
- Native failures/gaps may intentionally undercount and require fresh input; defaults/settings/totals are lost at exit. A busy UI can delay polling/status. Standby subtypes and API precision limit what can be inferred. No estimates imply eye-break completion or medical effectiveness.

## Practical owner review

Run the launch commands in README. Use threshold 30 seconds and Custom interval 1 minute for a short test. Validate input errors and unchanged totals on a threshold change; test Ready accumulation, active countdown, idle hold/recovery, manual Pause precedence, and Reset/Due. On the owner's machine, repeat through lock/unlock, sleep/wake, overlapping idle/lock, and Pause before each transition. Check that no sleep/locked duration jumps into either counter and no automatic recovery overrides Pause or restarts Due. Then complete the preserved tray, scaling, and screen-reader checks. README contains the full numbered sequence and official Microsoft API references.

No SQLite, persistent settings/history, autostart, per-app tracking, notification, sound, break countdown, overlay, telemetry, camera, AI, or medical-effectiveness claims were added. Milestone 4 was not started.
