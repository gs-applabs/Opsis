# DeskContainers - native core: spike result and cost table (for the user's approval)

Status 2026-11-10. **Nothing of the new core has been built.** This document is the Phase 0A spike result and the cost comparison the plan asks for; building starts only after the user approves the table (decision at the end).

## 0. DECISION OF THE USER (2026-11-10): option A, full rewrite with our own rendering
Requirement: the result, over a static OR an animated wallpaper, must look **fully transparent** (like PecoFence) and must show the **live** background behind containers, widgets and DeskStrip. A "frosted snapshot" is not accepted until the options are exhausted. The three open questions were not answered by the user, so my proposals stand **as ASSUMPTIONS to confirm**: minimum Windows 11 22H2 (build 22621+), the engine first as a **separate host process**, one icon-hiding mechanism = move the `SysListView32` items off screen with a mandatory restore on every exit path.

### Spike v2 - "real transparency + live background" (what was built and measured)
Tool: `spikes/GlassSpike2` (throw-away; Win32 layered windows through `UpdateLayeredWindow`, no WPF). Evidence: `docs/spike2/` (results, per-second CPU/GPU samples, 4 screenshots). Machine: 1 screen 2560x1440, Intel UHD 630, 6 cores, Windows 11 build 26300, DPI 100 %.
The stand-in for the wallpaper window is a topmost window playing Opsis' **own wallpaper page** (`src/Widgets/wallpaper/index.html`, Waves mode) in WebView2. It is not the real wallpaper window behind the icons (see the "not proven" list); it also kept the screenshots free of whatever is open on the user's desktop (my first attempt over the real desktop showed private business windows; those images were deleted at once).

| Question | Result |
|---|---|
| Are real per-pixel-alpha windows possible and live? | **Yes.** `UpdateLayeredWindow` panes (14 % white + rim, transparent rounded corners) show the animated wallpaper through them (screenshot `v1-transparent.png`). DWM composes it: no snapshot, no frame feed needed for plain transparency. 6 such windows idle: 0.0-0.1 % CPU; dragging one: 0.35-3 % CPU (60 moves/s). |
| Can the glass (refraction) see the live background without a frame API? | **Yes, with a capture of the composed desktop**, if our glass windows are excluded from it. `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` works for a GDI `BitBlt` capture: with the flag the capture saw the wallpaper (red channel 5), without it the window (255). So the glass never captures itself, and it captures everything behind (wallpaper, icons, other windows). Screenshots `v3-refraction.png`, `v3-refraction-4.png`: the live wave line bends at the glass bezel. |
| Cost of that CPU path | per window per frame: capture 14.3-15.6 ms (one display frame: the capture waits for the compositor, it is **not** pixel-bound) + glass maths 2.0 / 2.0 / 3.9 ms (420x300 / 630x450 / 840x600, i.e. 100 / 150 / 200 % DPI) + present 0.3-0.7 ms. Flat out: 49-60 fps; our process CPU 10.6 / 10.7 / 16.2 %. Four windows with ONE union capture per frame: 21.6 fps at a 30 fps budget, 5 % CPU (per-window capture was 15 fps: share the capture). |
| Cost of the live wallpaper itself | the Waves page in WebView2 (1700x900) held **~55-58 % GPU engine utilisation** (sum of engines, Intel UHD 630) and 3-6 % CPU of the whole process tree **before** any glass. The wallpaper, not the glass, is the dominant GPU cost on this machine. |
| DWM system backdrop (Acrylic) as the live blur | **Not working in my test**: the window rendered a flat grey (the solid fallback), most likely because Acrylic falls back when the window is not active and our panes are `WS_EX_NOACTIVATE`. Not resolved. Mica/Acrylic on the main window is a separate matter from desktop glass. |
| DPI 150 / 200 %, two monitors | **Not run on real DPI/monitors** (one screen at 100 % here). Only the bitmap sizes of 150 / 200 % were timed. |
| Direct2D / GPU glass | **Not built.** Glass maths here are CPU. |
| Side by side with PecoFence | **Not possible**: no Rust toolchain on this machine, and I did not run a downloaded executable. What the source shows (`crates/render/src/backdrop.rs`): PecoFence's backdrop is the monitor's **wallpaper image file**, mapped like Explorer and downscaled ("Mica-like"). It is a static image, so PecoFence itself does **not** follow an animated wallpaper; the live behaviour asked for here goes beyond it. |

### What this means for the rewrite
1. Plain transparency over any wallpaper (static or live) is solved by layered windows, at essentially zero cost.
2. Live refraction/blur is feasible with a capture that excludes our own windows. The capture must be **one per frame for the union of all glass windows** and should run only while something behind changes (live wallpaper on, or a window moves) - never at idle.
3. The CPU maths must move to the GPU for production (Direct2D displacement map; capture through DXGI Desktop Duplication or Windows.Graphics.Capture so the pixels stay on the GPU). That is **not proven** here.
4. Frame feed from the Opsis wallpaper engine (the user's idea): the wallpaper is Chromium, so there is no texture to share. Options, none proven: (a) capture the composed desktop as above (works today, includes everything behind); (b) host the wallpaper WebView2 in composition mode and take its DirectComposition surface; (c) Windows.Graphics.Capture of the wallpaper window (needs a top-level window; the wallpaper is a child of the desktop host window). (a) is the baseline; (b) and (c) are research items for the glass phase.

### Revised cost and phases of the rewrite (option A) - estimates, not measurements
| Phase / item | Size | Risk |
|---|---|---|
| 1. `Opsis.Glass`: layered-window host + shared capture (union rect, exclude-from-capture, change-driven), then the Direct2D/GPU glass (displacement, blur, rim) | Large | GPU/driver differences, device reset, AMD rim bug known from PecoFence, VRAM growth |
| 2. Own rendering of container content: icons, labels (DPI, ClearType on alpha, RTL), selection, scroll and virtualization | **Very large** (the plan's biggest item) | accuracy, DPI 100-200 %, performance of our own scrolling |
| 3. Shell integration: icon extraction/thumbnails (`IShellItemImageFactory`), context menu (`IContextMenu`, crash isolation for third-party extensions), drag and drop in and out (`IDropSource`/`IDropTarget`, elevated Opsis) | Large | extension crashes, UIPI |
| 4. Data model with `schemaVersion`, importer from the 2.4.6 data (never deletes), snapshots, backups | Medium | data loss (mitigated: read-only import) |
| 5. Rules engine, profiles, hotkeys (with the reserved-key check from Opsis.UI), automation merge | Medium | |
| 6. Window behaviour: non-activating windows, z-order next to the desktop icon layer and the wallpaper window, snap/roll-up/tabs/portals/notes | Large | Win+D, Explorer restart, 24H2/25H2 wallpaper z-order |
| 7. Accessibility (UI Automation for self-drawn items, keyboard, High Contrast, Narrator) | Medium-large | self-drawn UI has none by default |
| 8. DPI/monitors: PerMonitorV2, `WM_DPICHANGED`, monitor hot-plug, normalized geometry | Medium | needs real multi-monitor tests |
| 9. Integration: named pipe + CLI instead of the file bridge, one tray, perf governor, safe mode, `ProcessFreezer` | Medium | |
| 10. Tests and manual Windows 11 table | continuous | |
Order: 1 (prove the GPU/live path first) -> 4 -> 6 -> 2 -> 3 -> 5/9 -> 7/8 -> switch off the fork.

### Not proven (honest list)
GPU/Direct2D glass; DXGI duplication / Windows.Graphics.Capture cost; the real wallpaper window behind the icons (z-order, WorkerW) with glass above it; two monitors with different DPI and real DPI 150 / 200 %; Acrylic backdrop on a non-activated window; other GPUs (AMD / NVIDIA / WARP); long-run VRAM; icons and other windows behind the glass refracting correctly; PecoFence visual comparison.

**Next step after this spike: the user looks at `docs/spike2/*.png`. The full build does not start before that.**

---

## Spike v1 (earlier, kept for the record): WPF windows with a pre-rendered bitmap
### 1. The spike (what was measured)
Tool: `spikes/GlassSpike` (throw-away, not part of Opsis.exe). It opens 8 top-level WPF windows (`AllowsTransparency`, no taskbar button) 420x300 DIPs, each showing a pre-rendered "glass" bitmap (blurred copy of the real desktop behind it + refraction at the rounded bezel + tint + rim light, made on the CPU), then drags one window, resizes one window and refreshes all of them.

Machine: **1 screen** 2560x1440, **Intel UHD 630**, 6 logical cores, Windows 11, DPI scale 100 %.

| Measurement | Result |
|---|---|
| Making one glass bitmap, 420x300 (100 %) | 5.7 ms |
| ... 630x450 (150 %) / 840x600 (200 %) / 1260x900 (300 %) | 7.9 / 14.1 / 16.4 ms |
| Full-screen snapshot (CopyFromScreen) of the backdrop | 56 ms (so a live backdrop is a periodic snapshot, not per frame) |
| 8 glass windows, idle, 5 s | **0.05 %** CPU of the machine |
| Dragging one of the 8 | ~10 % CPU while moving (the test redrew on every composition frame, ~270 fps, much more than a real mouse drag); frame gap median 2.2 ms, p95 16 ms, max 46 ms |
| Resizing one window, same bitmap stretched | 5.5 % CPU, 29 steps/s (the test itself is the limit) |
| Resizing one window, glass REGENERATED every step | 8.4 % CPU, 21 steps/s -> **do not regenerate while resizing**: stretch, then render once when the resize stops (the same debounce rule as `GlassPanel` in Opsis.UI) |
| Live backdrop: all 8 windows re-glassed at 2 Hz, 6 s | **1.5 %** CPU |
| GPU engine utilisation of the process (an earlier run of the same test, sum of engines) | average ~5 %, max ~13 % |

**Reading:** top-level transparent WPF windows with a pre-rendered bitmap are cheap when still (0.05 % idle) and acceptable when refreshed a few times per second; the cost that matters is the bitmap generation, which grows with pixel count (6 -> 16 ms from 100 % to 300 %), so it must be cached per size/scale/tint and rebuilt only on change (plan 0.4d).

**Not measured (honest list):**
- **Direct2D / Vortice**: the glass here is CPU code. The real `Opsis.Glass` (displacement map on the GPU) was not built or timed. Expect it to be cheaper per bitmap, but this is not shown.
- **Two monitors with different DPI** and **DPI 150 / 200 %**: this machine has one screen at 100 %; only the bitmap sizes of those scales were timed, not real mixed-DPI behaviour (window moves between screens, `WM_DPICHANGED`).
- **A live wallpaper behind the windows** (Opsis' own wallpaper window): not run together; the backdrop was the normal desktop. Plan note stays: with a live wallpaper the result is frosted (periodic snapshot), not true refraction.
- Other GPUs (AMD / NVIDIA), WARP, device reset, long-run VRAM.
- Z-order with the desktop icons layer, Win+D, Explorer restart.

## 2. The options
The plan already decided the **window technology**: containers stay WPF, host/widgets/settings stay WinForms (section 0.4c, "subject to the spike"). The spike supports that decision: nothing in it rules WPF top-level windows out. So the real choice is how much of the fork to keep.

| | **A. Full rewrite**, own rendering (WinForms layered windows) | **B. New core on WPF, reusing the fork's UI layer** (plan 2.4.C as written) | **C. Evolve the fork in place** + shared glass library |
|---|---|---|---|
| What it is | everything redrawn by us (icons, labels, scroll, drag) | new Opsis-owned core; keep the working WPF container UI, replace the broken parts (hide-icons, hooks, hotkeys, drag and drop, file bridge -> pipe, settings, data model with `schemaVersion`) | keep `containers/` as it is, patch the known faults, add glass |
| Glass on containers, widgets and DeskStrip | yes | yes (bitmap from `Opsis.Glass`) | containers: yes (bitmap); fork internals stay as they are |
| Windows 10 | out of scope | out of scope | out of scope |
| Own code to write (order of magnitude, **my estimate, not measured**) | very large: the fork is ~36k lines of WPF and ~15k lines of those are UI behaviour we would redraw | large: ~10-15k new lines (core, data, hooks, pipe, rules, hotkeys), UI mostly reused | medium: ~3-6k lines of fixes |
| Time (relative, **estimate**) | 6-10x of C | 2-3x of C | 1x |
| Main risks | accuracy, DPI, accessibility, performance of our own scrolling/labels (plan table: "Large-Very large") | glass library (GPU/driver), WPF + WinForms in one process (separate top-level windows, no ElementHost), importer for old data | the fork keeps its known faults (file bridge, `Environment.Exit` paths, hook duplication, 16 options without UI, easter eggs) and its third-party code stays in the product |
| Fixes the known fork faults (plan R.2) | by design | by design (they are the replaced parts) | only the ones we patch |
| One tray / one control channel / one icon-hiding mechanism | yes | yes | no (file bridge stays) |
| Data migration risk | high | medium (importer reads the 2.4.6 data, never deletes it) | none |
| Licence | all ours | fork code kept under MIT (notice kept) | fork stays third-party MIT |
| Evidence from the spike | not needed | window technology works and is cheap; glass bitmap must be cached | same as B |

## 3. (Superseded by section 0) The recommendation I gave before the decision
**Option B**, built in this order (each step releasable on the 2.6.0 beta branch): (1) `Opsis.Glass` as a library, first on DeskStrip and the widgets, to test it; (2) data model + importer + pipe/CLI; (3) container windows on WPF with the fork's UI parts moved over; (4) rules, profiles, snapshots, hotkeys; (5) switch off the fork. Reasons: the spike removes the doubt about WPF top-level windows; a full rewrite (A) would redraw things that already work; evolving in place (C) leaves the faults the plan wants gone (one tray, one hiding mechanism, no file bridge).

Before step (1) is accepted as "done" the missing measurements above must be made on the real Direct2D version, with two monitors at different DPI, and with Opsis' live wallpaper running (manual table for the user).

## 4. Decisions (section 0 records the user's choice of A; the other three points are assumptions to confirm)
1. Approve **B** (or choose A or C).
2. Minimum Windows 11 build (proposal 22H2+; the system backdrop attribute used by Opsis.UI needs 22621).
3. Containers engine process: inside the Opsis process (cleaner) or a separate host first (solves drag and drop when Opsis runs elevated; the plan suggests starting separate).
4. One mechanism to hide desktop icons: move the `SysListView32` items off screen (what the fork does) with a mandatory restore on every exit path, or the hidden attribute.


---

## 5. Requested / existing -> implemented in the new core (no omissions)
Sources read: the fork's `SettingsManager.cs` (about 55 options, 16 with no UI), `containers/OPSIS_NOTES.md`, `CHANGELOG.md` 2.2.x-2.4.x and the older entries about containers, the master plan (sections 2.4, R.2, 1.1-1.4), and the user's new requests of 2026-11-10. (The older chat transcripts of the Opsis project are stored in a damaged text encoding; I could not read the user's wording from them, so items that exist only there may be missing: **please add them to this table when you remember one**.) Status column: NEW = planned for the new core, KEEP = existing behaviour carried over, FIX = existing but broken in the fork, DROP = intentionally not carried (with reason). Phase = the build order of section 4 (1 glass, 4 data, 6 windows, 2 rendering, 3 shell, 5/9 rules+integration, 7/8 a11y+DPI).

### 5.1 New requests (user, 2026-11-10)
| Request | Status | In the new core | Phase |
|---|---|---|---|
| Title bar may have **another colour than the whole frame**, adjustable **height** and **font size** | NEW (the fork had colour/opacity/height only) | per-container `TitleBar { Color, Opacity, Height, FontSize, Font }` + global default; frame and title are separate surfaces in the glass renderer | 2 |
| Fully transparent look, live background behind containers, widgets, DeskStrip (static or animated wallpaper) | NEW | layered windows + one shared GPU capture (spike v2) | 1 |
| "Glossy" intensity (specular highlight, rim light, bright edge, refraction) with presets Subtle / Glass / Very glassy | NEW | `GlassSettings.Glossy` (0-100) + 3 presets, shared with Opsis.UI | 1 |
| Low CPU/RAM, smooth with 8 GB RAM (Opsis + wallpaper + widgets + DeskStrip + containers) | NEW | rules and budgets in section 6 | all |
| Window technology = own rendering (option A) | decided | section 0 | 2 |

### 5.2 Options of the fork (`SettingsManager`) and how each is carried
| Fork option | Status | New core |
|---|---|---|
| EnableAutoBackup, LastAutoBackupDate (daily backup) | KEEP | backups daily + manual, restore with preview |
| ShowPortalExtensions, NoWildcardsOnPortalFilter, PortalBackgroundOpacity, ShowBackgroundImageOnPortalContainers | KEEP | portal (live folder) options |
| IsSnapEnabled, EnableDimensionSnap | KEEP | snap to edges / sizes (coordinated with `ZoneSnapEngine`) |
| UseRecycleBin | KEEP | delete to recycle bin |
| ShowInTray | DROP | one Opsis tray (plan 0.4) |
| EnableSounds | KEEP | via DeskSounds events |
| TintValue, MenuTintValue | KEEP + NEW | tint of frame / menu; plus title-bar colour (new) |
| MenuIcon | KEEP | menu glyph choice |
| AutoRollUpOnHover | KEEP | roll-up / hover expand |
| HideDesktopOriginalsOnDrop, DeleteOriginalShortcutsOnDrop, ExportShortcutsOnContainerDeletion | KEEP | one icon-hiding mechanism, mandatory restore on every exit path |
| TitleBarColor, TitleBarOpacity, TitleBarHeight | KEEP + extend | see 5.1 (adds font size, font family) |
| LockIcon | KEEP | lock glyph |
| SelectedColor | KEEP | selection colour (also follows theme) |
| IsLogEnabled, MinLogLevel, EnabledLogCategories, DeletePreviousLogOnStart, EnableBackgroundValidationLogging | KEEP (reduced) | one logger (`RotatingLogger`) with levels; no category zoo in the UI |
| MaxDisplayNameLength | KEEP | label length |
| EnableIconGlowEffect | KEEP | icon glow (off on Low tier) |
| DisableSingleInstance | DROP | the host owns the single instance |
| SuppressLaunchWarnings | KEEP | launch safety prompts (`SafeLaunch`) |
| DisableContainerScrollbars | KEEP | scrollbar visibility |
| DisableNoteAutoSave | KEEP | notes auto-save |
| EnableChameleonMode | KEEP | colour from the wallpaper (from the live backdrop capture: no extra cost) |
| EnableProfileAutomation | KEEP | merged with `AutomationService` (one system) |
| EnableAutoOrganize, EnableAutoOrganizeNotifications, AllowAutoReposition | KEEP | rules engine (type, extension, name, wildcard, shortcut target, time, size, dry-run) |
| EnableContextMenu | KEEP | Explorer `IContextMenu` in an isolated process (crash isolation) |
| AutoHideContainers, AutoHideTime, AutoResetHideTimer, HideFlashEffect | KEEP | auto-hide after idle |
| IconVisibilityEffect, LaunchEffect | KEEP | per-container launch effect and icon visibility effect (animations skipped on Low tier) |
| EnableSpotSearchHotkey, SpotSearchKey, SpotSearchModifier | KEEP + FIX | Spot Search; hotkeys through the shared registrar with the reserved-key check (default Ctrl+` kept; the hook is replaced by `RegisterHotKey`) |
| EnableProfileHotkeys, ProfileSwitch/Prev/Next keys+modifiers, EnableFocusContainerHotkey, FocusContainerKey/Modifier | KEEP + FIX | one hotkey system, conflicts reported to the user |
| SingleClickToLaunch | KEEP | single / double click launch |
| ~16 options with no UI (only in options.json) | FIX | each decided: kept with UI in an Advanced group, or dropped; list in `docs/` when phase 5 starts |

### 5.3 Behaviour and features (CHANGELOG 2.2.x-2.4.x, older entries, master plan 1.1-1.4)
| Requested / existed | Status | New core |
|---|---|---|
| Create / delete container (menu, draw mode, command); deleting restores the hidden icons | KEEP + FIX | restore rule in one place |
| Launch icons (single/double click), launch effect, per-container effect | KEEP | |
| Tabs: activate, rename, reorder, import, merge, detach (drag a title onto another) | KEEP + NEW (PecoFence idea) | |
| Portal containers (live folder, sub-folder navigation, filters) | KEEP | `FileSystemWatcher`, bounded cache |
| Note containers, auto-save | KEEP | |
| Roll-up (double click), hover expand, smooth animation, no flashing scrollbar | KEEP | animation respects perf tier |
| Move, resize by dragging the corner, lock, snap to edges and sizes | KEEP | |
| Icons the same size as desktop icons ("Desktop" size, default), icon size, labels, multi-select | KEEP | |
| Wheel and Left/Right arrow scroll, F2 rename, double click on title renames, centred title, item count in the title, "Search in container..." | KEEP | |
| Drag and drop **in** from Explorer/desktop (copy; the desktop icon is hidden, not deleted), also onto a container that already has icons | KEEP + FIX | `IDropTarget`, elevated Opsis (UIPI) handled by the separate host |
| Drag and drop **out** (Copy/Move/Link like Explorer) | FIX (missing in the fork) | `IDropSource` |
| Auto-organize rule "collect desktop files by type" | KEEP | rules engine |
| Automation "when an application starts, load a profile" | KEEP | merged with `AutomationService` |
| Profiles (switch, manager) and **"save current layout as a profile" really stores the layout** | KEEP + FIX | snapshots (up to 20, automatic before restore/import) |
| Backups: manual, daily, restore | KEEP | restore with preview |
| Spot Search, global hotkeys (profile, focus, next/prev) | KEEP + FIX | see 5.2 |
| Customize / EditShortcut / IconPicker (icon, font, size of a shortcut) | KEEP | own dialogs, same functions |
| Appearance: tint, title, menu icon, Chameleon | KEEP | |
| Quick-hide / quick-show of desktop icons by double click on the desktop, "Exclude from auto-hide" honoured, one double-click hook shared with Opsis | KEEP + FIX | one mechanism |
| Icons that stay visible (This PC, user folder, Control Panel, Recycle Bin) | KEEP | |
| Restore of desktop icons after crash / kill / upgrade / installer, no `Environment.Exit` bypass | FIX | watchdog + restore on every path |
| New container / focus container from the tray (command names matched) | FIX | one tray, one pipe |
| Settings in Opsis (cards), menu button opens a normal tabbed pop-up | KEEP | Opsis settings page + container pop-up |
| Greek + all 14 languages, "fence" wording gone (DeskContainer) | KEEP | key-equivalence script |
| Real native shadow of pop-ups, no white outline around containers/widgets | KEEP | layered window with our own shadow |
| Transparency applies to the background only (not text/icons) | KEEP | alpha only in the glass layer |
| Widgets with full transparency: text colour follows the wallpaper behind (light/dark, also when it moves) | KEEP | uses the shared live capture (`BackdropContrast` logic) |
| Installer stops the engine before replacing files and on uninstall | KEEP | |
| Perf tier: Low skips decorative start-up animations; pause on lock / RDP / battery saver / fullscreen; process freezer | KEEP + extend | section 6 |
| Safe mode (do not start with the engine) | KEEP | |
| Old data import (positions, items, tabs, portals, notes, spacers, icon size, title font and size) | KEEP | importer reads, never deletes |
| Rules engine conditions, dry-run, presets; snapshots; Peek (dim + search); dock on top / push-away; geometry per monitor; hide title on hover; named pipe + CLI (JSON); "plan restore" | NEW (PecoFence ideas, master plan 1.4) | phases 5/9 |
| Plugin API to reach the containers | NEW (optional, plan) | phase 9 |
| Dead code / author easter eggs / unused Options form | DROP | not carried |
| Update/news check, donation, About, "Start with Windows" as its own option, welcome note, engine tray icon | DROP (removed on purpose from upstream) | |
| Windows 10 support | DROP | Windows 11 22H2+ only |


---

## 6. Phase 1 proof: the GPU path (done 2026-11-10) and the resource rules

### 6.1 What was built and measured (`spikes/GlassGpu`, evidence in `docs/spike2/gpu/`)
Pipeline: **DXGI Desktop Duplication** (change-driven) -> ONE copy of the union rectangle of all glass windows into one persistent GPU texture -> **Direct2D effects on the GPU** (Gaussian blur + displacement/refraction map + tint + rim, all created once) -> **DirectComposition swap chains with premultiplied alpha** (windows with `WS_EX_NOREDIRECTIONBITMAP`, true per-pixel transparency), glass windows excluded from capture. Machine: Intel UHD 630, 2560x1440, DPI 100 %, the live background = Opsis' own wallpaper page (Waves) in a stand-in window.

| Check | Result |
|---|---|
| Glass windows are NOT in the duplicated frames (no feedback) | **Yes** (`gpu-captured-union.png` shows the background only; glass was on screen while it was captured) |
| Looks like glass over the live background | **Yes** (`gpu-glass-4.png`: blurred + tinted + refracted live wave, transparent rounded corners, rim) |
| 4 glass windows 320x240, live background, 60 fps | our process **1.5-2.7 % CPU**, **~9 % GPU** (Intel UHD 630, sampled), union texture **1.2 MB** VRAM once |
| Same with the earlier CPU path | 22 fps at 5 % CPU -> the GPU path is ~3x the frame rate at about half the CPU |
| Change-driven | `AcquireNextFrame` returns only when something behind changed; with a static background there is no capture and no render (idle cost 0) |
| Per-frame allocations in the render loop | none in our code (effects, brushes, geometry, layer, displacement map are created once; the swap-chain buffer wrapper per frame is the only COM wrapper and will be cached in the product) |
| "acquire 10 ms" | that is waiting for the next display frame, not work |

**Note on the live wallpaper:** the wallpaper page alone costs ~55-58 % GPU on this machine (sum of engines), several times more than all the glass. The wallpaper must obey the performance tier (frame cap on Low/Balanced) - this is where the 8 GB / low-end target is won or lost.

### 6.2 Still NOT proven (phase 1 is a proof of the path, not finished)
- The product library `Opsis.Glass` (this was a spike; the production code, device-lost recovery, cache and tiers are still to write).
- Real DPI 150 / 200 % and two monitors with different DPI (duplication is per output; one duplicator per monitor and per-monitor union rectangles are the design, not tested).
- Real wallpaper window behind the icons (the stand-in is a normal window; the capture is of the composed desktop, so it should behave the same, but it is not tested).
- AMD / NVIDIA GPUs, WARP/RDP (duplication is not available in some sessions: needs the fallback = layered window with a static frosted tint), device reset, VRAM over hours.
- DXGI duplication limits: one process may duplicate an output only a few times; protected content appears black; secure desktop interrupts it (handle `DXGI_ERROR_ACCESS_LOST`).
- Memory: RAM of the glass host was not measured yet.

### 6.3 Rules for the new core (resource rules, plan 0.4d + the user's 2026-11-10 requirements)
1. **Capture only when something changes** behind a glass window: live wallpaper active, or a window moves/resizes behind. No change = no capture, no render.
2. **ONE capture for the union of all glass windows** per monitor (one texture), cropped per window by the GPU.
3. **GPU only**: capture and effects stay in GPU memory (DXGI/D2D); no CPU read-back except for the contrast sampling of Chameleon/auto-contrast (a tiny downscaled copy, a few times per second).
4. **Throttle by performance tier**: High = up to 60 fps live refraction; Balanced = 30 fps, blur only; Low = static frosted tint (no capture), no animations, no refraction.
5. **Caches with a hard limit**: displacement maps and glass bitmaps per (size, DPI, radius, gloss) in a bounded LRU (`Opsis.UI.BoundedCache` pattern, disposing GPU objects); textures bounded; the union texture is resized only when the layout changes (with hysteresis).
6. **No per-frame allocation** in the render loop (reuse effects, brushes, geometry, buffers).
7. **Suspend** on idle (no input and nothing changing), lock screen, RDP/remote session, fullscreen game on that monitor, battery saver / low battery, minimized or hidden containers, and in safe mode.
8. **Target machine: 8 GB RAM** with Opsis + wallpaper + widgets + DeskStrip + containers running. RAM budgets are set after the baseline measurement (below); until then the rules above are the contract.

### 6.4 Budgets (proposed AFTER measurement; to be confirmed with the full Opsis baseline)
Measured today (spike, Intel UHD 630): glass layer for 4 windows at 60 fps live = **<= 3 % CPU, ~9 % GPU, 1.2 MB VRAM**. Proposed budgets for the glass layer on a mid machine, to be checked against the user's `tools/resource-logger.ps1` baseline of the 2.4.6.1 Opsis (not yet delivered by the user):

| Item | Idle | Live background (High tier) | Low tier |
|---|---|---|---|
| CPU of the containers/glass host | ~0 % | <= 3 % | ~0 % |
| GPU of the glass layer | ~0 % | <= 10 % (4 windows, 1080p-1440p) | 0 % |
| VRAM of the glass layer | <= 16 MB | <= 32 MB at 4K union | <= 4 MB |
| Private RAM of the containers host | <= 60 MB | <= 80 MB | <= 60 MB (**proposal, not measured**) |
| GDI / USER objects, handles | flat over 2 h (plateau test, plan 0.4d) | flat | flat |
The wallpaper itself is outside this budget but needs its own tier caps (section 6.1 note).

### 6.5 Glossy intensity (user request) - done in Opsis.UI 0.4.5
`GlassSettings.Glossy` (0-100) and three presets **Subtle / Glass / Very glassy** (they set glossy, rim light and shadow together), a slider and a preset dropdown on the Theme page (Advanced), `GlassState.Gloss` (0 on the Low tier), rim of every Flat control scales with it. The GPU glass of the new core reads the same state (specular highlight and refraction strength scale with `Gloss`).

### 6.6 Manual tests asked from the user (Windows 11)
1. Two monitors with different DPI: drag a glass pane between them; it must stay sharp and keep showing the live background.
2. DPI 150 % and 200 %: panes sharp, rim 1 px visible, no blur halo.
3. Real Opsis wallpaper behind the desktop icons: panes show it live; icons behind a pane are refracted; no flicker.
4. An AMD and an NVIDIA machine (or Remote Desktop): glass still shows, or falls back to the static tint without errors.
5. Lock and unlock, and Win+L / UAC prompt while glass is on: no black or frozen panes afterwards.


---

## 7. The animated wallpaper must cost less WITHOUT losing quality or stability (user request, 2026-11-10)

Finding: on the test machine the Waves wallpaper alone held 31-39 % GPU (Intel UHD 630, 1700x900 page, Balanced profile, 30 fps) and 3 % CPU, more than all the glass together. Three measures, in order of safety:

| # | Measure | Quality | Stability | Measured |
|---|---|---|---|---|
| 1 | **Pause where the desktop is fully covered** (`DesktopCoverage` + `AppActivity`, setting `PauseWhenDesktopCovered`, default on): a maximized / fullscreen opaque window on a monitor means the wallpaper there is invisible, so it is paused (the existing heavy pause freezes the WebView2 processes when every monitor is covered) and resumes within one second when the desktop shows. Tiled windows never count as covering. | none lost (nothing visible is changed) | needs two equal samples to pause, resumes at once; ignores Opsis' own windows, the shell, tool/layered/click-through windows | not measured live in an Opsis run (unit test of the geometry only); expected ~0 % while covered, i.e. most of a working day |
| 2 | **Cheaper drawing of the same picture** (`wallpaper/index.html`, Ribbons style): the full-resolution `blur()` filter for the shadow between folds is drawn on a quarter-resolution layer; the glow of the bright line (was `shadowBlur` at full resolution) is five wide transparent strokes; point arrays are reused (no per-frame allocation). `cfg.exactBlur = true` restores the old path. | visually the same (see `docs/spike2/wallpaper/waves-before.png` / `waves-after.png`); the glow is slightly softer; my first attempt (quarter-resolution glow) looked beaded and was rejected | only the Ribbons style changed; Aurora, TechGrid, Particles, Weather untouched | GPU **33-39 % -> 20-26 %** (same page, same fps) in 3 paired runs, CPU unchanged |
| 3 | Already there, kept: tier profiles (layers, glow, fps, render scale), battery / fullscreen / lock / RDP pauses | | | |

Not done (and why): lowering the frame rate below the tier value would change the motion; moving all the drawing to WebGL would be a rewrite of every style; video wallpapers keep their own decoder cost (hardware decode is the lever there).
The GPU counters on Intel are bursty; the paired runs agree in direction, not in the last digit.

## 8. Product library `glass/Opsis.Glass` (phase 1, written 2026-11-10)
`GlassManager`: one thread owns the devices and the windows; DXGI duplication per monitor with ONE union texture; Direct2D glass (blur, refraction at the bezel, tint, specular sheen from the glossy intensity, rim); DirectComposition panes with premultiplied alpha; `GlassPolicy` decides all work (capture only on change, 60 / 30 / 20 fps by tier and battery, Low = static tint, suspended on lock / remote / fullscreen / hidden / safe mode); bounded cache of displacement maps; device-lost and duplication-lost recovery; stats for the baseline. Pure logic has 15 unit tests (`glass/Opsis.Glass.Tests`). Smoke run `glass/Opsis.Glass.Demo` over the live wallpaper page (`docs/spike2/product/`): live High, 4 panes: ~36 captures/s (one per changed frame for all panes), ~145 pane-renders/s, **0.5-2.9 % CPU**, ~37-40 MB private memory of the whole demo process (it also hosts the WebView2 stage); static background, Low tier and locked: **0 captures, 0 renders, ~0 % CPU**; resume after unlock works.
Caveat: panes are excluded from capture so they never see themselves; the price is that screenshot and recording tools do not show them. `SetVisibleToCaptureTools` exists for a moment (the demo freezes the glass while taking a shot). To decide with the user whether a "show in screenshots" mode is wanted.
Not done yet in the library: per-monitor DPI scaling of panes (panes use the pixel size given), multiple adapters, hit-testing/click handling (panes are visual only; content windows come in phase 2), integration into DeskStrip and the widgets (their windows still use the DWM acrylic path in Opsis; the first integration is the next step).
