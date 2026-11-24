# OPSIS — Ενιαίο οικοσύστημα P3–P5 (2.8.0.2510 branch family)

## Architecture

`OpsisModCore` is the registry for individual Winhawk-style feature switches. `OpsisEcosystemGallery` provides a shared read-only manifest for wallpapers, widget assets, icon packs, layouts and plugin contributions; it does **not** execute third-party code merely to list assets. Existing Windows and Opsis settings remain in their respective engines instead of being duplicated.

All current and future additions use common application storage, theming and recovery. A contribution is allowed to live as part of the shared catalog or as an independent component. Each implementation must declare supported Windows builds, dependency and conflict policy. Optional capabilities are off unless expressly enabled.

## The five priority features

| Feature | Implemented scope | Not yet included |
|---|---|---|
| Compatibility Guard | Windows build/UBR/fingerprint baseline at startup + 15-minute registry checks; independent hold for Opsis taskbar replacement and automation; explicit approval | Automated compatibility certification against every Windows Insider build or all Windhawk mods |
| Recovery Center 2.0 | Unified settings backups, hash verification/restore, Windows shell file diagnostics, bounded copy of six unmodified Windows shell files for reference, user-requested native System Restore point | Full Windows bare-metal backup, arbitrary OS file overwrite, guaranteed restore point |
| Start Studio 2.0 | Search Start Menu shortcuts, pin favourites, grouped listing, launch user-selected shortcuts | Full replacement of the Windows Start menu via Explorer hooks |
| Weather Scene Composer | Save/read/validate/apply combinations of real Weather Wallpaper options, Greek landmarks and time/wind simulations, backup before apply | Full 3D landmark modelling (separate Weather PR #8) |
| Automation Studio | Opt-in Startup/Daily/Weekday rules for Scene, Appearance Preset, Theme Mode, Hot Corners; pure dry-run scheduler and rule validation | Arbitrary shell scripts, external command execution, network automation |

## Remaining features from the 30-item proposal

| Item | Status in codebase |
|---|---|
| Taskbar Designer | Native settings and Opsis replacement already exist; advanced visual designer still planned |
| Explorer Studio 2.0 | Individual Explorer settings exist; richer toolbar/tab customization planned |
| Context Menu Studio | Added opt-in, HKCU-only, owner-validated Opsis launcher on Explorer/desktop background context menus |
| Window Rules Engine | Added: foreground-only positioning/topmost with rollback and safe defaults |
| Advanced Snap Layouts | Existing AutoTile and layouts; additional custom editing can be extended |
| Desktop Spaces | Existing VirtualWorkspaces |
| Profile Scheduler | Implemented in Automation Studio for scenes and appearance presets |
| Dynamic Accent Engine | Implemented: source-image color extraction or Weather scene palette, Opsis-only theme refresh every 15 minutes when enabled |
| Animated Taskbar Effects | Planned |
| Icon Pack Manager 2.0 | Existing IconAtlas and icon-pack import |
| Cursor FX Studio | Implemented opt-in halo/ripple overlay without hooks or injection; pauses in fullscreen and optional battery mode |
| Advanced Lock Screen | Existing settings; more advanced visual content planned |
| Screen Saver Creator | Windows native controls exist; dedicated creator planned |
| Theme Marketplace | Offline plugin/Gallery contributions including validated `.opsis-theme` import; online marketplace still planned |
| Theme Import / Export | Data-only `.opsis-theme` ZIP for Appearance Preset and Weather Scene, importable from plugins/Gallery with integrity checks and overwrite protection |
| Multi-monitor Studio | Existing per-monitor wallpaper/profile and topology services |
| Performance Auto-Tuner | Existing performance profiles and adaptive wallpaper scaling |
| GPU Effects Manager | Existing GPU/performance monitoring; advanced per-effect tuning planned |
| Wallpaper Scene Composer | Implemented for existing Weather parameters |
| Weather Scene Rules | Available as safe Automation Studio rules |
| Widget Designer | Native Widget Layout Studio implemented for Clock/Date/Text/CPU/RAM/Battery; advanced drag-and-drop remains planned |
| Widget Data Sources | Native CPU/RAM/Battery bindings in Widget Layout Studio; shared plugin contribution manifests; richer online data sources remain planned |
| Command Palette 2.0 | Existing Opsis Command Palette; deeper cross-plugin command indexing planned |
| Desktop Diagnostics | Existing diagnostics + new update impact hash inventory |
| Compatibility Guard | New update review service |
| Recovery Center 2.0 | New unified recovery store and Windows Protection controls |
| Plugin SDK 2.0 | Existing SDK/approval flow; new contribution contract, further permissions planned |
| Unified Opsis Control Center | Existing main UI with grouped ecosystem catalog |

## Windows system files — crucial recovery policy

Windows Resource Protection files must NOT be replaced by dragging or copying stored backups. `RecoveryHub/WindowsOriginals` is **diagnostic evidence only**, a bounded sample of original binary versions and hashes. Windows servicing can change thousands of files; the sample is not a full system image. When corruption is suspected, follow Microsoft DISM/SFC procedures, or use a Windows System Restore point (subject to Windows policy/elevation and typically one point per 24 hours).

## Security model

- Opt-in: Window Rules and Automation Studio remain disabled by default.
- No DLL injection, Shell32 patching, global Windows hooks or untrusted script execution.
- Gallery contributions are declarative `opsis-contributions.json` files, categorized by type and category; only static known image formats may be applied, after user confirmation.
- Themes include JSON settings only, never scripts or executable payloads.
- Windows update change requires explicit approval for high-impact services; the risk-sensitive native Taskbar replacement stays held at startup.
- All plugin binaries continue through the existing PluginHost trust controls, independent of the Gallery catalog.
- Windows binaries are never overwritten by Opsis.

## GitHub validation

CI: `.github/workflows/p3-p5-unified.yml`, matrix Windows Server 2022 / 2025, .NET 11, isolated experience smoke tests, P1/P2 regression and portable publish.

Remaining release gates: Windows 11 desktop UI/manual screenshot testing, multi-monitor/high DPI, protected Windows shell behavior, System Restore UAC, real wallpaper shader resources and full plugin-integration certification. Do not label all 30 features complete until their planned components have actual implementation and runtime tests.


## Additional completed integrations after first P3–P5 baseline

- `OpsisContextMenuStudio`: named, reversible context menu entries for the Opsis launcher, restricted to HKCU and an Opsis ownership marker. No shell extension DLL is loaded.
- `WallpaperAccentEngine`: samples color from local image wallpapers or selects a time-sensitive Weather scene color; applies to Opsis theme tokens rather than Windows registry. Opt-in refreshing every 15 minutes, and no file writes when unchanged.
- `OpsisWidgetDesigner` + `OpsisCustomWidgetWindow`: declarative user widgets with Clock, Date, Text, CPU, RAM and Battery data. All use the same host process and monitoring APIs; no third-party script engines.
- `OpsisFeatureSwitcher`: safe, real operational adapters for selected modules, backed by the shared catalog and Settings/Recovery, rather than a metadata-only on/off label.
- `OpsisEcosystemGallery`: installed theme packs and native declarative widgets from plugins join contributed static wallpapers in the same typed, categorized Gallery.
- `OpsisRecoveryHub`: includes native Explorer/Taskbar original-setting backups, wallpaper, app settings, themes, widget designs, Window Rules and automation rules. A one-shot post-update shell hash inventory writes a durable change report, with no full-disk scans during normal idle.
- `Opsis P3-P5 Windows installer integration`: automated Inno Setup build, clean install, in-place reinstall and silent uninstall in both GitHub Windows VM images.

## Remaining gaps / strict release qualification

Full shell patching and Start replacement, pixel-accurate Taskbar Designer, direct Rainmeter skin execution, third-party Windhawk mod integration, deep Explorer context-menu rewriting, a drag-and-drop visual widget layout canvas, cursor click animations, independent 3D screensaver executables and a downloadable online marketplace are **not** complete. The native widget studio is a safe preset-based creator, not yet a free-position drag-and-drop designer. Changes must pass the current head CI and real Windows 11 desktop interaction tests before merge or release.


## New delivered work — Visual Widget / Rainmeter bridge / grouped Gallery

- Native **Visual Widget Designer**: mouse drag within a guarded canvas, live preview of data-binding/text/accent/font/opacity, normalized content X/Y positions, border setting, backed-up updates and Save As. The standalone widget renderer now honors the same layout positions and border.
- **Rainmeter compatibility subset**: read-only parsing of small `.ini` skins with exactly one `String` meter and optionally a built-in `Time` measure, translating to native Opsis widgets. Explicitly rejects `!Bang` commands, Lua/script/plugin measures, unsupported meter types, dynamic variables, embedded expressions, large/duplicate INI sections and unknown settings. This is **not** the entire Rainmeter engine.
- Common plugin Gallery now supports safe Rainmeter INI previews as native widgets in addition to `opsis-contributions.json` wallpapers, regular widget JSON and `.opsis-theme` theme packs.
- Gallery users can **filter by type, category and text**, with automatic group headers; the same Gallery is reused for all plugin contributions instead of a separate app per content type.
- Widget tests cover geometry/clamping, persisted drag positions, invalid 0–100 coordinates, GDI-backed native forms on an STA thread, Rainmeter parsing/blocked directives, plugin contributions and restoration of widget design backups.
- Build workflow now also invokes the **published Opsis.exe --run-selftest** with an isolated data directory; the test verifies that a machine-readable JSON report and four checks are present. The two Windows VM runs must complete before these additions can be called verified.

### Current integration boundaries

The importers do not execute Windhawk mods, install third-party Rainmeter DLLs or alter protected Explorer/Taskbar code. Rich Rainmeter syntax remains unsupported and must report an explicit validation error. Full visual customization of the native Windows Start/Taskbar and context-menu command extensions remains future work. No final release or installer should be published without passing the latest Windows build, security and installer workflows.


## New Cursor FX Studio (native, opt-in)

`src/UI/OpsisCursorFx.cs` provides a tiny transparent click-through WinForms overlay showing a pointer halo, a fading click ripple, or both. It does not need Windhawk binaries, external mod injections, or Windows shell modification. Style and intensity are stored in `AppSettings`, exposed through the common Personalization page and the `OpsisFeatureSwitcher` module adapter, disabled by default. A 50ms timer runs only while explicitly enabled. Wallpaper rendering and widget plugins are independent. The overlay suppresses on foreground fullscreen windows and when operating on battery according to pause settings. All configuration/compatibility reads are cached to avoid frequent SSD writes. Native pointer trails remain a separate Windows setting.
