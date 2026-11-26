# Milestone 4 review: break experience and reminders

Date: November 26, 2026. Implemented Milestone 4 only. Actual baseline was `c5ea0b0e0742b8e2aeb4cfc214f66c2a60dd6f13`, clean main matching origin/main. Read root guidance, README, source/tests, SDK policy, and historical milestone records before editing. Preserved the standing validation/review/commit/push authorization. No unrelated user changes were present.

## State and timing policy

ReminderExperience in Core coordinates the existing normal timer, a separate credited-activity snooze timer, and a separate monotonic break deadline. It has distinct Interval/Snooze/Break modes and None/Running/Finished/Cancelled/Interrupted break states. Underlying normal Ready/Running/Paused/Due semantics remain intact. Normal timer remains stopped at Due during snooze and break, so the three countdowns do not consume concurrently. The App routes all reminder actions through the same experience/view model for window and tray.

Due remains until an explicit Start eye break, Snooze, or Reset. Start eye break is enabled only from Due with reliable unlocked/awake monitoring, including input-idle. It starts 20 elapsed monotonic seconds; ordinary input inactivity does not hold it. The screen shows the requested approximately-six-metre look-away instruction and explains optional end sound. No window, overlay, or focus change is initiated by a Due event.

Reliable completion produces exactly “Break timer finished.” It resets the normal reminder to Ready/full selected interval without starting. It establishes no verified gaze, distance, eye rest, or health effect. Usage estimation continues independently under its existing input-based policy, including during breaks; no totals are rewritten to invent rest.

Cancel returns to the same Due occurrence with a cancellation result and no completion. Lock, suspend, native/monitoring failure, reliability-baseline changes, negative elapsed time, or an unexplained gap over the existing five-second threshold interrupts before checking completion. Interruption returns Due with a reason retained through recovery. ActivitySession exposes a reliability revision so a failure followed by recovery between experience refreshes cannot be mistaken for uninterrupted break time. Starting, cancelling, or interrupting a break consumes that occurrence's pending alert opportunity; cancellation/recovery does not produce a duplicate alert.

Snooze choices are 1/5/10 minutes, default 5, from Due only. Snooze uses credited estimated active time and all existing idle/lock/sleep/unavailable holds. Pause and Resume operate on snooze while in that mode; manual pause wins automatic recovery, and Resume while blocked arms without consuming time. Expiration returns to Due once with one new alert opportunity. Snooze is labelled distinctly, never silently starts a full normal interval, and never marks completion. It is unavailable during break or existing snooze.

Reset abandons any normal/Due/break/snooze cycle, restores Ready/full selected normal interval, clears result, stops app-owned sound, and clears outstanding balloon requests. There are no delayed completion callbacks to survive Reset; Core reconciles synchronously when polled. Shutdown makes subsequent refresh/actions inert and disposes notification/sound resources. Effects may fail, but failures are contained and cannot resolve/reset/corrupt the reminder. A delivery status note reports failures without exposing exception details.

## Notification and sound policy

Every new Due occurrence has one alert opportunity, deferred until the session is Active. Idle, locked, suspended, and unavailable conditions defer without repeated attempts. Once eligible, enabled notification and reminder-sound effects are attempted once even if an API fails or Windows suppresses delivery. Restoring the window or activity/monitoring recovery cannot replay an attempted occurrence. Disabled settings at that first eligible opportunity consume it without their effect; enabling later does not retroactively replay it. Turning notification delivery off clears an outstanding request; turning a sound option off stops app-owned playback. All options are session-only: notifications on, reminder sound off, combined break start/end sounds off by default.

The Windows Forms tray menu and application-owned icon are retained. ShellTrayIcon is a small native shell adapter using the same HWND/icon/menu, replacing only icon/balloon plumbing because managed NotifyIcon.ShowBalloonTip does not expose NIIF_NOSOUND. Native balloon requests use NOSOUND, RESPECT_QUIET_TIME, and REALTIME flags, so default notification audio is suppressed, OS quiet policy is respected, and a queued stale request is discarded. Basic tray access falls back to built-in NotifyIcon if native icon initialization fails; that fallback does not issue potentially noisy balloons. Taskbar recreation reuses the icon registration and avoids creating duplicate fallback icons on repeated messages.

Windows notification hide/timeout/dismissal messages have no state-changing handler. A balloon click or tray Open/double-click is an explicit user action that can restore the existing window, and never finishes/resets a reminder. Even a late OS click changes no timer state. Native hooks are removed, registrations deleted, fallback handlers unsubscribed, and the menu/icon disposed on shutdown before window destruction.

Sounds use local Windows sound-scheme aliases through asynchronous PlaySound, with no network download/service, loop, or queue. Each cue stops/replaces prior app-owned audio. Optional audio follows the system sound scheme/volume and may be inaudible or may still sound when a balloon is suppressed; Do Not Disturb is not assumed to mute all application audio. No OS settings are changed or bypassed. Playback failure is contained; shutdown/Reset/cancel/interruption stop owned audio.

There is no automatic foreground activation, minimize, input interception, overlay/fullscreen takeover, game-process inspection/injection, anti-cheat interaction, or automatic game detection. Notifications can be suppressed by DND/fullscreen and remain only requests, not proof of display/read. Exclusive-fullscreen compatibility is not verified. Window/tray actions remain the fallback.

## Files and dependencies

Added: `src/EyeCare4U.Core/ReminderExperience.cs`; `src/EyeCare4U.App/Platform/ShellTrayIcon.cs`, `WindowsReminderSounds.cs`; Core `ReminderExperienceTests.cs`; App `BreakViewModelTests.cs`; this review record.

Updated: ActivitySession reliability tracking/activity timer factory; App lifecycle; MainWindowViewModel; TrayIcon; main XAML/code-behind; README; AGENTS.md. Existing interval/idle validation and usage tests are retained. SDK, project/package versions, and Windows CI workflow are unchanged. No NuGet dependency was added.

Official Microsoft references consulted: [NotifyIcon.ShowBalloonTip](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon.showballoontip?view=windowsdesktop-10.0), [NOTIFYICONDATA](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw), and [PlaySound](https://learn.microsoft.com/en-us/previous-versions/dd743680(v=vs.85)). Source review confirms only explicit Open/click paths call Show/Activate, not notification delivery or timer completion.

## Automated validation

Windows x64, SDK 10.0.404. Commands:

```powershell
dotnet restore EyeCare4U.slnx
dotnet build EyeCare4U.slnx --configuration Release --no-restore
dotnet test EyeCare4U.slnx --configuration Release --no-build --no-restore
```

Local results: restore succeeded; Release build succeeded with 0 warnings/errors; **146 tests passed, 0 failed, 0 skipped** (122 Core, 24 App/view-model), including all original 114 tests. Tests inject time and fake monitoring/notification/sound adapters, without real waits, locks, or suspension. No automated failures were encountered in the milestone runs.

Coverage: alert at-most-once, deferred eligibility under each blocker, recovery/dismissal without repeats, disabled/settings changes, snooze 1/5/10 timing, holds/manual pause/blocked Resume, one new Due occurrence on expiry, no simultaneous normal consumption, break completion through simulated Idle classification, cancellation, lock/suspend/failure/long-gap/intermediate-recovery interruption, Reset during break/snooze, repeated actions, stale refresh after Reset/dispose, once-only sounds, adapter/cleanup exceptions, shared commands/labels/options, and significant state/result property notifications without per-second announcements.

Simulations and fake adapters do not verify Windows shell display, native audio output, actual session/power delivery, real hidden/tray behavior, focus in games, OS scaling, or assistive technology.

## Desktop checks and remaining checks

Used the computer-use skill and the real built app/native monitoring. Selected Custom = 1, confirmed 01:00 and started explicitly. The interval reached Due / “Time for an eye break” / 00:00 with Start eye break, Snooze, and Reset enabled. Started a break and saw Break running / 00:20, the six-metre instruction, enabled Cancel/Reset, and disabled normal Start/Pause/Resume/Snooze. Cancel returned Due with “Break cancelled. Reminder remains due.” Started again, left input untouched, and observed Ready / 01:00 / “Break timer finished.” without a new normal interval. Usage continued independently rather than being forced to show rest.

An attempted another-app focus observation was inconclusive: desktop foreground changed during review and the automation sometimes returned cached accessibility text or captured an occluding window. A passing foreground/fullscreen test is not claimed. Due was directly inspected after an explicit review activation. System balloon visibility/click/dismiss and audible sounds remain unverified; no actual audio-output claim follows from fake adapter tests.

A later explicit one-minute run reached Due again. The default Snooze action showed Snoozing / 05:00 with the normal interval stopped. Pause showed Snooze paused by you / 04:56; Resume continued from 04:56, and Reset restored Ready / 01:00 without a completion result. Alt+F4 closed the app; a process lookup found none. Final source review also added a duplicate taskbar-created-message guard; that native Explorer-restart path remains unverified manually. Owner-machine checks retained:

- Direct tray synchronization, hidden break/snooze operation, restoration of the same window/session, repeated Open/double-click, tray Exit, and visual icon removal. The available desktop inventory does not expose the notification area reliably.
- Real notification display/click/dismissal, once-only behavior under DND/quiet time/fullscreen, non-focus-stealing behavior with another app/game, and audible sound start/end/reminder output. No exclusive-fullscreen compatibility claim.
- Actual lock/unlock, sleep/hibernate/Modern Standby, failures/gaps and recovery during break/snooze, and late native events. Unit tests verify policy, not Windows integration.
- Actual Windows scaling, 360 × 360 review of new controls, keyboard-only tray interaction, and screen-reader announcements. Prior scaling simulations do not replace actual OS checks.

## Short owner checklist

Launch with the README commands. Use Custom = 1 and idle threshold 120 to reach Due without unintended idle holds. Keep another app active and verify no focus change, at most one silent notification, and dismissal leaving Due. Start a break and look away for 20 seconds; verify finished/Ready/full interval, without auto-start. On later cycles cancel a break, snooze each duration, pause/resume while blocked, and Reset from each mode. Opt into sounds explicitly and verify they remain single/nonoverlapping. Repeat hidden through the tray and across real lock/sleep; interrupted breaks must remain Due, not finish. Finish the tray, scaling, and screen-reader checks listed above.

No SQLite, persistence/history dashboard, autostart, per-app tracking, overlay, game detection, camera, AI, telemetry, or medical-effectiveness claim was added. Milestone 5 was not started. Publication follows the existing owner authorization after validation and staged review; hosted CI must be checked on the actual pushed commit.
