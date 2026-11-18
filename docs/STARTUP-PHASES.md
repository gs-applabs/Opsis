# Opsis start-up phases (2.6.0)

Every phase writes one line `[startup] <name> <ms> ms (t=<ms since process start>, thread, UI)` to the Opsis log (`%AppData%\Opsis\logs`, or the OPSIS_APPDATA folder). The class is `Services/StartupPhases.cs`.
`Mark("x")` closes the phase that began at the previous mark; `Measure` times one block; `StartStallProbe()` runs a 50 ms timer on the UI thread for 25 s and logs the longest delay of that timer
(= the longest time the UI thread, and with it the pointer hooks, did not answer): `[startup] longest UI-thread stall ...`.

## Mechanism
- One queue, `StartupPhases.Queue` (`DeferredStartup`), started ONCE after the main window object exists (`Queue.Start()` in `TrayApplicationContext`). First step after 1.2 s, then one step every 400 ms, one step per timer tick, a failing step is logged and does not stop the others.
- Queue order: desktop icon engine (low-level mouse hook + crash restore) -> native DeskContainers -> wallpaper (schedule, Enable, overlay, lock screen follow) -> restore of the last session, where the widgets are created one every 150 ms (`WidgetHostEngine.RestoreSnapshotsStaggered`).
- Unchanged: the single early-start mechanism (the per-user logon task `Opsis\Start with the desktop`, `StartupTask`); safe mode still skips wallpaper / plugins / last session; `OPSIS_STARTUP_NODEFER=1` (diagnostic only) runs every step immediately, the old behaviour, to compare both with the same binary.
- Already lazy before this change: LibreHardwareMonitor (`GpuMonitorService`, only when a widget needs the sensors; no `Computer.Open()` at start-up).

## Measured (isolated run, 2026-11-18, this development machine)
Command: `Opsis.exe --background` with `OPSIS_APPDATA=<temp>`, empty profile except a "Last Session" with 2 widgets (sysmon, clock), no animated wallpaper, no containers, 32 s run, then the process was killed. One run each.

| Phase | before (NODEFER) ms | after ms | where |
|---|---|---|---|
| safe-mode check + timers | 1 | 1 | UI, sync |
| plugins + appearance start | 24 | 19-24 | UI, sync |
| tray menu + tray icon visible (time to tray: t = 116 ms / 69-113 ms) | 91 | 49-88 | UI, sync |
| update check, hotkeys, icon engine, activity, discord, backdrop | 15 | 13-16 | UI, sync |
| desktop icon engine (mouse hook) | inside the line above | 2 (at t = 1.35 s) | UI, deferred |
| native containers | 7 (at t = 140 ms) | 8-13 (at t = 1.4-1.8 s) | UI, deferred |
| wallpaper step (wallpaper itself was OFF in this run) | 31 | 31-34 | UI, deferred |
| last session, 2 widgets | 33 | 32-38 (widgets one every 150 ms) | UI, deferred |
| main window created (t when done) | 72 (t = 245 ms) | 43-52 (t = 137-176 ms) | UI, sync |
| longest UI-thread stall in the first 25 s | 80 ms | 94-163 ms | probe |

Reading: in this isolated profile the synchronous start-up is about 0.14-0.18 s before the main window exists (0.25 s before the change), nothing blocks the UI thread for more than ~160 ms, and the three heavy steps now start 1.2-2.6 s after the tray icon is visible, one per tick. The stall probe shows no gain here (80 vs 94-163 ms, one run each = noise level), because this profile has nothing heavy to defer.

## NOT measured / NOT VERIFIED
- The stall the user sees on the real machine (mouse stuck for seconds at logon): NOT reproduced here, so NOT VERIFIED that it is gone. Candidates this run did not exercise: the animated wallpaper WebView2 start (wallpaper was off, a real attach would change the real desktop), real desktop icon lists (cross-process read in the icon engine), several WebView2 widgets, a real profile, logon with many other start-up apps, antivirus.
- Real-machine numbers (time to tray, time to first wallpaper frame, jitter with the real profile): NOT VERIFIED; run `Opsis.exe --background` once, then read the `[startup]` lines of the log.
- The user-visible price of the deferral: the wallpaper, containers and widgets appear 1-3 s later than before, and quick-hide by double click works after the first second.
