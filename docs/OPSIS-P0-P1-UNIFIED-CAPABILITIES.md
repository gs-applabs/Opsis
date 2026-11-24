# 3.0.0.3200 — P0/P1 incremental integration (24 November 2026)
Base: ec979ce91131ec84501046878d27d685a75dde4b. Not a release. Original readiness manifest still blocks release.

| Task | Owner | State |
|---|---|---|
| P0 module/option aliases and unchanged writes | OpsisModCore | source changed, compile pending |
| P0 RC build recovery | existing CI | failed on Windows 2022/2025 at candidate build; investigate compiler output |
| P0 native Windhawk capability equivalence | OpsisIntegrationAudit / OpsisModCore | partial; inspect enabled plugins at runtime and compare measured providers |
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
