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
