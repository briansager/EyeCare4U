# EyeCare4U

A Windows desktop portfolio project for configurable eye-break reminders during work or gaming.

**Current status: Milestone 6 / 0.6.0-rc.1 owner-review candidate.** Targeted reliability/accessibility fixes and a reproducible self-contained Windows x64 ZIP now accompany the existing reminder, tray, break/snooze, local preferences and history. Launch always starts Ready. Public release is blocked by the owner's unselected project license and outstanding Windows qualification; automated passes do not establish notification/audio, fullscreen, power-event or screen-reader behavior. No final release/tag, installer, autostart, cloud/account, per-app surveillance, camera/AI or telemetry is included.

## Technology and structure

C#, .NET 10 LTS, WPF/XAML, and straightforward MVVM. No MVVM framework or service container.

- `src/EyeCare4U.Core`: reminder state machine; activity accounting and credited-activity clock; input/power counter validation; interval/threshold parsing. No WPF or Windows API dependency.
- `src/EyeCare4U.App`: views, view model, application lifecycle, Windows input/session/power adapter, and Windows Forms NotifyIcon adapter. Window and tray share one ActivitySession, reminder, and view model.
- `src/EyeCare4U.Storage`: small parameterized SQLite store with transactional schema versioning and a FIFO worker queue; orchestration uses Core history contracts.
- `tests/EyeCare4U.Storage.Tests`: temporary-database schema, preferences, retry, clear-race, recovery and shutdown persistence tests.
- `tests/EyeCare4U.Core.Tests`: deterministic timer, usage, policy, parsing, and counter tests.
- `tests/EyeCare4U.App.Tests`: view-model validation, commands, holds, and state-notification tests, without launching a window.
- `.github/workflows/build.yml`: Windows restore, Release build, and full tests. Check the corresponding GitHub Actions run independently of local results.
- `Assets/EyeCare4U.ico` in App: original application-owned eye icon, drawn locally with no downloaded branding.

No new NuGet dependency was introduced for Milestones 3–4. Windows Forms is a built-in Windows Desktop framework reference; Windows monitoring, silent shell balloons, and optional local sound playback use small native adapters. ReminderExperience in Core coordinates break/snooze state and fakeable notification/sound interfaces. Milestone 5 adds [Microsoft.Data.Sqlite 11.0.0](https://www.nuget.org/packages/Microsoft.Data.Sqlite/11.0.0), verified as stable and compatible with net10.0 through official NuGet metadata and successful restore/build/native SQLite tests. Its SQLitePCLRaw native bundle is transitive. Test dependencies remain xunit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, and xunit.runner.visualstudio 3.1.4.

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

## Portable release-candidate review

Run `pwsh -NoProfile -File scripts/Package-Portable.ps1` from the repository on Windows with PowerShell 7.2+ and the selected SDK. It performs locked restore, Release build, all tests, a direct/transitive NuGet vulnerability audit, self-contained win-x64 restore/publish, archive/checksum verification and three negative packaging regressions. Failures stop packaging. No trimming, Native AOT, single-file bundling, signing, installer, tag or GitHub Release is used. Version is `0.6.0-rc.1` in Directory.Build.props; runtime packs are pinned to 10.0.15. Committed lock files pin NuGet versions/content hashes for normal and Windows runtime graphs. Dependency versions were retained after official verification, including Microsoft.Data.Sqlite 11.0.0; see [Milestone 6 review](docs/MILESTONE-6-REVIEW.md). xUnit v2 is test-only and marked legacy/security-fixes-only; no known vulnerabilities were reported by the current NuGet sources. An advisory check is not a security guarantee.

Outputs: `artifacts/packages/EyeCare4U-0.6.0-rc.1-win-x64.zip` and adjacent `.zip.sha256`. All generated staging, synthetic package tests and smoke extractions remain under ignored `artifacts/`. The ZIP contains only fresh publish output, owner-review instructions, release notes, notices and a per-file SHA-256 manifest recording SDK/runtime, source commit and dirty-tree status. ZIP timestamps/order are normalized. Regenerate from the same revision, SDK/runtime and locked packages; do not treat a dirty working-tree package as immutable release evidence. Public CI runs the same script and uploads ZIP/checksum as 14-day workflow artifacts after its successful tests; it does not publish a Release.

Supported target for this candidate: supported Windows 11 x64 desktop editions. Local testing is Windows build 26300; consult [Microsoft's .NET 10 OS matrix](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) and the local validation matrix for the difference between platform eligibility and actual tests. Other Windows versions, ARM emulation and clean machines without installed .NET remain unqualified. Self-contained means the runtime is bundled; the published process was checked loading its local runtime/native SQLite, but this is not a clean-machine demonstration.

Compare `Get-FileHash -Algorithm SHA256 <zip>` with the checksum, extract the entire ZIP, then open `EyeCare4U/EyeCare4U.App.exe` directly. The unsigned executable may trigger Windows reputation warnings; no protections are bypassed. Read [portable instructions](docs/PORTABLE-README.md) for data location and removal: deleting the extracted folder keeps LocalApplicationData history/preferences; optional data-folder removal after exit removes those too. Clear history keeps preferences and is logical deletion, not secure erasure. There is no automatic Windows startup.

For isolated review, extract into a fresh `artifacts/owner-review` folder, then run:

```powershell
./scripts/Start-IsolatedReview.ps1 -Executable './artifacts/owner-review/EyeCare4U/EyeCare4U.App.exe'
# To reopen the SAME isolated history, append -ExistingDirectory '<printed directory>'
```

The explicit review argument accepts only marked GUID folders under the current user's Temp/EyeCare4U-Review root; the window title says “isolated review”. Normal launches use LocalApplicationData and expose no review controls. Never clear real history to finish qualification. Confirmed Clear is covered end-to-end through view-model commands and populated temporary SQLite, including queue epochs, new collection and restart; live UI opening/Keep/Escape were checked separately. This is not evidence of deletion of the owner's existing production history.

Keyboard additions: Alt+F Refresh saved history, Alt+G Clear history, Alt+V Delete saved history when confirmation is open, Alt+K Keep history. Confirmation focuses Keep; Escape cancels and returns to Clear. Native focus is visibly outlined. High contrast uses system Window/WindowText brushes and responds to changes; actual Windows high-contrast/scaling and screen-reader qualification remain outstanding. Significant storage failure/recovery/clear announcements exclude periodic saved-time changes. No app animations were added. See the [release-candidate checklist](docs/RELEASE-CANDIDATE-CHECKLIST.md), [draft release notes](docs/RELEASE-NOTES-0.6.0-rc.1.md), and [single Windows validation matrix](docs/MILESTONE-6-REVIEW.md#windows-validation-matrix).

## Estimated activity: exact policy

“Estimated active PC time — this session” begins at the first reliable launch sample and ends when the app exits. It continues independently of Ready, Running, Paused, Due, snooze, or break state. Starting or finishing a break never rewrites usage totals or establishes actual eye rest. The session metric starts fresh; accepted history and valid preferences are saved separately as described below. Reopening the same hidden window does not begin a new session.

The input-idle threshold defaults to **120 seconds**. In Ready, enter a whole number from **30 through 900 seconds**. Whitespace is accepted; signs, fractions, overflow, and out-of-range values are rejected. Invalid text stays visible with an accessible error and the last applied threshold displayed; Start is disabled until corrected. Monitoring continues under the explicitly displayed applied setting. Valid changes settle the old policy, preserve accumulated totals, and establish a fresh baseline under the new policy. They never recalculate history. No threshold changes are allowed while Running (including held), Paused, Due, snoozing, or running a break.

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

Choose 20, 30, 60, or a custom 1–240 whole-minute interval in Ready. Invalid interval input remains visible, displays `--:--`, and disables Start. Reset restores the full selected interval, returns Ready, and clears running intent. From Due, Start eye break, Snooze, and Reset are explicit actions. Finishing a break also returns Ready, but the next normal Start remains explicit. Repeated invalid state commands do not restart the timer. Displayed seconds round upward so 00:00 does not appear before the credited-activity deadline.

Automatic blockers freeze the reminder's credited-activity clock; they do not impersonate a manual Pause. Running while held means **armed and waiting**, not consuming. Start/Resume while blocked is allowed and displays why it is waiting. Counting continues automatically only after all blockers clear while the user still intends Running. Unlocking while still idle cannot resume consumption; waking cannot override manual Pause. Inactivity never resets the interval or marks a break completed. Due remains Due through monitoring changes.

Core ReminderTimer still accepts injected time and is independently tested. In the application its time source is the ActivitySession clock, which advances only by conservatively credited activity; Windows APIs stay outside Core.

## Breaks, snooze, and once-only reminders

A reminder reaches Due after its configured estimated-active interval. It stays Due until Start eye break, Snooze, or Reset. It never automatically opens/raises a window, minimizes another app, blocks input, or starts a break. Notification dismissal/timeout changes no state. An explicit notification click may restore the existing window.

Each new Due occurrence has one eligible alert opportunity. Notifications are enabled by default; reminder sound and break start/end sounds are disabled by default. These options are saved at checkpoints. Alerts wait until monitoring reports Active; idle, lock, suspend, failure, and uncertain gaps defer them. On eligibility, enabled effects are attempted once, including if the API fails or Windows suppresses display. There are no retry storms on restore, input changes, or recovery. Settings at the first eligible opportunity apply to that occurrence; enabling an option later does not replay it. Disabling notifications clears the outstanding request; disabling a sound option stops app-owned sound playback.

**Start eye break** is available from Due when monitoring is reliable and unlocked/awake (Active or input-idle). It begins a separate monotonic **20-second** timer and shows “Look away from the screen at something about 6 metres away.” An optional end sound lets the user look away without watching the display. The normal interval stays stopped at Due. Input inactivity alone does not pause or complete the break, and the break can run while the window is hidden.

At a reliably observed 20 seconds, the result is exactly **“Break timer finished.”** The normal reminder returns Ready at its full selected interval and requires explicit Start. This says only that a timer finished; gaze, distance, attention, eye rest, and health benefits are not verified. The independent input-based usage estimate can still grow during the break.

**Cancel break** returns to the same Due occurrence with a cancellation message, without completion or a replacement alert. Lock, suspend, monitoring failure/reliability changes, negative elapsed time, or a gap over five seconds conservatively interrupts a running break and returns to Due with a reason. This is checked before completion, so a delayed wake cannot count as a finished break. The reason remains visible when the session becomes available. Cancellation/interruption does not generate another notification for that occurrence. A pending, unattempted alert is consumed when the user explicitly starts a break.

**Snooze** is offered only from Due, with choices **1, 5, or 10 minutes**, default **5**. Its label and remaining time explicitly identify snooze. It consumes estimated active time under the existing idle/lock/sleep/unavailable holds, not wall-clock time. Pause/Resume work on snooze; manual pause survives automatic recovery, and Resume while blocked arms without consuming time. It does not count the normal interval in parallel or mark a break completed. Expiration returns Due once and creates one new eligible alert opportunity. There is no snooze action during a break or an already running/paused snooze.

**Reset** explicitly abandons a normal interval, Due reminder, break, or snooze, clears the result, stops owned sound/removes pending notification requests, restores the full selected normal interval, and returns Ready. Core uses synchronous polling rather than scheduled completion callbacks; obsolete countdowns cannot later complete or recreate alerts. Shutdown makes polling/actions inert and disposes owned delivery resources. API failures are contained with a status note; state remains available in window/tray.

### Windows delivery and gaming limits

The existing application-owned icon and Windows Forms tray menu are retained. A small shell adapter manages the icon/balloon using Shell_NotifyIcon so balloon requests can explicitly include **NIIF_NOSOUND**, **NIIF_RESPECT_QUIET_TIME**, and **NIF_REALTIME**. This avoids a default balloon sound when sound is off and discards a request instead of queueing a stale alert. If native icon initialization fails, the built-in NotifyIcon preserves basic tray access; silent balloon delivery is reported unavailable instead of falling back to a noisy notification.

Optional sounds use built-in Windows sound-scheme aliases through asynchronous PlaySound. Each new cue stops/replaces prior app-owned playback; nothing is downloaded, queued, or looped. Playback follows the Windows sound scheme/audio volume and may be inaudible. Optional application sounds are independent of a suppressed balloon and may not be muted by Do Not Disturb; keep them off when silence is wanted. The application does not change or bypass OS notification settings.

Windows may suppress balloons in Do Not Disturb, quiet time, or fullscreen use. An API success is only a request, not evidence that the notification appeared or was read. Reminders remain available in the main window and tray. There is no automatic game detection, process injection, anti-cheat interaction, input interception, forced overlay, or fullscreen takeover. Exclusive-fullscreen compatibility is **not verified**.

## Window, tray, and accessibility

Hide to tray keeps monitoring and an armed reminder alive, subject to automatic holds. The existing window is restored by tray Open or double-click. The shared tray menu provides status, estimated usage, Open, Start/Resume, Pause, Start eye break, Cancel break, Snooze 1/5/10, Reset, and Exit. Start/Resume and Pause operate on the active normal or snooze timer; they are disabled during a break. Tooltips are capped at 63 characters. Only a user Open request activates the window; Due does not force focus.

Main-window Close exits the app. Shutdown stops/detaches polling, unsubscribes native hooks, unregisters session/power notifications before window destruction, and disposes tray resources. No hooks or polling remain after exit.

Controls support Tab/Shift+Tab, scrolling, and access keys: Alt+S Start, Alt+P Pause, Alt+R Resume, Alt+E Reset, Alt+M interval, Alt+C custom minutes, Alt+H Hide, Alt+T idle threshold, Alt+B Start eye break, Alt+N Cancel break, Alt+Z snooze choice, Alt+O Snooze, Alt+Y notifications, Alt+D reminder sound, and Alt+U break sounds. Labels/settings have accessible names and visible validation. Status/error changes are polite live regions; countdown and estimated-time values are not live regions. No announcements are deliberately emitted every second. Minimum window size is 360 × 360; content scrolls and controls/labels wrap. Actual Windows scaling and screen-reader behavior remain unverified.

## Practical manual review

1. Launch and confirm Ready/20:00, estimated activity beginning near zero, monitoring status, and default 120-second threshold. Leave the timer Ready and confirm estimated time can accumulate.
2. Try thresholds `29`, `901`, `1.5`, and text; confirm visible errors, the unchanged applied threshold, and disabled Start. Correct to `30`; verify prior totals remain. Set Custom interval to `1`.
3. Start and provide keyboard/mouse input. Stop input longer than 30 seconds. Confirm Waiting for activity, frozen remaining time, and frozen estimated total after the grace period. Provide input and confirm automatic continuation without resetting.
4. Pause manually. Provide input and confirm estimated activity can grow while the reminder remains Paused by you. Repeat through owner-controlled lock/unlock and sleep/wake; never rely on simulation as proof of Windows behavior.
5. During a run, lock, wait, unlock; check no locked time was credited. Repeat with sleep/hibernate/Modern Standby as available. After wake, verify a fresh baseline, no sleep jump, no immediate Due, and no auto-resume from manual Pause. Test overlapping idle/lock conditions. Start/Resume while blocked should arm and wait.
6. Reach Due using actual active time, then confirm input/unlock/recovery cannot restart it. Reset restores Ready/full interval. Valid saved preferences return on launch; countdowns never resume.
7. Exercise tray commands against the visible window. Hide during a run, wait, restore, and verify the same session/window and policy-aware countdown. Repeat Open/double-click, Close, and tray Exit (including Exit while hidden); visually check icon removal.
8. Check the new section at minimum window size, actual increased Windows text/display scaling, keyboard-only operation, and a screen reader. An owner-controlled delayed UI/debugger pause can exercise the five-second gap fallback; do not interpret that simulation as sleep verification.

Preserved Milestone 2 outstanding checks: direct tray synchronization; hidden countdown/restoration; tray Exit and visual icon removal; actual Windows scaling; screen-reader behavior. See [Milestone 4 review](docs/MILESTONE-4-REVIEW.md) for current evidence and the additional break/snooze owner checklist; prior review records remain historical. Hosted CI must be checked on the actual pushed commit.

For Milestone 4 review, keep Custom = 1 and the idle threshold at 120 initially. Let the interval reach Due with another app active; confirm no focus change and at most one notification. Dismiss the notification and check Due remains. Start a break, leave input untouched for 20 seconds, and verify “Break timer finished” / Ready / full interval without auto-start. On later cycles, cancel a break, try each snooze choice, pause/resume snooze, and Reset from both modes. Opt into sounds explicitly to check start/end/reminder cues. Repeat hidden through tray controls and owner-controlled lock/sleep/failure scenarios. DND/fullscreen, actual sounds, direct tray interaction, lock/sleep, OS scaling, and screen-reader checks require actual platform verification, not just tests.

## Local persistence and history

Database: `%LOCALAPPDATA%\EyeCare4U\history.db` (this review machine: `C:\Users\Brian\AppData\Local\EyeCare4U\history.db`). It is outside the repository/install directory. History is local, unencrypted SQLite, accessible to software with access to this Windows account. There is no automatic retention limit; the dashboard query limits do not delete older records.

Saved preferences: interval selection (20/30/60/Custom), valid custom minutes (1–240; default 20), idle threshold (30–900; default 120), notifications (default on), reminder sound and break sounds (default off), snooze choice (1/5/10; default 5). Missing/invalid values fall back to these defaults; invalid text edits do not replace the last valid value. No running intent, countdown, snooze or break is restored. A per-user Windows mutex rejects a second launch before monitoring/storage startup.

Schema version 1 stores a deletion epoch, preferences, collection IDs/start UTC, cumulative usage bucket IDs with recorded local date/offset/zone, accepted monotonic ticks and first/last UTC, and event/occurrence/break-attempt IDs with kind, non-sensitive reason, UTC, recorded date/offset/zone and tie-breaking sequence. Events are Due, break start/finish/cancel/interruption, snooze and relevant Reset. Reset/close during a break records an interruption. These records say nothing about notification delivery/viewing or verified eye rest; balloon dismissal is not an event.

History consumes accepted estimator windows, including partial idle-cutoff intervals, rather than estimating again. Reliable mappings split at local midnight and DST offset transitions while preserving monotonic ticks. Old date/offset/zone attribution is retained after timezone changes. A timezone/rule change, explicit Windows time-change signal, or wall-versus-monotonic mismatch over 250 ms re-baselines mapping: accepted ticks for the uncertain sample are retained separately as **without reliable daily attribution**, excluded from daily totals. Unobserved gaps are never filled. The timezone cache refreshes at checkpoints as a fallback. Daily summaries use the current local calendar labels for today and the preceding six dates; observed zero differs from no recorded data. Event display uses the recorded offset. A wall-clock correction can change UTC event ordering; recorded sequence breaks equal-time ties within a collection.

Checkpoint targets are roughly 15 seconds, plus lifecycle signals, Refresh saved history, and orderly Close/tray Exit. Database work runs on one FIFO background worker because [Microsoft documents that SQLite async I/O executes synchronously](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async). Stable IDs and cumulative upserts make retries/checkpoints idempotent; foreign keys, indexed queries and transactions coordinate writes. The dashboard shows saved snapshots and their capture time, never an unsaved estimate labelled durable. Healthy crashes/forced termination can lose activity/events/preferences since the last successful snapshot (typically roughly 15 seconds); stalls, failures or OS termination can lose more. There is no backfill. Close awaits the queued final snapshot without blocking the UI thread.

**Clear history…** opens an in-app confirmation. Confirming logically deletes all saved usage/events/collections, retains preferences/current timer/current-session estimate, and starts a fresh collection boundary. Transactions serialize old writes before deletion; epoch checks reject stale writes afterward. The first partial observation is clipped at the request boundary. New monitoring continues; the next checkpoint saves only new history. This is not forensic secure erasure: database free pages, filesystem remnants and backups may retain data. If deletion fails, existing saved records remain and collection still restarts at the request boundary; old unsaved pending history is discarded conservatively. If deletion succeeds but dashboard reload fails, status says so.

Unavailable, unreadable, corrupt or newer-schema storage leaves timers/reminders working in memory and reports **History is not being saved**. Existing data is preserved; automatic checkpoints/manual Refresh retry. Successful recovery saves retained cumulative accepted activity and pending events without duplicates; recovered old preferences do not overwrite current in-memory choices. Pending events are capped at 5,000 during prolonged failure, with a visible warning if oldest unsaved events are dropped. Unsupported newer/unrecognized unversioned databases are never rebuilt automatically. Schema creation/version changes are transactional.

See [Milestone 5 review](docs/MILESTONE-5-REVIEW.md) for actual validation and the remaining Windows checks. To review, launch with the commands above, change a valid preference, Close/reopen and confirm Ready plus the saved choice and history. Use Custom = 1 for Due/break events. Refresh repeatedly and restart; prior totals must persist without duplication. Clear only history you intend to delete; cancel keeps it. Confirmed Clear keeps the live session/timer, then new activity should reappear at a later checkpoint. Launch again while open: only one monitoring process should remain.
## Seven-milestone roadmap

1. Desktop foundation — implemented.
2. Timer and system tray — implemented; outstanding manual checks preserved.
3. Session-only estimated usage and idle/lock/sleep-aware timing — implemented; platform checks distinguished from deterministic tests.
4. Break experience and reminders — implemented: explicit 20-second breaks, active-time snooze, nonintrusive notifications, optional sounds. No overlay or game detection.
5. Local persistence/preferences — implemented: SQLite history, validated preferences, seven-day summaries, recent events and confirmed logical deletion. No autostart or automatic retention policy.
6. Reliability/accessibility and release candidate — implemented: targeted fixes, locked dependencies, self-contained portable ZIP/checksum/notices, and an honest Windows validation matrix. License selection and owner-machine qualification remain public-release blockers.
7. Optional computer vision — future only: explicit opt-in, local processing, consent, and documented limits. No camera or AI scaffolding before this milestone.

## Evidence, privacy, and API references

The 20-minute default and 20-second break timer are configurable reminders, not medical treatment or a medically optimal schedule. Break-timing recommendations and research evidence will be documented carefully in a later milestone; this app makes no medical-effectiveness claim.

**Implemented:** local input-age/lock/power processing, session estimates, and the minimal SQLite history/preferences described above. No keystrokes, mouse coordinates, screenshots, foreground titles, process inventories, or browsing history are recorded. Session-query buffers are freed immediately; only connection/lock flags are read into managed code. Only the documented preferences, accepted duration aggregates and lifecycle events are persisted. Nothing is sent as application telemetry. Development tools/GitHub Actions have their own network behavior. No camera features existed in Milestones 1–2 or exist now.

**Future intentions:** continue minimal collection/local processing, explain any future retained data and deletion controls before introducing them, and describe usage values as estimates. Clear history is implemented; optional camera processing remains deferred.

Microsoft documentation consulted for the adapter and conservative policy:

- [GetLastInputInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo): current-session scope and non-monotonic input timestamps.
- [GetTickCount64](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-gettickcount64) and [QueryUnbiasedInterruptTime](https://learn.microsoft.com/en-us/windows/win32/api/realtimeapiset/nf-realtimeapiset-queryunbiasedinterrupttime): elapsed counters and awake-only time.
- [WTS session query](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsquerysessioninformationw), [extended session flags](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/ns-wtsapi32-wtsinfoex_level1_w), and [session notification registration](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification). Modern supported Windows flag meanings are used, not Windows 7's reversed flags.
- [Power notification registration](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registersuspendresumenotification), [power messages](https://learn.microsoft.com/en-us/windows/win32/power/wm-powerbroadcast), and [automatic resume limitations](https://learn.microsoft.com/en-us/windows/win32/power/pbt-apmresumeautomatic).

No release/license is selected. The owner will choose a license before public release. Standing milestone commit/push authorization and safety/review requirements are recorded in AGENTS.md. No work beyond Milestone 6 is included.

Additional Microsoft delivery references: [NOTIFYICONDATA](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw) and [PlaySound](https://learn.microsoft.com/en-us/previous-versions/dd743680(v=vs.85)).
