# Draft release notes — EyeCare4U 0.6.0-rc.1

Owner-review candidate, not a final/public release. Project license selection and the Windows validation matrix remain release gates. Unsigned Windows x64 portable ZIP; self-contained .NET 10.0.15; no installer or autostart.

Features: explicit configurable reminder interval; shared window/tray commands; conservative input-based estimated activity and idle/lock/sleep holds; manual Pause precedence; explicit 20-second breaks and 1/5/10-minute active-time snooze; once-per-occurrence notification attempts and opt-in sounds. Every launch/break completion returns Ready without auto-start.

Local SQLite preferences/history include saved activity today, seven calendar days and 50 recent events. Clear history confirms logical deletion while preserving preferences and current session/timer. No cloud/account, telemetry, camera/AI or per-app surveillance. Data stays in LocalApplicationData as unencrypted SQLite, outside the portable folder.

Milestone 6 fixes: valid loaded preferences survive history-query failure; checkpoint tasks report unexpected failures; final-save/startup failures have explicit messages; storage health/clear transitions have significant announcements; keyboard confirmation defaults to Keep, supports Escape and returns focus; system high contrast and visible focus are supported. Locked dependencies, artifact manifests/checksums and upstream notices accompany packaging.

Limits: estimated activity is not attention; finished break timers do not verify eye rest. Notifications can be suppressed; an API request is not proof of delivery/viewing. Crashes/forced termination or storage failure can lose uncheckpointed data. Clear is not secure erasure. A second launch exits instead of raising an existing window. No medically optimal timing or medical benefit is claimed.

Not fully verified: native tray/hidden/Exit/icon behavior; actual notification/audio and other-app/fullscreen focus; real power/session transitions; actual Windows scaling/high contrast; screen readers; clean-runtime-free machine. See docs/MILESTONE-6-REVIEW.md in the repository for exact results and owner steps. No camera work, final release, license selection or signing is included.
