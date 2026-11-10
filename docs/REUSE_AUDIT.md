# Phase 0A: reuse audit (2026-11-10) - licences checked against the real LICENSE files

Clones (shallow, read only, never built or run) live in `<workspace>\reuse-src\` (outside the repo). Rule: MIT / Apache-2.0 / BSD may be reused with attribution; MPL-2.0 only with its files kept under MPL; GPL = idea only (clean-room). Everything below is a first classification; the actual port decisions come with the Phase 0 design.

| Source | Real licence (file) | Language | What is in it for Opsis | Classification | Rule |
|---|---|---|---|---|---|
| **PecoFence** (DayuanJiang) | Apache-2.0 (`LICENSE` + `NOTICE`) | Rust | Liquid glass: `crates/render/src/liquid_glass.rs` (1347 lines) + `gpu_glass.rs` (683); rules engine, snapshots, Peek, named pipe + CLI | B: port the algorithm to C#/Direct2D | Keep LICENSE + NOTICE text, state changes, add to `THIRD_PARTY_NOTICES.md`. No Rust toolchain in Opsis |
| **WinDynamicDesktop** (t1m0thyj) | **MPL-2.0** (`LICENSE`) | C# WinForms | Time-of-day / sunrise-sunset wallpaper switching, `.ddw` theme format, `EventScheduler`, `DesktopHelper` | B/C: logic can be re-implemented; copying files is allowed only if those files stay MPL-2.0 and remain available as source | Prefer clean re-implementation of the idea (solar times from location) inside Opsis' weather wallpaper; if a file is copied, keep it separate with its MPL header and list it in `THIRD_PARTY_NOTICES.md` |
| **Equinox** (rlxone) - the "macOS wallpaper repo" (link given by the user 2026-11-10) | MIT (`LICENSE`, (c) 2021 Dmitry Meduho) | Swift, macOS only | How a macOS dynamic wallpaper is built: sets of images + solar/time-of-day metadata (HEIC) | C: study only (cannot run on Windows); MIT allows reusing ideas/code with the notice | Use for the data model (day/night image sets, sun position / time tables). Not a code source |
| **LockscreenGif** (Leapward-Koex) | MIT (`LICENSE.txt`, (c) 2026) | C# .NET 10 + a privileged helper | Animated GIF as the Windows lock screen by writing the lock-screen image cache; video -> GIF with FFmpeg/Gifski; may need elevation and a Windows feature change + reboot | B: reuse allowed with notice. **Security note:** it needs an elevated helper and edits system cache files; do not add elevation to Opsis lightly (plan 0C.1) | Copy only the cache-writing logic if kept in an optional, user-approved helper; MIT notice in `THIRD_PARTY_NOTICES.md` |
| **FluentFlyout** (unchihugo) | **GPL-3.0** (`LICENSE`) | C# WPF | Media flyout UI, GSMTC (Windows media session) integration | D for code: **do not copy** (GPL-3.0 is incompatible with shipping inside the MIT Opsis). Clean-room only: GSMTC is a public Windows API (`GlobalSystemMediaTransportControlsSessionManager`), the flyout is our own design | Never read-and-paste; write from the Windows API docs |
| **Rainbow-Music-Visualizer-Wallpaper** (Aprotonix) | Declared **MIT only inside `LivelyInfo.json`**; there is **no LICENSE file** and the folder `Some font/` has 3 fonts (`MODERN-WARFARE.ttf`, `Potra.ttf`, `pavelt.ttf`) with **no licence information** | HTML / JS (Lively wallpaper) | Audio visualizer (`viz.js`), layout of the song info | C for the idea; **fonts: NOT cleared** | Do not ship the fonts until each font's licence is found (ask the user / author). The JS can be reused under the declared MIT, ideally after asking the author for a LICENSE file |

## Existing Opsis assets to reuse first (from the plan, not re-audited here)
`AtomicFile`, `GlassBlur` (DWM acrylic), `AutomationService`, `WorkspaceProfileService`, `PerformanceProfile`, `ProcessFreezer`, `ZoneSnapEngine`, `PluginHost`, `WallpaperWindow.CapturePreviewAsync`, `ZoneOverlayWindow`, and the new `Opsis.UI` 0.4.1 (Glass theme, settings layout, hotkey registrar, restart, status bar).

## Not verified / needs the user
- No build or run of any clone. No dependency scan.
- The fonts of Rainbow-Music-Visualizer (licence unknown) and whether the author will add a LICENSE.
- Whether WinDynamicDesktop's MPL files will be copied or only the idea used (decision for Phase 0).
- The 3D landmark models (plan 2.1.5): not searched here, each needs its own licence check.
