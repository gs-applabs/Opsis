# DeskContainers: fork (2.4.x, WPF `OpsisContainers.exe`) → native core (2.6.0 beta) — equivalence table

**Status 2026-11-12: the fork is REMOVED** (`containers/`, `BuildContainers` target, `ContainersEngine`/`ContainersBridge`, installer entries; the installer deletes the old engine folder on upgrade after putting back any icon it hid). Rows still ☐ below are now ✗ "missing" in the beta and are listed to the user in `docs/TRY-NATIVE-CONTAINERS.md`.
✓ done and checked · ◐ partly · ☐ not yet · ✗ dropped on purpose (plan §5 of `CONTAINERS_NATIVE_DESIGN.md`).

| Feature of the fork | Native core now | Evidence / note |
|---|---|---|
| Containers with icons, title, lock, roll-up, move, resize, snap | ✓ | `ContainerWindow` + `ContainerBehavior` (tests), `--test-container-window` |
| Own title-bar colour / height / font (new request) | ✓ | pipe `set-title-style`, rendered per container |
| Transparent glass with live background (new request) | ✓ behind `UseGpuGlass` | GPU glass pane; fallback = translucent dark card |
| Launch an item (double click, Enter, menu "Open") | ✓ | single-click launch option ☐ |
| Drag and drop IN (files from Explorer as references, no duplicates) | ✓ | `DropRules`, test in `--test-container-window` |
| Drag and drop OUT (file drag) and reorder inside | ✓ | out = drag keeps the item; Copy/Move/Link choice ☐ |
| Remove an item (reference only) / context menu | ✓ | own menu, no shell extensions loaded |
| Create / delete container (tray, hotkey, pipe, desktop right-click) | ✓ | Ctrl+Alt+Shift+N, tray menu, `create`, desktop verb |
| Quick-hide / quick-show all | ✓ | Ctrl+Alt+Shift+H, tray, pipe `toggle-all`; "Exclude from quick-hide" ☐ |
| Profiles | ◐ | layout **snapshots** (save/load/list/delete, rules kept); automation by profile ☐ |
| Import of the 2.4.x data | ✓ | read-only importer; first start imports Default as the live layout, other profiles as snapshots; old folder untouched |
| Persistence (atomic, schemaVersion, .bak, .corrupt) | ✓ | `ContainersStore` |
| Rules (monitor count, process, time, fullscreen app) | ✓ engine + host tick | UI for rules ☐; the fork's auto-organize rules (type/extension/name) ☐ |
| Z-order: above desktop, below apps; Win+D; Explorer restart | ◐ | z-order ✓ in the real z-order chain (`--test-container-zorder`), re-seated every second (covers Explorer restart logically); Win+D and a real Explorer restart ⛔ need the user (not run: they would disturb the live session) |
| Keyboard navigation, accessibility tree, High Contrast | ✓ | `ContainerNavigation`, MSAA tree; screen-reader pass ⛔ user |
| Tabs, portals (live folder), notes | ☐ | data model has them; no UI/rendering yet — **needed** |
| Hide the desktop originals when dropping / restore on every exit path | ☐ | the native host never hides icons (safe); the legacy quick-hide engine + `--restore-desktop-icons` rescue exist — **needed** |
| Spot Search (Ctrl+`), profile hotkeys, focus-container hotkey | ☐ | **needed** |
| Rubber-band selection → "create container" | ☐ | needs the item API (exists) + selection hook |
| Backups (daily/manual) | ☐ | snapshots are the manual part |
| Icon glow, launch effect, auto-hide after idle, hover roll-up | ☐ | cosmetic, can follow |
| Settings page for the native core | ✓ | DeskContainers page: on/off, per container title bar colour/opacity/height/font, lock, hidden, delete, layout snapshots |
| Installer: stop shipping `OpsisContainers.exe`, removal on upgrade | ✓ | `[InstallDelete]` + unhide before; upgrade 2.4.6.2 → 2.6.0 beta verified with an isolated test installer (other AppId, no admin) |
| Tray entry "DeskContainers" | ✓ | native variant of the menu |

## What the switch-off did
1. `UseNativeContainers` is the default (true); the first start imports your 2.4.x data (already implemented).
2. Remove `containers/` (WPF fork), the `MotionDeskStudio.csproj` build step and excludes, the installer entries (`containers\OpsisContainers.exe`, its `taskkill`/unhide steps), `ContainersEngine`/`ContainersBridge` and the legacy WinForms containers.
3. Keep: `--restore-desktop-icons` (rescue), the quick-hide engine (it moves desktop icons, independent of containers), the importer.
4. Upgrade from 2.4.6.x: the installer replaces the files; the old `%AppData%\Opsis\containers` folder is kept (never deleted) so a downgrade still works.
