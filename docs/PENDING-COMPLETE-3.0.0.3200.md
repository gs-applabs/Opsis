# Opsis 3.0.0.3200 — complete list of pending and half-done work

Base: branch `integration/r28-on-3.0-mine` = my 3.0 patch build (`patch/3.0.0.3200-appearance`) + the R27/R28 unified source (`integration/opsis-unified-source-r27`, a5e61ef). Status date: 2026-11-26.
Legend: **DONE** verified here · **PARTIAL** (half-finished, what is missing is stated) · **OPEN** not started · **BLOCKED** needs something outside the code. Anything not run on a real Windows 11 machine is **NOT VERIFIED**.
Detailed acceptance criteria stay in `REMAINING-TASKS-3.0.0.3200.md` (IDs unchanged).

## A. Done and verified (build 0 errors, self-test 0, node tests 51/51, ExperienceSmoke 170, RcUiSmoke PASS, SignatureExplorerSmoke PASS)
- Audit hang root cause fixed; Windows audit PASS on release / patch branches (R28 merge run pending).
- 2.8.0.2510 and 2.8.0.2511 released; `main` holds the 2.8.0.2511 tree.
- #19 Update Guard: process latch kept until restart (+ `RestartRequired`, tests).
- #3 native taskbar auto-hide: shell check, backup hygiene.
- #10 Explorer capture: bounded STA worker, 5 s timeout, tests.
- W5: real 200 static WebP pack imports (200 distinct, static, decoded); import off the UI thread.
- S1/S2 appearance patch (Start buttons, Opsis 3 logo, Signature R14, unified Gallery).
- R28 source merged: Europe photo catalog, glass / desktop-materials follow-ups, brand mark, plugin host updates, SignatureExplorerSmoke.
- Stale expectations fixed: `check-plugin-dll-versions.ps1` + `PluginReleaseGate` test (contract 1.0.0.0, Opsis.UI 0.6.0.0), version-identity test, `suite/plugin-compatibility.json` (contract 1.0.0.0, Opsis.UI 0.6.0.0, Crawler 0.28.1, Chronikologio 1.1.0).
- Start screen, About notes, README, 14-language locales (2.8.0.2511 + 3.0 notes).
- Wallpaper add-on 1.3.0 (112 wallpapers incl. Water Ripples and Christmas Spectrum), install/uninstall verified on a temp root.
- Opsis.UI: v0.6.1 tag; final 0.6.0 DLLs committed (locally) in Crawler, Appearance, Chronikologio and Opsis 2.6.0.
- #18 Windhawk (registry/INI inventory only, no CLI) and #8 power menu (confirm, no `/f`) verified as already compliant.

## B. Half-done (PARTIAL)
| ID | Item | What is missing |
|---|---|---|
| B1 | #3 Native Taskbar | registry build gating per Win11 build, no-duplicate-owner check, native look where supported |
| B2 | #19 Update Guard | UBR-change and invalid-fingerprint cases with the latch in a real second process; UI text in 14 languages for `RestartRequired` |
| B3 | #10 Explorer tabs | tab order / group / save / restore, same-window restore, real Win11 UI-Automation evidence |
| B4 | W5 / Gallery | paging UI with the 200 images, bounded thumbnail cache, no stale highlight: visual check on Windows |
| B5 | #18 Windhawk | per-surface compatibility, proof that a mod is really active, fail-closed conflict handling |
| B6 | #8 Start power menu | keyboard navigation and menu configuration tests |
| B7 | Add-on 1.3.0 | real install on a real Opsis folder; upgrade from 1.2.0 is refused by `PrepareToInstall` (uninstall 1.2.0 first) — decide: allow in-place upgrade |
| B8 | Europe capitals (R26–R28) | 44 of 49 have a payload; Amsterdam and Prague masks unusable; 3 missing (incl. London, Podgorica); Dublin R16 to include; Ancient Rome to confirm; real-photo rights / geography / masks; installer of the preview (442 MB, unsigned) not run |
| B9 | R27 unresolved overlaps | 34 files recorded as unresolved in `SOURCE-CONSOLIDATION.md`: `Opsis.csproj`, `OpsisTaskbarDesigner.cs`, `DeskFlipEngine.cs`, `DesktopLauncher.cs`, `ReplacementTaskbar.cs`, `ThemeManager.cs`, `wallpaper/index.html`, `weather.js`, locales (12 files, `main-2.8-followup`), `WeatherInfoTone.test.cjs`, workflows (need connector permission) — the merge resolved only `MainWindow.WallpaperGallery.cs`; the rest must be compared file by file |
| B10 | Opsis.UI libs | commits in Crawler / Appearance / Chronikologio / Opsis 2.6.0 are local, not pushed; `_crawler28` and `p3-src` still hold older DLL copies |
| B11 | Other apps | GearWin / Kombos branches `opsis-detect-exe-only` and EnfiaPro `net11` (WPF styles) are local; Media Suite contract work reported by its session only; Appearance hygiene test fails on `docs/PLAN-INTAKE.md` |
| B12 | Secondary windows | styler done; no automated GUI/function audit yet (C3); containers menus and native `MessageBox` not styled (C4) |
| B13 | 3.0 start screen / notes | texts are in; visual check in every language NOT VERIFIED |
| B14 | In-app history/help/instructions | family state refresh requested by another session (Crawler 0.28.1, Chronikologio 1.1.0, GearWin 7.0.0, Opsis.UI 0.6.0), 14 languages, README, CLAUDE.md, CHANGELOG — queued, not started |

### B9 triage result (2026-11-26, stage 1) — B9 is CLOSED
31 unresolved entries compared three-way with this build:
- 14 locale files: only `VersionNotes.2.8.0.2511` was missing — added.
- `Opsis.csproj` (`_compat` excludes) already present; `Version.props` nothing to add.
- Ported: DeskFlip uses the plain list in high contrast; the replacement taskbar uses system colours and no window opacity in high contrast.
- NOT ported, moved to S3 (Signature Glass / Opsis.UI 6, stage 2): the `SignatureGlass` theme, Signature styling of launcher / taskbar / DeskFlip layouts, animated brand mark (this build keeps the static Opsis 3 logo), composition preview opt-in.
- NOT ported: workflow files (this build's are newer; the crawler-bridge workflow still expects Opsis.UI 0.1.0.0), the one-line Rainmeter test fixture.
- DECISION NEEDED (C13): wallpaper info box text colour. 2.8.0.2511 follows the "text colour follows the wallpaper" switch; the 3.0 RC forces white text and ignores the switch.

## C. Not started (OPEN)
- Wave 1 remainder: #20 shared shell profile editor (import/export, snapshots), #16 per-monitor appearance, #12 Tray Icon Studio consent rules.
- Wave 2: #1/#2 native Start/Explorer per build, #9 Explorer command bar, #4–#7 replacement taskbar (4 edges, floating, segmented, dynamic), #11 thumbnails, #17 gestures, S3 Signature Glass / Opsis.UI 6 merge (PR #3/#12 drafts), S4 DeskFlip real Alt+Tab, S5 Rainmeter importer, P1 plugin interoperability.
- Wave 3: #13 Notification Center, #14 Quick Settings, #15 Volume flyout, W1/W2 weather photo masks and HDR/GPU, W3 verify the 110 originals, W4 remaining capitals + 9 weather cities, Q2 resource audit.
- SDK 2.0: SDK1 API negotiation, SDK2 permissions/isolation, SDK3 dependency DAG/update/rollback, SDK4 developer kit.
- Proposals: X1 screen saver, X2 marketplace, X3 snap layouts + spaces, X4 animated taskbar, X5 icon packs 2.0, X6 lock screen, X7 automation 2.0, X8 command palette 2.0, X9 widget data sources, X10 performance auto-tuner, X11 context menu studio, X12 Opsis.UI 6 settings, X13 recovery/migration hardening, X14 weather previews/privacy, X15 100-improvement reconciliation, X16 diagnostics centre.
- Opsis.UI: 1 px AdvancedSection border overlap; Ctrl+F missing in the shortcuts list (14-language text).

## D. Blocked / needs a decision
- C1 interactive install, upgrade 2.6 → 2.8 → 3.0, uninstall, mixed DPI / multi-monitor on a clean Windows 11 VM.
- C2 code signing of installers (all installers are unsigned).
- Europe R26 host: cherry-pick or take R28 branch as the source of truth (R28 already merged here).
- Q3 final 3.0 release: all gates PASS on the exact SHA, 205 baseline settings preserved, user approval before `main` / release. `Version.props` stays 2.8.0.2510; the 3.0 build uses `-p:OpsisAppVersion=3.0.0.3200`.
- Release gates still `pending` in `release/3.0.0.3200-readiness.json`: integrated plugins E2E, Windows 11 visual / DPI / performance, installer matrix, security review.
