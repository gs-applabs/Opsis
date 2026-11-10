# DeskContainers - native core: spike result and cost table (for the user's approval)

Status 2026-11-10. **Nothing of the new core has been built.** This document is the Phase 0A spike result and the cost comparison the plan asks for; building starts only after the user approves the table (decision at the end).

## 1. The spike (what was measured)
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

## 3. Recommendation (mine; the decision is the user's)
**Option B**, built in this order (each step releasable on the 2.6.0 beta branch): (1) `Opsis.Glass` as a library, first on DeskStrip and the widgets, to test it; (2) data model + importer + pipe/CLI; (3) container windows on WPF with the fork's UI parts moved over; (4) rules, profiles, snapshots, hotkeys; (5) switch off the fork. Reasons: the spike removes the doubt about WPF top-level windows; a full rewrite (A) would redraw things that already work; evolving in place (C) leaves the faults the plan wants gone (one tray, one hiding mechanism, no file bridge).

Before step (1) is accepted as "done" the missing measurements above must be made on the real Direct2D version, with two monitors at different DPI, and with Opsis' live wallpaper running (manual table for the user).

## 4. Decision needed from the user
1. Approve **B** (or choose A or C).
2. Minimum Windows 11 build (proposal 22H2+; the system backdrop attribute used by Opsis.UI needs 22621).
3. Containers engine process: inside the Opsis process (cleaner) or a separate host first (solves drag and drop when Opsis runs elevated; the plan suggests starting separate).
4. One mechanism to hide desktop icons: move the `SysListView32` items off screen (what the fork does) with a mandatory restore on every exit path, or the hidden attribute.
