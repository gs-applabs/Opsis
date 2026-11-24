# OPSIS — Integration Audit / Roadmap 3.0.0.3200

**Status: pre-release engineering.** The canonical shipped application version in `Version.props` remains **2.8.0.2510**. The requested **3.0.0.3200** is reserved and may only be stamped after every mandatory release gate has passed.

## 1. What was actually integrated

The integration branch `feat/3.0.0.3200-integration` is based on `feat/p3-p5-unified-experience`, which stacks the P0 / P1+P2 / unified-core / P3–P5 changes. The Weather Wallpaper 2.5D/3D PR #8 is based on an earlier P1 branch and therefore was **not previously included** in P3–P5. Its weather JS modules, sky masking, atmosphere, HTML bridge, wallpaper-host persistence, WebGL2 fallback, settings selector, scene Gallery and Windows tests have now been copied or carefully integrated into this branch. The large P3–P5 `MainWindow.cs` was **not replaced** by the older Weather branch: only the Weather depth selector hook was inserted.

Both the WebGL2 3D precipitation and 2.5D depth effects are opt-in and fall back to the 2D renderer. This is not yet a verified fully modelled 3D Greek city/landmark renderer.

## 2. Verified 2.8.0.2510 baseline

**Source-backed baseline**: `src/Services/OpsisIntegrationAudit.cs` declares **26 native subsystems/features** against the actual 2.8.0.2510 implementation. The baseline file paths were checked directly in `release/2.8.0.2510`.

**Configuration inventory**: `docs/OPSIS-2.8.0.2510-SETTINGS-BASELINE.json` contains **205 unique public auto-properties** with declaring class, type, source file and baseline line number: 100 AppSettings, 69 WallpaperSettings, 17 DeskStripSettings, 6 WavePalette, 4 WallpaperScheduleRule, 5 WallpaperElement and 4 WallpaperSkylineEdit.

**Integration schema delta**: `docs/OPSIS-3.0.0.3200-SETTINGS-DELTA.json` identifies **17 additive configuration properties**, **0 removals**, **0 type changes** and **0 duplicate property keys** in these classes. This is a source-level schema check, **not** proof that every serialized user profile or visual feature has passed runtime tests. Other settings classes and manual UI commands remain outside this exact 205-key inventory and require ongoing audit.

## 3. Single-owner architecture: no duplicate engines

| Feature area | Authoritative existing 2.8 owner | 3.0 integration policy |
|---|---|---|
| Static, video, Waves and Weather Wallpaper | `WallpaperHostEngine` in `WallpaperWindow.cs` | Weather spatial shaders and Gallery are contributors to the **same** engine; never create a second desktop renderer |
| Weather updates, time, wind and landmarks | Existing wallpaper host + Weather JS | Scene Composer, spatial modes and addons write native settings and consume the same render pipeline |
| Taskbar replacement | `ReplacementTaskbar` and its original `AppSettings.ReplaceWindowsTaskbar` | Taskbar Designer only changes visuals of this existing overlay, **not** a second taskbar process |
| DeskStrip | `DeskStripHostEngine` | Remains an independent dock as in 2.8; warn when displayed alongside the Opsis taskbar |
| Desktop Toolbar | `DesktopToolbar` | No new toolbar service; warn about possible overlapping screen-edge positioning |
| Window arrangement | `AutoTileService` / workspace controls | Window Rules cannot simultaneously reposition windows while AutoTile is enabled |
| Windows Explorer settings | Existing Explorer/Windows settings services | Explorer Studio uses the same HKCU options and original-value recovery, no new DLL injection |
| Theme and accent | `ThemeManager` | Dynamic Accent computes palette input only; original theme service owns application styling |
| CPU/RAM/battery telemetry | `AdvancedSystemMonitorService` | Native Widget Studio reads existing snapshots, not a competing polling/telemetry service |
| Widgets and built-in replacement | Existing widget host + `PluginHost.FindWidgetReplacing` | New native layouts and declarative plugin widgets use existing host mechanisms; plugin replacement is not duplicated |
| Runtime plugin DLLs | `PluginHost` + `PluginTrust` | **Only PluginHost** activates DLLs after SHA-bound approval; new Gallery only reads static metadata/assets |
| Plugin install paths | `PluginHost.EnumerateCandidateFolders` | Gallery now uses the **same** roots, including registered `HKCU\\Software\\Opsis\\PluginPaths` entries |
| Duplicate plugin IDs | Existing PluginHost version comparison | Already keeps only the newest compatible instance for a given ID; inventory reports unexpected duplicates |
| Legacy Appearance plugin | `PluginHost.IsReplacedByBuiltIn` | Former `OpsisAppearancePlugin.dll` remains excluded; native Appearance is the owner |
| Command Palette | Existing `MainWindow.ShowCommandPalette` | Index native studios/modules/plugin pages rather than creating a separate command service |
| Lock screen, visualizer, icon hiding and original settings | Existing 2.8 services | Preserve existing behavior and data, with shared recovery and explicit optional features |

**Conflict safety:** `OpsisIntegrationAudit` checks registered module IDs, loaded plugin identities, duplicate Gallery contribution origins, shell replacement collisions, AutoTile vs Window Rules, overlapping docks and two competing accent preferences. The Opsis replacement taskbar is held when StartAllBack/ExplorerPatcher is detected as a high-risk competing shell owner; original Windows taskbar files are never overwritten. External applications are **not** silently uninstalled, killed or modified.

**Installed plugin inventory:** the audit distinguishes candidate installed folders (without executing their code), pending user trust approvals, loaded identities and disabled/enabled runtime plugins. It does not claim to certify a DLL merely because it is present. Gallery metadata may be listed without loading the DLL; explicit actions are required to apply a wallpaper or import a theme. Duplicate `PluginId:ItemId` contributions appear once; a higher valid manifest version takes precedence, with all sources retained in diagnostic results.

## 4. Remaining work before a true 3.0.0.3200

The following larger original requests remain incomplete or require substantial real Windows 11 testing: full Windows Start replacement, advanced native Windows Taskbar and Explorer modifications across builds, Windhawk mod parity, full Rainmeter plugin/Lua engine, actual Windows `.scr` installation, downloadable online Theme Marketplace, deeper plugin SDK permission isolation, exhaustive live installed-plugin tests, 3D city/landmark geometry, Windows 11 GPU + DPI + fullscreen + multiple monitors, unattended update/migration rollback and packaging a production release.

This audit intentionally **does not** convert "planned" flags into "finished" implementations.

## 5. Hard release gate

Machine-readable state: `release/3.0.0.3200-readiness.json`.

- `tests/integration/check_baseline_and_release.py` fails on any removed/retyped 2.8 configuration key, unreviewed additions or a premature 3.0 version bump.
- `.github/workflows/3.0-integration.yml` runs both Windows 2022/2025 builds, safety/stress regressions, portable Opsis.exe self-test, Weather JS/WebGL fallback and real WebView2 captures.
- `.github/workflows/3.0-installer-e2e.yml` builds and verifies the installer across clean install, reinstall and uninstall Windows VM lifecycles.
- Plugin compatibility, SHA-bound trust/approval and replacement of built-in widgets require E2E checks with the actual installed plugin binaries.
- Full Windows 11 desktop visual tests and product security review remain mandatory and cannot be inferred from Windows Server GitHub Actions alone.

**Do not change `Version.props` to 3.0.0.3200, tag a final release, or merge this integration branch into the shipping release until all gates are green.** Keeping the canonical identity at 2.8.0.2510 prevents a preview build from being misidentified as the finished 3.0 product.
