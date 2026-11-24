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
| Taskbar Designer | Native Opsis Taskbar Designer for classic/compact/glass, alignment, width, gap and opacity; Windows taskbar remains untouched |
| Explorer Studio 2.0 | Visual reversible HKCU options (extensions, hidden items, compact spacing, startup location); toolbar/tab control remains planned |
| Context Menu Studio | Added opt-in, HKCU-only, owner-validated Opsis launcher on Explorer/desktop background context menus |
| Window Rules Engine | Added: foreground-only positioning/topmost with rollback and safe defaults |
| Advanced Snap Layouts | Existing AutoTile and layouts; additional custom editing can be extended |
| Desktop Spaces | Existing VirtualWorkspaces |
| Profile Scheduler | Implemented in Automation Studio for scenes and appearance presets |
| Dynamic Accent Engine | Implemented: source-image color extraction or Weather scene palette, Opsis-only theme refresh every 15 minutes when enabled |
| Animated Taskbar Effects | Optional slow active-button pulse on Opsis replacement bar; advanced Acrylic/Mica motion effects remain planned |
| Icon Pack Manager 2.0 | Existing IconAtlas and icon-pack import |
| Cursor FX Studio | Implemented opt-in halo/ripple overlay without hooks or injection; pauses in fullscreen and optional battery mode |
| Advanced Lock Screen | Existing settings; more advanced visual content planned |
| Screen Saver Creator | Native clock/image scene preset builder and fullscreen preview; standalone Windows .scr registration remains planned |
| Theme Marketplace | Offline plugin/Gallery contributions including validated `.opsis-theme` import; online marketplace still planned |
| Theme Import / Export | Data-only `.opsis-theme` ZIP for Appearance Preset and Weather Scene, importable from plugins/Gallery with integrity checks and overwrite protection |
| Multi-monitor Studio | Existing per-monitor wallpaper/profile and topology services |
| Performance Auto-Tuner | Existing performance profiles and adaptive wallpaper scaling |
| GPU Effects Manager | Existing GPU/performance monitoring; advanced per-effect tuning planned |
| Wallpaper Scene Composer | Implemented for existing Weather parameters |
| Weather Scene Rules | Available as safe Automation Studio rules |
| Widget Designer | Native drag-and-drop position editor, preview, save/as, Clock/Date/Text/CPU/RAM/Battery bindings |
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


## Follow-up: staged native Shell Studio and screensaver implementation

Implemented in `feat/p3-p5-unified-experience` after the initial P3–P5 delivery:

1. **Start Studio layouts**: list, tiles and compact presentation, optional grouping by Start Menu folder; pinned shortcuts persist in `start-studio.json`. This is a launcher, NOT a Windows Start-menu injection/replacement.
2. **Opsis Taskbar Designer**: separate visual editor for classic/compact/glass look, approximate left/center arrangement, item width/gaps, opacity, optional low-frequency active button pulse, show/hide Task View and clock. It operates on the **Opsis ReplacementTaskbar** only, never patches native Windows taskbar. The 5-second read cache avoids repeated settings-file polling; profile is backed up by Recovery Hub.
3. **Explorer Studio 2.0 — safe subset**: editor for the four explicit HKCU preferences already supported by `ExplorerViewProfiles`. Preserves backup and supports original settings restore. Does not inject into the Explorer toolbar or tabs.
4. **External shell mod coexistence**: read-only scan of well-known installed/running shell products (StartAllBack, ExplorerPatcher, Windhawk, Rainmeter, TranslucentTB) that returns advisory risk classes. Unknown individual Windhawk mod configurations are **not** assumed to be verified safe.
5. **Screensaver Studio — safe preview**: native fullscreen clock, static wallpaper image, or clock-with-image scene; dimming, seconds and user-triggered preview. Does not register a .scr file or change Windows screen saver settings without a separate supported installer.
6. **Safety and tests**: all scene/profile files are in common Opsis recovery. Windows smoke adds invalid-schema rejection, complete Taskbar and Start style roundtrips, mod coexistence analysis, screensaver validation and real STA Win32 handle construction for Shell and Screensaver Studio.

### Delivery / test gate

GitHub-hosted Windows 2022/2025 workflows are configured for build, executable self-test, regression and Inno installer full lifecycle, with separate plugin compatibility. These latest jobs have been queued; **a queued workflow is not a passed test**. Do not issue production release/merge until the latest head passes and Windows 11 interactive GUI/high-DPI/multi-monitor checks are completed.

### Still not delivered from initial 30-item list

- True Windows Start replacement, low-level StartAllBack/ExplorerPatcher/Windhawk shell equivalents.
- Full Explorer toolbar/tab command replacement and universal context menu editor.
- General-purpose Rainmeter Lua/plugin/measure runtime.
- True per-element Acrylic/Mica taskbar animation and arbitrarily configurable native Windows taskbar.
- Native Windows .scr installer screensaver, online Theme Marketplace, complete dynamic profile automation and richer cross-plugin command registry.
- Full 3D weather landmarks are isolated in PR #8, not merged into this branch.
