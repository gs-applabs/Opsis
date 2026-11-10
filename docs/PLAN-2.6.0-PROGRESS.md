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

## New core (option A, approved 2026-11-10) - phases
| Phase | Status |
|---|---|
| 1. Opsis.Glass: layered host + shared capture + GPU glass | PRODUCT LIBRARY WRITTEN (`glass/Opsis.Glass`, 15 tests, demo measured, design doc 8). Not yet integrated into DeskStrip/widgets. Earlier: PROOF DONE (`spikes/GlassGpu`, `docs/spike2/gpu/`, design doc section 6). The product library `src/Opsis.Glass` (device-lost recovery, caches, tiers, multi-monitor) is NOT written yet |
| Glossy intensity + presets | DONE in Opsis.UI 0.4.5 (Theme page) |
| Requests table (old containers -> new core) | DONE, design doc section 5 (older chat transcripts are unreadable, so more items may exist) |
| 4 data model + read-only importer + pipe/CLI | DONE (`core/Opsis.Containers.Core`, 15 tests, design doc section 9) |
| 6 windows and behaviour | PART A DONE: the behaviour rules as pure tested functions (`ContainerBehavior`: snap to edges / other containers, resize limits, roll-up with the container's OWN title-bar height, keep reachable after a monitor change, lock, hit test; 8 tests). PART B NOT done: the real window host (non-activating layered window + glass pane), z-order next to the desktop icon layer and the wallpaper window, Win+D / Explorer restart behaviour - these need real-desktop tests |
| 2 rendering, 3 shell, 5/9 rules+integration, 7/8 a11y+DPI, switch off the fork | NOT started |

## Asked from the user (manual tests, steps in design doc 6.6)
Two monitors with different DPI; DPI 150/200 %; real wallpaper behind the icons; AMD/NVIDIA or Remote Desktop; lock/unlock and UAC while glass is on. Also: run `tools/resource-logger.ps1` on 2.4.6.1 for the baseline that fixes the RAM/CPU budgets.

## Integration of Opsis.Glass into DeskStrip (done, behind a setting)
`UseGpuGlass` (Settings > Appearance > Advanced, default OFF, restart asked): the DeskStrip becomes a layered per-pixel-alpha window (icons only, `LayeredSurface`) with an Opsis.Glass pane directly below it (`GlassService`, conditions from the performance tier, live wallpaper, lock / remote / fullscreen / battery saver, safe mode); if the GPU part cannot start the old DWM blur look stays. Verified by `Opsis.exe --test-deskstrip-glass <folder>` (isolated data, synthetic moving background in my own window, nothing from the desktop of the user is captured): pane created, 51 captures / 51 renders in 4 s, pane removed on Disable; screenshot `docs/spike2/deskstrip/deskstrip-gpu-glass.png`. NOT done: the widgets (still DWM blur; they are WebView/WinForms windows with their own drawing, next), hover/popup windows of the strip, per-monitor DPI of the pane, drag and drop onto the layered strip, long-run behaviour. Why default off: real-use risk (layered window input, z-order with other topmost windows) needs the user's test first.

## Covered-desktop pause: what was tested
- 8 unit tests of the decision (`DesktopCoverageDecisionTests`): maximized window covers its monitor only; minimized / cloaked / own-process / tool / layered / click-through / shell windows do not; tiled windows never; two monitors.
- LIVE on this machine (`Opsis.exe --test-covered`, read-only, `docs/spike2/covered-desktop-live-check.txt`): the user's maximized window was detected as covering DISPLAY1 and `AppActivity.IsPausedFor(primary)` was True after the two-sample hysteresis with no fullscreen / lock flag, i.e. the pause is active through coverage alone.
- NOT tested automatically: the RESUME when the desktop is shown again (needs an uncovered desktop; the logic resumes on the first sample without cover). Manual steps for the user: with the wallpaper running, maximize a window on it and watch GPU use of the Opsis / msedgewebview2 processes drop to ~0 within 2-3 seconds; minimize it (or Win+D) and check the wallpaper moves again within about 1 second; repeat with two monitors (cover one only).

## Integration of Opsis.Glass into the widgets (done, behind the same setting)
`GlassPaneBinder` keeps a glass pane directly below a widget window (add / move / resize / show-hide / remove follow the form; the pane is re-seated every 500 ms because a widget changes its z-order without events). It is active only when `UseGpuGlass` is on AND the widgets are in "Transparent" mode WITHOUT a tint (the colour key makes the widget form see-through, which already worked). Checked with `Opsis.exe --test-widget-glass <folder>` (Clock widget over a synthetic moving background): pane created, 51 captures / 51 renders in 4 s, removed on close; screenshot `docs/spike2/widget/widget-gpu-glass.png` (glossy refracting glass, text readable). The first run showed NO glass because the pane ended below my stage window after the widget changed its TopMost state: that is what the periodic re-seating fixes. NOT done: widgets in the tinted or normal (blur) modes (they are opaque windows with DWM blur; the GPU glass needs the see-through form), WebView2 widgets were not tried separately (same form, same key), per-monitor DPI, a "glass" choice in the widget menu.

## Wallpaper cost (user request: less resources, same quality and stability)
Done: pause when the desktop is fully covered (`DesktopCoverage`, setting on by default) and a cheaper drawing of the Ribbons wallpaper (GPU -35-40 %, same look). Details and what was not done: design doc section 7. NOT verified live: the covered-desktop pause inside a running Opsis (only the geometry is unit-tested); the new Ribbons drawing on the real wallpaper window at other resolutions / tiers.

## Where I stopped
Phase 6 part A (behaviour rules) done. Next: phase 6 part B (the container window host on Opsis.Glass, including the z-order question: containers must sit above the desktop icons and the wallpaper but below normal windows, and survive Win+D and an Explorer restart - the first real-desktop test is a manual one, steps will be listed here), then phase 2 (own rendering of icons / labels / scroll), 3 shell, 5/9, 7/8.
