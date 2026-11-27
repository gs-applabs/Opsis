# OPSIS — consolidated source working tree (R27)

Base: latest unified capability/DeskFlip PR #16, 96bd979a03b736c8d879add9e8af888295196e2f.

Selected non-overlapping changes, with all incoming commit references preserved in SOURCE-CONSOLIDATION.json. Conflicting files are not overwritten. This is a review branch, not a final release. User-side R20 source and original 110-wallpaper installer were not recoverable from Library bytes; preserve them. Existing 100/110 pack and Ancient Athens/Greek scenes must not be overwritten. Ancient Rome final art remains unverified. Keep only one released source ZIP after reconciliation, not earlier generated archives.

## Consolidation summary

| branch | copied | already same | overlaps | proposed deletions retained |
|---|---:|---:|---:|---:|
| appearance | 78 | 0 | 0 | 0 |
| desktop-materials | 5 | 0 | 3 | 0 |
| europe-R26 | 41 | 14 | 17 | 0 |
| europe-R25 | 1 | 0 | 0 | 0 |
| crawler-bridge | 1 | 0 | 4 | 0 |
| main-2.8-followup | 3 | 10 | 24 | 0 |

Legacy add-on handoff: 3 reference files. Its legacy code is documentation only; the active installer source is R26 and the real original add-on installer is missing.

## Available build artifacts (not a release)

- R26 installer: https://github.com/gs-applabs/Opsis/actions/runs/45744541834/artifacts/15082443211
- R26 compatible host: https://github.com/gs-applabs/Opsis/actions/runs/45744541834/artifacts/15082223673
- Photo masters/review layers: R22 15024050313, R23 15023840456, R25 15022663892. Keep newest revision per ID. Do not include rejected synthetic R7/R8/R10 or duplicate raw download batches in new release.

## Outstanding gates

Reconcile every overlapping code file and unrelated-history feature, compare against latest local integrated R20, verify original 110-wallpaper package, complete real-photo mask/geography/rights and remaining London/Podgorica, include Dublin R16 and confirm Ancient Rome. Re-run full Windows build, installer, UI, WebView2, plugin compatibility, security and performance. No final installer until these gates are met. Main is untouched.

## Automated three-way result

Cleanly reconciled: 14. Unresolved: 34.

- desktop-materials: .github/workflows/windows-audit.yml — workflow edits require connector review and workflows permission
- europe-R26: Opsis.csproj — Git three-way conflict
- europe-R26: src/Services/OpsisTaskbarDesigner.cs — Git three-way conflict
- europe-R26: src/UI/MainWindow.WallpaperGallery.cs — Git three-way conflict
- europe-R26: src/UI/MainWindow.cs — Git three-way conflict
- europe-R26: src/UI/ThemeManager.cs — Git three-way conflict
- europe-R26: src/Widgets/DeskFlipEngine.cs — Git three-way conflict
- europe-R26: src/Widgets/DesktopLauncher.cs — Git three-way conflict
- europe-R26: src/Widgets/ReplacementTaskbar.cs — Git three-way conflict
- europe-R26: src/Widgets/wallpaper/index.html — Git three-way conflict
- europe-R26: src/Widgets/wallpaper/weather.js — Git three-way conflict
- europe-R26: tests/Opsis.ExperienceSmoke/Program.cs — Git three-way conflict
- europe-R26: src/Services/WallpaperGalleryCatalog.cs — add/add or missing common base
- crawler-bridge: .github/workflows/plugin-compatibility.yml — workflow edits require connector review and workflows permission
- crawler-bridge: Opsis.csproj — Git three-way conflict
- main-2.8-followup: .github/workflows/windows-audit.yml — workflow edits require connector review and workflows permission
- main-2.8-followup: Opsis.csproj — do not downgrade RC; manually port relevant changes
- main-2.8-followup: Version.props — do not downgrade RC; manually port relevant changes
- main-2.8-followup: locales/ar-SA.json — Git three-way conflict
- main-2.8-followup: locales/de-DE.json — Git three-way conflict
- main-2.8-followup: locales/el-GR.json — Git three-way conflict
- main-2.8-followup: locales/en-US.json — Git three-way conflict
- main-2.8-followup: locales/es-ES.json — Git three-way conflict
- main-2.8-followup: locales/fr-FR.json — Git three-way conflict
- main-2.8-followup: locales/hi-IN.json — Git three-way conflict
- main-2.8-followup: locales/it-IT.json — Git three-way conflict
- main-2.8-followup: locales/ja-JP.json — Git three-way conflict
- main-2.8-followup: locales/ko-KR.json — Git three-way conflict
- main-2.8-followup: locales/pt-PT.json — Git three-way conflict
- main-2.8-followup: locales/ru-RU.json — Git three-way conflict
- main-2.8-followup: locales/tr-TR.json — Git three-way conflict
- main-2.8-followup: locales/zh-CN.json — Git three-way conflict
- main-2.8-followup: src/Widgets/wallpaper/index.html — Git three-way conflict
- main-2.8-followup: tests/WeatherInfoTone.test.cjs — add/add or missing common base

## R28 targeted reconciliation

Excluded documentation C# examples from application compilation (duplicate Gallery type). Ported R26 European photo discovery into current paginated Gallery, explicit preview labels and selection state. Ported mask handling into current native renderer, preserving existing photograph-ready logic. Retained current Glass Signature deduplication in WallpaperGalleryCatalog. Remaining conflict records: 31. Windows tests pending.

## R29 reconciliation

All 14 locale files retain their existing values and now include the missing localized VersionNotes.2.8.0.2511 entry. Version.props declares 3.0.0.3200 for the integrated RC, not a production release. The JSON ledger records these 15 resolutions; 16 conflict records remain. Windows build and installer verification are pending.

## R30 reconciliation

Ported opt-in Signature Composition project reference, native DLL staging and prerequisite checks while preserving default bundled Opsis.UI, SkiaSharp dependencies, .NET 11 and existing host exclusion globs. Three project-file overlap records resolved; 13 remain. Windows compile and optional Signature build are not yet verified. WeatherInfoTone overlap deliberately remains open: incoming automatic dark-text behavior conflicts with the user's fixed-white desktop text requirement; reconcile HTML and regression tests together.

## R31 reconciliation

Retained fixed-white weather text in plain and Signature card layouts; legacy adaptive black text is intentionally superseded. R26 additions already present except the obsolete contrast sampling guard. Added regression assertions for both layouts. Existing StartStudioPreferences.StartButtonStyle and OpsisStartButtonArt supersede the proposed duplicate StartGlassIcon boolean. Four records resolved by policy/source comparison; nine remain. Runtime tests pending. ReplacementTaskbar styling and animation overlap remains open.

## R32 reconciliation

Ported SignatureGlass named palette and dark-intensity guard; retained accent settings and traced repaint. Integrated Launcher all-term search, high-contrast field colors, Signature shortcut styling and branded Start header while preserving power actions, Quick Look and shortcut diagnostics. Two conflict records resolved; seven remain. Windows compile and UI tests pending.

## R33–R34 reliability and navigation

R33 adds guarded UI-thread shutdown, per-stage exception isolation and diagnostics, stops only started plugins, and routes version recovery through the tray exit path. A hung synchronous stop remains a release blocker. R34 aligns classic navigation ordering and Studio groups (Appearance, Desktop, Media, System, Add-ons); preserves shortcuts and About last. About separates host, plugins, family, dependencies and consolidated guidance. Pending: interactive Windows control checks, hung-stop reproduction and bounded recovery design, per-plugin cards, intra-page duplicate audit, seven unresolved source overlaps and Windows build. No release certification.
