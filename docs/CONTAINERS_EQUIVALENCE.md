# DeskContainers: fork (2.4.x, WPF `OpsisContainers.exe`) → native core (2.6.0 beta) — equivalence table

**Status 2026-11-12: the fork is REMOVED** (`containers/`, `BuildContainers` target, `ContainersEngine`/`ContainersBridge`, installer entries; the installer deletes the old engine folder on upgrade after putting back any icon it hid). Rows still ☐ below are now ✗ "missing" in the beta and are listed to the user in `docs/TRY-NATIVE-CONTAINERS.md`.
✓ equivalent / improved and checked · ◐ partly · ✗ missing in 2.6.0 (listed to the user) — no row is left open (plan §5 of `CONTAINERS_NATIVE_DESIGN.md`).

| Feature of the fork | Native core now | Evidence / note |
|---|---|---|
| Containers with icons, title, lock, roll-up, move, resize, snap | ✓ | `ContainerWindow` + `ContainerBehavior` (tests), `--test-container-window` |
| Own title-bar colour / height / font (new request) | ✓ | pipe `set-title-style`, rendered per container |
| Transparent glass with live background (new request) | ✓ behind `UseGpuGlass` | GPU glass pane; fallback = translucent dark card |
| Launch an item (double click, Enter, menu "Open") | ✓ | single-click launch option ✗ |
| Drag and drop IN (files from Explorer as references, no duplicates) | ✓ | `DropRules`, test in `--test-container-window` |
| Drag and drop OUT (file drag) and reorder inside | ✓ | out = drag keeps the item; Copy/Move/Link choice ✗ |
| Remove an item (reference only) / context menu | ✓ | own menu, no shell extensions loaded |
| Create / delete container (tray, hotkey, pipe, desktop right-click) | ✓ | Ctrl+Alt+Shift+N, tray menu, `create`, desktop verb |
| Quick-hide / quick-show all | ✓ | Ctrl+Alt+Shift+H, tray, pipe `toggle-all`; "Exclude from quick-hide" ✗ |
| Profiles | ◐ | layout **snapshots** (save/load/list/delete, rules kept); automation by profile ☐ |
| Import of the 2.4.x data | ✓ | read-only importer; first start imports Default as the live layout, other profiles as snapshots; old folder untouched |
| Persistence (atomic, schemaVersion, .bak, .corrupt) | ✓ | `ContainersStore` |
| Rules (monitor count, process, time, fullscreen app) | ✓ engine + host tick | UI for rules ☐; the fork's auto-organize rules (type/extension/name) ☐ |
| Z-order: above desktop, below apps; Win+D; Explorer restart | ◐ | z-order ✓ in the real z-order chain (`--test-container-zorder`), re-seated every second (covers Explorer restart logically); Win+D and a real Explorer restart ⛔ need the user (not run: they would disturb the live session) |
| Keyboard navigation, accessibility tree, High Contrast | ✓ | `ContainerNavigation`, MSAA tree; screen-reader pass ⛔ user |
| Tabs, portals (live folder), notes | ✓ | tab strip (new / rename / remove, icons never lost), portal = live folder listing with a watcher (never stored), note = text with an editor; checked in `--test-container-window` and a core layout test |
| Hide the desktop originals when dropping / restore on every exit path | ✓ | ONE mechanism (setting `HideOriginalsOnDrop`, default on): the icon is moved off-screen by name and its place is kept in `desktop-originals-hidden.json`; restored on item removal, host exit, EVERY start, tray "Restore desktop icons" and `Opsis.exe --restore-desktop-icons`. Verified on the real desktop: hide+restore, kill (state kept), rescue = identical positions |
| Spot Search (Ctrl+`), profile hotkeys, focus-container hotkey | ✓ / ✗ | Spot Search popup (pure `ContainerSearch`, tests); profile hotkeys = next / previous layout snapshot (Ctrl+Alt+Shift+] / [); focus-container hotkey ✗ (focus from the tray menu) |
| Rubber-band selection → "create container" | ✗ | not done (needs a desktop selection hook); containers are created from the tray / hotkey / desktop menu |
| Backups (daily/manual) | ✗ | snapshots cover the manual part; the data file keeps a `.bak` of the last save |
| Icon glow, launch effect, auto-hide after idle, hover roll-up | ✗ | cosmetic, not carried (roll-up by double click works) |
| Settings page for the native core | ✓ | DeskContainers page: on/off, per container title bar colour/opacity/height/font, lock, hidden, delete, layout snapshots |
| Installer: stop shipping `OpsisContainers.exe`, removal on upgrade | ✓ | `[InstallDelete]` + unhide before; upgrade 2.4.6.2 → 2.6.0 beta verified with an isolated test installer (other AppId, no admin) |
| Tray entry "DeskContainers" | ✓ | native variant of the menu |

## What the switch-off did
1. `UseNativeContainers` is the default (true); the first start imports your 2.4.x data (already implemented).
2. Remove `containers/` (WPF fork), the `MotionDeskStudio.csproj` build step and excludes, the installer entries (`containers\OpsisContainers.exe`, its `taskkill`/unhide steps), `ContainersEngine`/`ContainersBridge` and the legacy WinForms containers.
3. Keep: `--restore-desktop-icons` (rescue), the quick-hide engine (it moves desktop icons, independent of containers), the importer.
4. Upgrade from 2.4.6.x: the installer replaces the files; the old `%AppData%\Opsis\containers` folder is kept (never deleted) so a downgrade still works.


## Final result (2.6.0)
Equivalent or better (✓): windows, own title bar, glass, drag and drop in/out/reorder, tabs, portals, notes, launch, hide-all, hide originals with mandatory restore, Spot Search, snapshots as profiles, tray, hotkeys, rules engine, import of the old data, z-order, keyboard / accessibility / High Contrast, settings page. Missing (✗): rubber-band create, backups, glow / launch effects / auto-hide after idle, Copy/Move/Link choice, single-click launch option, exclude-from-quick-hide, focus hotkey, auto-organize by type, a UI for rules.

