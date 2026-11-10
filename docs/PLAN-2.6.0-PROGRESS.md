# PLAN 2.6.0 - progress (branch `2.6.0-beta`, local only, never pushed)

Updated 2026-11-10. Rule of this file: if a session is cut off, the next one starts from "Where I stopped".

## Packages in the approved order (intake section 5)
| Package | Status | Commit |
|---|---|---|
| Phase 0A reuse audit | DONE (first pass) | `docs/REUSE_AUDIT.md` |
| A - adopt Opsis.UI 0.4.1 | PARTIAL | shared status bar, restart popup (plugins toggle), SettingsStore + schemaVersion, WindowMemory per screen. NOT done: shared SettingsPage/Card/Row layout on the Settings page, UiScale (the Opsis fonts are static in `UiTheme`, pages are built later: needs a font-scale in `UiTheme` first), Glass dropdown + colours page (needs a bridge from `UiTheme` to `OpsisTheme`/`GlassTheme`) |
| B - quick wins | PARTIAL | done: start-with-Windows description (it already starts hidden in the tray through `--background`), DeskContainers page, DeskStrip page, Profiles merged into Automation (rules shown beside profiles), video library Previous/Pause/Next, narrower library buttons on one row. NOT done: cards/sub-tabs/"Advanced"/section Reset on the dense pages (needs the user's screenshots), Glass dropdown, spectrum removal (waits for the music overlay) |
| C - WPF glass spike | see `docs/CONTAINERS_NATIVE_DESIGN.md` | `spikes/GlassSpike` |

## Verified
- Build 0 errors, 164 unit tests pass.
- `Opsis.exe --ui-audit` (isolated data folder, every page, every switch) finished with exit 0 including the new pages. The audit found my own restart popup blocking on the plugins switch: it is now skipped when `OPSIS_E2E=1`.

## NOT verified in real use (needs the user, Windows 11)
- Restart now / relaunch really restarts Opsis; status bar look in dark/light; video Previous/Next/Pause on a real playlist (including with two monitors); the new pages in the sidebar at 100-200% DPI; WindowMemory with a removed monitor.

## Where I stopped
After the spike measurements and the cost table (see CONTAINERS_NATIVE_DESIGN.md). The new native core has NOT been started: the user must approve the table first.
