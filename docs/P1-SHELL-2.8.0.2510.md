# Opsis P1 — Advanced native shell customization

**Base:** `feat/p0-from-2.8.0.2510` (P0, based on `release/2.8.0.2510`).

## P1 changes delivered in this branch
- Independent Explorer view controls: file extensions, hidden files, compact mode and launch folder, via explicit Apply buttons.
- Individual Windows 11 Taskbar controls: alignment, Widgets, Task View and search presentation; current values read from HKCU, with input validation.
- Original registry settings use the same first-use snapshots and Restore controls as P0; individual edits never overwrite that backup.
- Hot Corner dwell time (300–2500 ms), corner edge size (2–20 px), and optional full-screen inhibition. Settings are opt-in and inherited from P0.
- Safe Mode still skips Hot Corners. Settings are stored in the existing Opsis settings file; no additional background service.

## Important compatibility notes
The Windows 11 Taskbar keys are implementation-dependent; the UI may not visually update on some current/future Windows builds. The advanced Start entry opens the native Windows Settings page, **not** a full replacement Start menu. No protected Windows components are modified.

## Tests required before P1 merge
1. On Windows 11, compile and run `dotnet build -c Release -m:1 -p:UseSharedCompilation=false -nodeReuse:false`.
2. Run `dotnet test tests/Opsis.Tests/Opsis.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -nodeReuse:false`.
3. Check explorer/taskbar individual settings and full-profile application in both orders; restore exactly the prior registry state.
4. Test all four corners on multi-monitor, including monitors with negative coordinates, remote sessions and fullscreen games/windows.
5. Confirm minimum and maximum dwell/edge preferences, plus persistence on restart.
6. Confirm P0 Weather, wallpaper/WebView2, DeskContainers, widgets, plugins, updater, safe mode and installer are unaffected.
7. Test the Personalization UI under Greek/English and varied DPI, ensuring the new sections scroll correctly.

**Status:** Source committed, not Windows-build-tested or installer-certified. This is an incremental P1 change, not the completed P1 release.
