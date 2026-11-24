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
| Context Menu Studio | Planned |
| Window Rules Engine | Added: foreground-only positioning/topmost with rollback and safe defaults |
| Advanced Snap Layouts | Existing AutoTile and layouts; additional custom editing can be extended |
| Desktop Spaces | Existing VirtualWorkspaces |
| Profile Scheduler | Implemented in Automation Studio for scenes and appearance presets |
| Dynamic Accent Engine | Planned |
| Animated Taskbar Effects | Planned |
| Icon Pack Manager 2.0 | Existing IconAtlas and icon-pack import |
| Cursor FX Studio | Native pointer trails exist; ripple/spotlight animations planned |
| Advanced Lock Screen | Existing settings; more advanced visual content planned |
| Screen Saver Creator | Windows native controls exist; dedicated creator planned |
| Theme Marketplace | Local plugin/Gallery contributions; online marketplace planned |
| Theme Import / Export | Data-only `.opsis-theme` ZIP for Appearance Preset and Weather Scene with validation and overwrite protection |
| Multi-monitor Studio | Existing per-monitor wallpaper/profile and topology services |
| Performance Auto-Tuner | Existing performance profiles and adaptive wallpaper scaling |
| GPU Effects Manager | Existing GPU/performance monitoring; advanced per-effect tuning planned |
| Wallpaper Scene Composer | Implemented for existing Weather parameters |
| Weather Scene Rules | Available as safe Automation Studio rules |
| Widget Designer | Planned |
| Widget Data Sources | Existing Widget host and plugin feeds; standardized contribution manifest added |
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
