# Milestone 5 review — local persistence and usage history

Scope: Milestone 5 only, based on clean `main` at `8c05493f693c8f2c98ae5e893329836844aaaf1b`. No work on Milestone 6. Standing commit/push authorization remains in AGENTS.md.

## Implemented behavior and storage decisions

The History section adds saved estimated activity today, break timers finished today, exactly seven calendar-date rows, and the latest 50 lifecycle events. Missing data differs from an observed zero; totals cover only reliable observed periods while the app ran. A finished break timer does not establish looking away or eye rest. Refresh reconciles existing row objects by date/ID instead of resetting ItemsSource or activating the window. Loading, empty, unavailable and unattributed-duration messages are explicit.

Actual database on this machine: `C:\Users\Brian\AppData\Local\EyeCare4U\history.db`, resolved from `Environment.SpecialFolder.LocalApplicationData` plus `EyeCare4U/history.db`. No actual database, logs, local preferences or generated output belongs in Git. Storage is local unencrypted SQLite, with no cloud service or automatic retention/deletion schedule.

Core contains history contracts/collector only, with no SQLite, WPF or Windows reference. The new Storage project uses **Microsoft.Data.Sqlite 11.0.0**, verified stable/compatible with net10.0 using [official NuGet metadata](https://www.nuget.org/packages/Microsoft.Data.Sqlite/11.0.0), then restored, built and exercised against real temporary SQLite files. The package supplies the transitive SQLitePCLRaw native bundle. Microsoft documents [synchronous SQLite async I/O](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async); all directory/open/schema/read/write work therefore runs on a FIFO TaskScheduler.Default worker queue, never directly on the dispatcher. Values use SQL parameters, foreign keys are enabled on every connection, connections close after each queued operation, and schema creation/user_version changes use transactions. A two-second SQLite busy timeout prevents indefinite ordinary lock retries. Corrupt, unrecognized unversioned, and newer-schema databases are preserved rather than replaced.

Schema version 1 stores preferences; deletion epoch; collection ID/start UTC; cumulative usage bucket IDs, accepted monotonic ticks, recorded date/offset/timezone and first/last UTC; event ID, occurrence ID, optional break-attempt ID, kind, safe reason, UTC, recorded date/offset/timezone and sequence. Stable IDs, cumulative MAX upserts and conflict-ignore event inserts prevent retry duplication. Date and event-order indexes support the seven-date aggregates and 50-row query. Older stored history remains until explicit Clear; query limits are not retention limits.

Saved preferences: selected 20/30/60/Custom interval, valid custom minutes 1–240 (default 20), idle threshold 30–900 (default 120), notifications (default on), reminder/break sounds (default off), snooze 1/5/10 (default 5). Missing/invalid stored values use defaults; malformed JSON falls back safely. Invalid text edits retain the last valid persisted value. Settings apply before the initial monitoring baseline. Every launch is Ready with fresh session totals/baselines; no live countdown or running/snooze/break intent is persisted or resumed. A Windows per-user Global mutex rejects a second launch before monitoring or database initialization.

## Timing, daily attribution and loss

ActivitySession exposes its exact accepted windows, including partial idle-cutoff/new-input unions. History does not run another estimator. Existing exclusions for lock, suspend, failures, unreliable gaps and fresh baselines still apply. Duplicate/out-of-order observations cannot credit an interval twice.

Durations are monotonic ticks. Reliable UTC mappings split windows at local midnight and daylight-saving offset changes; date/offset/zone recorded then are retained. A timezone/rule change, explicit WM_TIMECHANGE, or wall-versus-monotonic mismatch greater than 250 ms re-baselines mapping. Accepted ticks in the uncertain sample are saved without a date, excluded from daily totals and disclosed separately. No unobserved gap is backfilled. The timezone cache refreshes at checkpoints as a fallback for missed OS change signals. Event UTC is chronological ordering, with sequence/ID tie-breaking; a wall-clock correction can reorder wall timestamps. UI renders each stored event's original offset.

Checkpoints target roughly 15 seconds plus lifecycle boundaries, manual Refresh and awaited orderly Close/tray Exit. Snapshots cross into the worker immutably. The UI labels saved snapshot capture time separately from the live session estimate. A crash/forced termination can lose activity/events/preferences since the last successful snapshot, typically around 15 seconds with healthy scheduling/storage and potentially much more during stalls/failure. OS termination can bypass orderly flush. No missing activity is invented.

Events distinguish Due, break start, finish, cancellation, interruption with safe reason, snooze and relevant cycle Reset. Reset/close during a running break records interruption. Repeated invalid actions do not create outcomes. Notification attempts/delivery/viewing and balloon dismissal are not history events. No screenshots, keys, coordinates, window titles, process names, URLs, camera data or telemetry is collected.

## Clear and failure behavior

Clear history opens inline confirmation. Confirmation logically deletes stored usage/events/collections, keeps preferences/current timer/current-session estimate, increments the persisted epoch and resets the in-memory collection boundary. FIFO transactions place old writes before deletion; stale post-delete retries are rejected by epoch. Async generation guards stop a delayed old checkpoint from restoring a deleted dashboard. The first post-clear partial interval is clipped at the request boundary; subsequent monitoring is saved at the next checkpoint.

This is not forensic secure erasure; free pages, filesystem remnants and backups may retain data. If deletion fails, existing stored history stays and new collection still starts at the request boundary, conservatively discarding old unsaved pending data. A successful deletion followed by reload failure is reported separately.

Storage failure reports **History is not being saved** while timing/reminders continue in memory. Checkpoints and manual Refresh retry. Recovery preserves cumulative usage/event IDs, does not replay accepted durations, and does not overwrite current choices with late-loaded old preferences. Up to 5,000 pending events are retained in memory; prolonged failure beyond that drops oldest pending events with a status warning. A prior saved snapshot can remain visible with its failure status; unsaved data is never represented as durable.

## Files and validation

Added: Core `HistoryModels.cs`/`HistoryCollector.cs`; Storage project, `SqliteHistoryStore.cs`/`HistoryService.cs`; App `SingleInstanceGuard.cs`/`HistoryViewModel.cs`; Core/App persistence tests and a real temporary-database Storage test project; this review. Updated solution/project references, ActivitySession observation output, ReminderExperience lifecycle output, App startup/async shutdown, Windows time-change hook, main view model/XAML, README, AGENTS and Windows CI test step. The solution-based CI command includes all three test projects.

Validation on Windows x64 / SDK 10.0.404:

```powershell
dotnet restore EyeCare4U.slnx
dotnet build EyeCare4U.slnx --configuration Release --no-restore
dotnet test EyeCare4U.slnx --configuration Release --no-build --no-restore
```

Final local result: restore succeeded; Release build **0 warnings, 0 errors**; **179 tests passed, 0 failed, 0 skipped** (139 Core, 27 App, 13 Storage). All previous 146 tests remain. During development a temporary test fixture Path-name compile collision was corrected and an xUnit collection-size style warning was resolved before final validation.

Coverage includes transactional/repeated initialization and foreign keys; preference defaults/round trip/invalid JSON and values; restart Ready/fresh monitoring; exact estimator windows/idle/failure totals; partial midnight, both DST transitions, forward/backward clock jumps, timezone change, explicit invalidation and culture-independent date keys; duplicate observations and lifecycle actions; break cancellation/finish/reset/close interruption distinctions; idempotent cumulative/event writes; 50-row query; missing versus zero; unattributed totals; queued save/Clear/stale epochs and delayed dashboard continuation; unavailable storage recovery; corrupt/newer preservation; final accepted fractional interval/event persistence; stable dashboard row identity; in-app confirmation commands; mutex exclusivity.

## Actual desktop review and outstanding checks

Used the computer-use skill on the real Release app/native monitoring. Observed Ready/20:00 and growing session/saved-today totals. Chose Custom = 1, started explicitly, reached Due, started a 20-second break, then observed Ready and “Break timer finished.” Closed normally; a process lookup found no remaining app. Rebuilt/reopened: Ready/01:00, fresh session estimate, saved Custom = 1, saved activity and three original Due/start/finished events remained, with one break timer finished today. Repeated manual Refresh retained those event IDs and the finished count, without new duplicate events. UI showed previous unobserved days as “No recorded data”. A second executable launch exited cleanly while only the original monitoring process remained.

Inspected the live inline Clear history confirmation and selected **Keep history**. Actual live history was retained; deletion/new collection/race behavior was verified using temporary SQLite databases and view-model commands, not by deleting the real user database through the UI. A full live confirmed-deletion owner check remains. Restored the selected interval to 20 minutes before final orderly Close; the saved valid custom value remains 1 for convenient review. Sounds stayed off. No claim of notification appearance, audible output or foreground/fullscreen behavior follows from these checks. Automation sometimes returned stale focused-element text; visual screenshots and refreshed history values were used where available.

Owner checks still required:

- Confirm Clear on data you intend to delete; verify current timer/session estimate/preferences stay, old records do not return, and new activity/events appear after a checkpoint/restart. Logical deletion is not secure erasure.
- Minimum 360 × 360 layout/resizing, actual Windows text/display scaling, keyboard history-table navigation through refresh/day rollover, and real screen-reader announcements. Tests do not establish assistive-technology behavior.
- Direct tray synchronization, hidden normal/break/snooze operation, same-window restoration, repeated Open/double-click, tray Exit/async flush, visual icon removal and Explorer restart.
- Real notification display/click/dismissal, once-only behavior under DND/quiet time/fullscreen, another-app/game focus, and audible start/end/reminder cues. Exclusive-fullscreen compatibility remains unverified.
- Actual lock/unlock, sleep/hibernate/Modern Standby, RDP/session changes, late native notifications and recovery across break/snooze/storage checkpoints. Deterministic tests verify policy, not actual Windows power/session delivery.
- Long-running owner-machine midnight/DST/timezone/wall-clock transitions and forced-termination loss/recovery. No OS clock/timezone/security settings were changed for review.

Launch/review instructions are in README. Hosted CI is checked after publishing on the actual commit; the handoff reports its final result independently of local checks. No autostart, installer/public release, cloud/account, per-app tracking, camera/AI, telemetry or medical-effectiveness claim was added.
