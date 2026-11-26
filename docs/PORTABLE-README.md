# EyeCare4U 0.6.0-rc.1 — portable owner-review candidate

This unsigned Windows x64 candidate supports review on supported Windows 11 desktop editions (24H2/25H2/26H1 subject to Microsoft's OS lifecycle). Local smoke testing uses Windows build 26300; other versions, ARM emulation, Windows Server and Windows 10 are not qualified. See the repository's Windows validation matrix before broader use.

1. Compare the ZIP's SHA-256 with its adjacent `.sha256` file using `Get-FileHash -Algorithm SHA256`.
2. Extract the entire ZIP into a folder you control. Keep all DLLs, native SQLite files, configuration and licenses together.
3. Open `EyeCare4U.App.exe` directly. No separate .NET runtime installation is required by this self-contained packaging mode. A clean machine without any .NET runtime has not yet been tested.
4. Launch starts Ready. Choose an interval and press Start. Due never opens a window automatically; select Start eye break or Snooze explicitly. Finished break timers do not verify looking away or eye rest.
5. Close exits and awaits a final history snapshot; Hide keeps the app running in the tray. Use tray Open/Exit. The app never registers itself to start with Windows.

Normal user data remains at `%LOCALAPPDATA%\EyeCare4U\history.db`, outside this portable folder. Preferences and minimal accepted activity/lifecycle history are stored locally in unencrypted SQLite. No cloud/account, telemetry, camera/AI, keys, coordinates, titles, process inventory or URLs are collected. No live countdown resumes after restart. Saved history covers only observed periods; crashes/storage failures can lose recent unsaved data. Input inactivity does not establish absence or eye rest.

Clear history asks for confirmation, keeps preferences/current timer/current-session estimate, and starts a new collection boundary. It is logical deletion, not forensic secure erasure. Use isolated review data for destructive qualification; never clear real history just to finish a checklist. The repository's `scripts/Start-IsolatedReview.ps1` launches this exact executable with a marked temporary GUID data folder and an “isolated review” title. Reuse its printed directory for restart checks. Normal launch uses no arguments and has no development controls in its UI.

Windows may show reputation/SmartScreen warnings because the executable is unsigned. No certificate, installer, automatic startup, tag, GitHub Release or final release is supplied. Make trust decisions yourself; these instructions do not ask you to bypass Windows protections.

To remove: exit EyeCare4U, then delete its extracted portable folder. Saved preferences/history remain in LocalApplicationData. Optionally remove that application's saved-data folder after closing all instances and retaining any backup you want; that removes preferences too. Clear history in the app retains preferences. Also remove isolated temporary review folders when you no longer need their test data. Neither form promises secure erasure or removal of backups.

The project license decision is still pending and blocks public release. Third-party licenses/notices apply to their components only; see THIRD-PARTY-NOTICES.md and licenses/. Real tray, notification/audio, focus/fullscreen, lock/sleep, scaling/high contrast and screen-reader checks remain subject to the repository review matrix. No medical effectiveness is claimed.
