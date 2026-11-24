# 3.0.0.3200 — P0/P1 incremental integration (24 November 2026)
Base: ec979ce91131ec84501046878d27d685a75dde4b. Not a release. Original readiness manifest still blocks release.

| Task | Owner | State |
|---|---|---|
| P0 module/option aliases and unchanged writes | OpsisModCore | source changed, compile pending |
| P0 RC build recovery | existing CI | CS0136 top/bottom shadowing fixed; new compile pending |
| P0 native Windhawk capability equivalence | OpsisIntegrationAudit / OpsisModCore | per-mod installed-registry inventory added; actual injection, portable storage and measured comparisons pending |
| P1 DeskFlip Personalization | MainWindow / DeskFlipEngine | source changed, compile and GUI pending |
| P1 3 visual styles (Flip 3D, thumbnail grid, compact list) | same DeskFlip overlay / capture | source changed; actual GUI and performance pending |
| P1 Rainmeter compatible data/skins | existing WidgetEngine / designer / importer | partial, one String/Time importer exists; no secondary widget host |
| P1 Start/taskbar/Explorer | existing studios | preserve PRs 10, 13, 14; full native shell replacement not certified |
| P2 appearance presets and skins | AppearancePresets / ThemeManager | existing, regression pending |
| P3 wallpapers add-on, weather, screensaver | WallpaperHostEngine / Gallery | partial; packaging and validation pending |
| P4 plugin compatibility, HDR/DPI/multi-monitor | existing hosts | pending |
| P5 performance/VM/installer/regression | existing CI | pending; previous RC, comparison, installer and plugin runs failed |

No third-party binaries, native Alt+Tab interception, new service or parallel timer. Compact list skips window and desktop captures; flat layouts skip animation. Flip 3D retains default. No claims of comparative performance before tests. No module flag represents a separate switcher. No production version bump.

Benchmark criteria: same workload, resolution and DPI; CPU/GPU, memory, GDI handles, disk writes, input latency; retain compatibility and rollback. Add new looks as styles, not independent engines. Rainmeter native importer remains fail-closed for unsupported executable components. DeskFlip PrintWindow remains a potential blocking capture path; investigate before release.

## Canonical user task list (preserve IDs and priorities)

| ID | Priority | Task | Verified baseline / current work |
|---:|---|---|---|
| 1 | P0 | Full native Start Menu customization | Start Studio is an Opsis launcher, not native Start; native integration pending |
| 2 | P0 | Advanced Explorer Styler | ExplorerViewProfiles + Explorer Studio; native visual styling pending |
| 3 | P0 | Native Taskbar customization | TaskbarViewProfiles has four HKCU settings; full native visual controls pending |
| 4 | P1 | Taskbar on all screen edges | ReplacementTaskbar follows native bounds; independent native placement pending |
| 5 | P1 | Floating Taskbar | pending |
| 6 | P1 | Segmented Taskbar | pending |
| 7 | P1 | Dynamic Taskbar sizing | existing designer button sizing; native dynamic height pending |
| 8 | P1 | Custom Start power menus | verify DesktopLauncher; native integration pending |
| 9 | P1 | Explorer Command Bar customization | pending |
| 10 | P1 | Advanced Explorer tabs | pending |
| 11 | P1 | Taskbar thumbnail customization | pending; DeskFlip is separate |
| 12 | P1 | System Tray Icon Studio | existing Windows tray access is not a styler; pending |
| 13 | P2 | Notification Center Styler | pending |
| 14 | P2 | Quick Settings Styler | pending |
| 15 | P2 | Volume Flyout customization | pending; existing audio controls do not equal native flyout styling |
| 16 | P2 | Independent appearance profiles per monitor | partial; existing per-monitor wallpaper profiles not full shell profiles |
| 17 | P2 | Custom Taskbar gestures | pending |
| 18 | P0 | Conflicts per enabled Windhawk mod | read-only registry inventory; explicit competing taskbar styles hold Opsis overlay; portable/per-user scopes and live injection not verified |
| 19 | P0 | Automatic isolation after Windows Update | existing UpdateGuard now latches on state failure and isolates running taskbar, tiling, window rules, hot corners, cursor FX, appearance and plugin starts; tests pending |
| 20 | P1 | Shared Shell visual profile editor | AppearancePresets + existing studios; unified native shell profiles pending |

P0 order: 18, 19, 1, 2, 3. CI compilation repair is a cross-cutting prerequisite, not a replacement for any numbered task. P1: 20, then 4–12. P2: 13–17. DeskFlip, Rainmeter and wallpaper tasks remain supplementary and do not count as completion of these twenty. No benchmark winner until measured. Do not equate Opsis overlay styling with native Windows shell styling.

CI evidence: RC run 45389933784 and comparison 45389933797, installer 45389933931, plugin 45389933822 all reported CS0136 in OpsisStartButtonArt.cs. Electron job passed. Fixed only local variable names; no skin change. Added RC target branch to existing PR workflow filters. New CI results pending. Registry location and Disabled semantics referenced from https://github.com/ramensoftware/windhawk/discussions/73 and https://github.com/ramensoftware/windhawk/discussions/155. No third-party settings are written.

### P0 18/19 — runtime isolation increment
No new service or timer. Existing 60-second experience timer checks persisted review and refreshes opted-in taskbar conflict scan; Windows fingerprint interval remains 15 minutes. Explicit configured competing taskbar style/geometry mods hold Opsis overlay without changing Windhawk; unknown mods remain advisory. Portable configuration and actual injection still not certified. Existing WindowsUpdateGuard now fails closed on unreadable or invalid state, latches before writes, and clears on successful user approval. Runtime stop is best-effort; in-process plugin DLLs remain resident, so restart is required after approval. Existing preferences are not overwritten. Runtime isolation does not prove native Shell modification compatibility. CI must pass before claiming completion.
