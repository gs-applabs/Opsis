# Opsis 3.0.0.3200 — remaining tasks (analytic list)

Source: `PENDING.csv` / `CLAUDE-TASKS-EL.md` of the RC patch. Status as of 2026-11-24, branch `patch/3.0.0.3200-appearance`.
Effort: **S** = short (code only, ≤ 1 h), **M** = medium (code + tests), **L** = long (needs real Windows 11 / VM / external assets), **X** = blocked by something outside the code.

## Done
| ID | Item | Evidence |
|---|---|---|
| S1 | Start Buttons overlay (logo, 24/32/48/64 art) | applied, build 0 errors, ExperienceSmoke 165 PASS |
| S2 | Opsis 3 logo/ICO, Signature R14, unified Gallery | applied, RcUiSmoke PASS |
| — | Audit hang (RefreshFromWindows re-posting itself) | fixed 5fc1e20 |

## Open gate before anything else
| Item | State |
|---|---|
| Windows audit "Windows did not apply Settings.TransparencyEffects" | only remaining audit problem; diagnostic pushed (df9a3a1), run pending |

## Wave 1 — short and simple (code only, testable without a VM)
| # | ID | Task | Effort |
|---|---|---|---|
| 1 | 19 | `WindowsUpdateGuard.ApproveCurrentBuild` must not clear `_runtimeHold`; process-lifetime latch until restart + tests | S |
| 2 | 3 | Native Taskbar: backup-only auto-hide restore independent of grouping backup; check the shell exists before read/write; build gating | S |
| 3 | 10 | Explorer capture: bounded STA worker + timeout (no UI hang), 64 safe local folders, no arbitrary executable paths | M |
| 4 | W5 | 200 static WebP: validate manifest schema, exactly 200 distinct, decode/hash/limits, IO off the UI thread | M |
| 5 | 18 | Windhawk CLI: bounded output, timeout, cancellation, reject reparse points | M |
| 6 | 20 | Shared shell profile editor: import/export, first-change snapshot, no-op writes, old-format compatibility | M |
| 7 | 8 | Custom Start power menu: keyboard, explicit confirmation, no `/f` | S |
| 8 | 16 | Per-monitor appearance: stable monitor IDs, disconnected displays | M |
| 9 | 12 | Tray Icon Studio: never edit other owners' icons without consent | S |
| 10 | Q1 | Fix the historical-archive workflow (no common ancestor case) | S — done on `archive/3.0.0.3200-rc-7a055f1` |

## Wave 2 — medium (code + tests, some manual UI checks)
| ID | Task |
|---|---|
| 1, 2 | Native Start / Explorer: registry values per supported Win11 build, rollback |
| 9 | Explorer Command Bar: supported commands only, no blind input |
| 4, 5, 6, 7 | Replacement taskbar: 4 edges, floating, segmented, dynamic sizing (DPI, maximised windows, work area) |
| 11, 17 | Taskbar thumbnails and gestures (opt-in) |
| S4 | DeskFlip: 3 styles, Alt+Tab opt-in, bounded async PrintWindow |
| S5 | Rainmeter importer: declarative meters, scripts/plugins denied |
| P1 | Plugin interoperability: Crawler + Chronikologio 1.0.0.0, no duplicated features |
| S3 | Signature Glass / Opsis.UI 6 integration (review PR #12 diff, opt-in build) |

## Wave 3 — long / blocked (need real Windows 11, GPU, external assets)
| ID | Task | Why |
|---|---|---|
| 13, 14, 15 | Notification Center, Quick Settings, Volume Flyout | need per-build native behaviour; optional Windhawk only |
| W1, W2 | Weather photo-first masks, HDR/GPU pipeline | actual visual QA on real GPUs |
| W3 | Verify 110 original wallpapers and hashes; add-on installer E2E | needs admin install test |
| W4 | 42 remaining capitals + 9 weather cities | needs rights-verified photographs |
| Q1, Q2 | VM matrix, upgrade 2.8→RC→patch, resource audit | real Win11 VMs, long runs |
| Q3 | Final release | needs all gates PASS + user approval |

## Verification rules for every item
Build 0 errors → `--run-selftest` → `node --test` → ExperienceSmoke/RcUiSmoke → CI audit on the exact SHA. Anything not run is reported as NOT VERIFIED.

## Progress log (2026-11-25)
| ID | Result |
|---|---|
| 19 | DONE — latch kept until restart, `RestartRequired`, smoke tests |
| 3 | DONE — shell check, backup hygiene (auto-hide) |
| 10 | DONE — Explorer capture on bounded STA worker (5 s), tests |
| W5 | DONE — real 200 WebP pack imports (200 distinct, static, decoded, 1.3 s) in smoke test; import runs off the UI thread |
| 18 | VERIFIED existing — Windhawk is inventoried from registry/INI only (no CLI is spawned), bounded reads, reparse points rejected |
| 8 | VERIFIED existing — power menu is configurable, confirms, never uses `/f` |
| 20, 16, 12 | OPEN |

## Supplement: Plugin SDK 2.0 and P3–P5 proposals (OPSIS-Plugin-SDK-2.0-and-Remaining-Proposals-EL.md)
Rules kept: assembly ABI 1.0.0.0 unchanged, 205 baseline settings, one PluginHost; no in-process "sandbox" claims — untrusted executables only in a restricted separate process with authenticated bounded IPC, otherwise the feature is disabled. Status of every item below: **OPEN** (nothing implemented yet).

| ID | Pri | Task | Effort |
|---|---|---|---|
| X15 | P0 | Reconcile the original 100-improvement backlog and the 30-item P3–P5 roadmap against source/tests (#12, #17, #20, #24, #32, #40, #97); unrecoverable records are marked unverified | M |
| X13 | P0 | Recovery and migration hardening: atomic profile migration, interrupted update / power-loss simulation, restore of the previous working config | M |
| SDK1 | P0 | SDK/API version negotiation separate from ABI 1.0.0.0, legacy adapter, lifecycle, cancellation, duplicate IDs | M |
| SDK2 | P0 | Least-privilege manifest, per-capability consent, SHA-bound trust and re-approval; out-of-process isolation for untrusted executables | L |
| SDK3 | P1 | Dependency DAG and version ranges, hash/signature check before staging, atomic update and rollback | M |
| SDK4 | P1 | Developer kit: docs, sample plugin, contract tests, package validator, shared contributions | M |
| X3 | P1 | Advanced snap layouts + desktop spaces, one positioning owner (AutoTile vs Window Rules) | M |
| X7 | P1 | Automation Studio 2.0 + profile scheduler (dry run, undo, no scripts) | M |
| X8 | P1 | Command Palette 2.0 over a single command registry | M |
| X10 | P1 | Performance auto-tuner / GPU effects budgets | M |
| X11 | P1 | Context Menu Studio + per-app window styles | M |
| X12 | P1 | Opsis.UI 6: SettingsEditSession, EffectsPolicy, shared tokens | L |
| X1 | P1 | Screen Saver Creator: real .scr (/s /c /p) | L |
| X2, X4, X5, X6, X9, X14, X16 | P2 | Marketplace, animated taskbar effects, icon packs 2.0, lock screen, widget data sources, weather previews/privacy, diagnostics centre | L |

Release gates for the SDK: tests for legacy plugin, new API, incompatible API, duplicate ID, dependency cycle, denied permission, changed hash, interrupted update, crashed/slow/disabled plugin, uninstall/rollback; real Crawler, Chronikologio and Media Suite binaries.

## Carried over from the 2.6 / 2.8 work (merged into the 3.0 list on request)
All of these were open or unverified when 2.8.0.2510 was released. They are now part of the 3.0.0.3200 scope; IDs `C1…` are new, nothing above was renamed.

| ID | Pri | Item | Origin | Effort |
|---|---|---|---|---|
| C1 | P0 | Interactive install on a clean Windows 11 VM, upgrade 2.6.0 → 2.8.0.2510 → 3.0 and uninstall; mixed DPI / multi-monitor (NOT VERIFIED for 2.8.0.2510) | 2.8 release | L |
| C2 | P0 | Installer is unsigned: code signing (or a documented SmartScreen notice) for the final installer | 2.8 release | X |
| C3 | P1 | Secondary windows: automated GUI/function audit (Profile mapping, Weather triptych, Zone editor, Skyline mask editor, Help, dialogs) with screenshots and an "unstyled control / low contrast" check (`OPSIS_AUDIT_SECONDARY`) | 2.8 request | M |
| C4 | P1 | Containers context menus and native `MessageBox` dialogs still use system colours: Opsis-styled message box and menu renderer | 2.8 request | M |
| C5 | P1 | Visual verification of the Wallpaper Gallery with the new entries (Signature R14, 109 add-on, 200 static) | 3.0 patch | M |
| C6 | P1 | 110-wallpaper add-on installer: real install / uninstall / rollback on Windows (built and unified, not run) | add-on | M |
| C7 | P1 | Opsis.UI: 1 px overlap of the AdvancedSection border; Ctrl+F missing from the shortcuts list (needs 14-language text) | Opsis.UI | S |
| C8 | P1 | Opsis.UI release hygiene: tag v0.6.1 pushed; PR #3 (Signature Glass) and PR #1 (CI matrix) still draft | Opsis.UI | M |
| C9 | P1 | Home/start screen, About notes, README and locales refreshed (14 languages): build, test, commit, decide the version of the re-release | this session | S |
| C10 | P2 | Other apps: News Crawler, Appearance, Chronikologio (libs commits local, not pushed; Appearance hygiene test `TrackedSourcesHaveNoPersonalPathsOrEmail` fails on `docs/PLAN-INTAKE.md`), GearWin / Kombos (local branches `opsis-detect-exe-only`), EnfiaPro (net11, WPF styles), Media Suite (contract 1.0.0.0): push, release and verify the Program Files install | siblings | M |
| C11 | P2 | Older copies of Opsis.UI.dll in the `_crawler28` and `p3-src` working folders | audit | S |
| C12 | P2 | Keep `OPSIS_TRACE` diagnostics (HangTrace) out of the user path and document them | audit | S |

Done and verified since: audit hang fixed and Windows audit PASS on the release and patch branches; v2.8.0.2510 released; `main` now contains the 2.8.0.2510 tree (history merged); final Opsis.UI 0.6.0 DLLs in the plugin repos and in 2.6.0.
