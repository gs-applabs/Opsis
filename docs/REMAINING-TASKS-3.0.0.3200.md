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
