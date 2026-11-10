# PLAN 2.6.0 - progress (branch `2.6.0-beta`, local only, never pushed)

Updated 2026-11-10. Rule of this file: if a session is cut off, the next one starts from "Where I stopped".

## Packages in the approved order (intake section 5)
| Package | Status | Notes |
|---|---|---|
| Phase 0A reuse audit | DONE (first pass) | `docs/REUSE_AUDIT.md` |
| A - adopt Opsis.UI | DONE except the points below | Opsis.UI 0.4.3 in `libs/`: shared status bar, restart popup (plugins switch), `SettingsStore` + schemaVersion, `WindowMemory` per screen, UI scale (shared `UiTheme` fonts, 100/125/150 %), Settings page built from `SettingsPage/SettingsCard/AdvancedSection` (cards, Reset per card, collapsed Advanced groups, search) |
| B - quick wins | PARTIAL | done: start-with-Windows description (starts hidden in the tray through `--background`), DeskContainers page, DeskStrip page, Profiles merged into Automation, video library Previous/Pause/Next + narrower buttons on one row, Theme page (Solid/Glass dropdown, SEPARATE colours card with its own Reset, collapsed Advanced effects; live; saved), Glass tint reaches every Opsis colour through `GlassHost` -> `ThemeManager.Current` |
| C - WPF glass spike | DONE | `docs/CONTAINERS_NATIVE_DESIGN.md`, `docs/GLASS_SPIKE_RESULTS.txt`, `spikes/GlassSpike`. Waiting for the user's decision on the table |

## Still open in A/B (and why)
- Dense pages other than Settings (Wallpaper ~730 lines of UI code, Personalization, Audio Enhancement, DeskZones, Widgets) are not yet cards/sub-tabs with "Advanced": each needs its own rows moved by hand and checked with screenshots (the UI audit writes `page-*.png`, so this can be done page by page).
- Windows 11 system backdrop (Mica/Acrylic) on the main window: `GlassManager.Attach` is not used, because every Opsis panel is opaque and the black-client-area trick would show black gaps. Needs a pass over the panels first.
- UI scale only scales the shared fonts (headings, body, icons); text with its own fixed size inside a control does not scale.
- Spectrum removal waits for the music overlay (plan 2.1.6).
- Not changed: the Wallpaper/Containers pages still use the old section style.

## Verified
- Build 0 errors, 167 Opsis tests + 157 Opsis.UI tests pass.
- `Opsis.exe --ui-audit` (isolated data folder, every page, every switch) runs through all pages, screenshots in its output folder. The audit found two of my own problems (a restart popup blocking, the Theme page's scrollbar/button text) which are fixed.

## NOT verified in real use (needs the user, Windows 11)
- Restart now / relaunch really restarts Opsis; video Previous/Next/Pause on a real playlist (and with two monitors); the new sidebar pages at 100-200 % DPI; WindowMemory with a removed monitor; Glass look in light theme and with Windows "Transparency effects" off; UI scale 125/150 % on every page.

## Where I stopped
Settings page converted and audit-checked (see the last commit). Next: the dense pages one by one (start with Wallpaper), then the Mica/Acrylic backdrop pass. The new native core has NOT been started: the user must approve the cost table first.
