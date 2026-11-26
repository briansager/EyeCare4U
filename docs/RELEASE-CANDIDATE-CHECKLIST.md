# Release-candidate owner checklist — 0.6.0-rc.1

This is an owner-review candidate. Do not publish a final release/tag until the blockers below are resolved. See MILESTONE-6-REVIEW.md for actual results and the Windows matrix.

1. Build with `pwsh -NoProfile -File scripts/Package-Portable.ps1`. Confirm locked restore/build/tests/advisory checks/package checks pass. Match the ZIP SHA-256 and inspect package-manifest source commit/dirty status. Extract the entire ZIP into a fresh folder.
2. Launch the extracted executable using Start-IsolatedReview.ps1; keep the printed temporary directory. Confirm Ready, icon, monitoring and saved history. Use Custom = 1 to reach Due and finish/cancel a break; test snooze/manual Pause. Close/reopen the same isolated directory: Ready and saved preferences/history must remain, without duplicates.
3. On that isolated data only, open Clear, inspect its scope, cancel once, then confirm deletion. Keep current timer/session/preference values in view. Old records must disappear, new accepted activity/events must appear, and restart must not resurrect old rows. Automated populated-SQLite command tests pass; actual live confirmation click remains an owner check. Do not clear production history merely for review.
4. Follow all unverified matrix rows: visible/hidden tray commands, restoration and Exit/icon removal; actual notification/audio/DND and other-app/borderless/exclusive-fullscreen focus; real lock/sleep/resume and manual Pause; keyboard grid navigation, actual Windows text/display scaling/high contrast, screen-reader announcements and clean-runtime-free-machine startup.
5. Select the project license explicitly and review third-party redistribution notices. No project license has been added. Review unsigned-binary reputation prompts yourself; no bypass is part of qualification. Resolve known defects found in matrix checks before a public release decision.

Automated green builds are a prerequisite, not completed Windows qualification. No medical effectiveness, gaze/attention verification or exclusive-fullscreen compatibility is established. No camera work is authorized before Milestone 7.
