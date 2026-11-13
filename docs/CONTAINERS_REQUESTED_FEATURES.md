# DeskContainers: what was requested -> where it stands (2.6.0 beta)

Sources used: the equivalence table (`CONTAINERS_EQUIVALENCE.md`), the old fork's feature list (options, `OPSIS_NOTES`), the user's requests in this work (tabs / portals / notes, own title bar, glass, drag and drop, hide originals, hover roll-up, fonts, palette, rubber band, live settings) and the coordinator's lists. It is NOT a read of every old transcript: anything the user asked for that is not in those sources is missing here and should be added when it comes up.

Status: DONE = implemented and checked (test or live) · DONE-U = implemented, not checked on a real user desktop · MISSING = not done.

| Requested | Status | Note |
|---|---|---|
| Containers with icons, title, lock, roll-up, move, resize, snap | DONE | |
| Own title bar colour / opacity / height | DONE | |
| Title bar colour from a PALETTE (shades + custom) | DONE | `ColorPalette`, in the container's own settings and on the DeskContainers page |
| Font family and size for the title and for the icon labels | DONE | |
| Transparent glass with the live background | DONE-U | GPU glass pane; tint colour + strength and glossy highlight per container are new (schema 2) |
| Settings on the DeskContainers page work | DONE | the stored style is applied to the open windows (it was lost before); same panel as the right-click settings |
| Settings from the container itself (right-click "Container settings...") and from the tray | DONE | |
| Live settings (no restart) | DONE | test in `--test-container-host` |
| Drag and drop IN (Explorer / desktop) | DONE-U | works with a stand-in Explorer window and real mouse input (`--test-container-dnd`). Real desktop icon: NOT verified here. Likely cause of the user's failure: Opsis was started elevated by the installer (UIPI blocks the drop); fixed (runasoriginaluser + relaunch with normal rights) |
| Drag and drop reorder inside, and between containers | DONE | Move by default, Ctrl = Copy (setting reverses); a drag outside never moves the real file (Copy/Link only) |
| Single / double click to launch (per container) | DONE | |
| Hover roll-up with delay (per container) | DONE | |
| Rubber-band: select desktop icons -> make a container | DONE-U | a button appears next to the pointer after a selection of 2 or more icons (setting) |
| Exclude a container from quick-hide | DONE | |
| Focus-container hotkey | DONE | Ctrl+Alt+Shift+F cycles through the containers |
| Backups (daily + "Back up now") | DONE | `container-backups`, 7 kept |
| Tabs, portals, notes | DONE | |
| Hide originals on drop, always restored (exit / start / tray / CLI) | DONE | |
| Spot Search, profile hotkeys, snapshots | DONE | |
| Widgets: settings from the right-click menu | DONE | opens the Widgets page |
| DeskStrip: settings from the right-click menu, live repaint | DONE | |
| Icons with the same size as the desktop icons | DONE | icon size 0 = the desktop size, adjustable per container |
| Delete a container -> its originals come back | DONE | |
| Rules (monitor, process, time, fullscreen) | DONE | no UI for editing rules |
| Auto-organise by type / extension / name | MISSING | |
| Launch effects, icon glow, auto-hide after idle | MISSING | cosmetic |
| Customise / edit shortcut dialog, icon picker, "Chameleon" look, menu icon | MISSING | not carried from the fork |

## Why the tests did not catch the drag and drop problems
The earlier tests called `AcceptDrop` and `MoveItem` directly, so the OLE drop target, the real mouse and the cross-container case were never exercised. Two real bugs were hidden by that: a drag between containers re-ordered by the wrong container's index, and style changes were lost on the next window move. `--test-container-dnd` now drives the real mouse (cursor moves + button down / up), a stand-in Explorer window starts a genuine OLE drag, and the checks wait and retry instead of sleeping once. It is a diagnostic, not part of `dotnet test`, because it moves the real cursor.

## From the user's own request history (`OPSIS-USER-REQUEST-HISTORY.md`, container-related lines only)
| Request (as worded by the user) | Status |
|---|---|
| DeskZones should work like FancyZones, not like Fences | not part of the containers core (DeskZones is a separate page) |
| DeskContainers: resizable, multiple colour palette, auto-hide, visual effects such as opacity %, a menu with options like Fences, and extra ideas | resizable DONE · palette DONE · opacity DONE · menu DONE (right-click settings) · **auto-hide MISSING** · launch / glow effects MISSING |
| Fences-style double click on an empty desktop spot hides / shows desktop icons, except Computer, the user folder and Recycle Bin | exists as a setting of the quick-hide on the DeskContainers page, with the "icons that stay visible" list (not changed here) |
| "Create container from these icons / files" (Fences function) built into DeskContainers | DONE-U (rubber-band offer after a selection of 2 or more icons) |
| Rename every "fence" to "OpsisContainer" | not checked in this pass |
| Deleting a container must bring its icons back; Opsis needs a New / Delete container button; old buttons back, connected to the new core | DONE (delete restores the originals; buttons are on the DeskContainers page) |
| Title bar with its own colour and size, Windows 11 22H2+ | DONE |
| Settings of containers, DeskStrip and widgets apply live; container settings reachable from the container | DONE |
| Drag and drop from the desktop like the old containers and Fences | implemented; real desktop icon NOT verified (see above) |
| "Opsis teal" as a separate preset in Base theme | NOT done: the base-theme list lives in the shared Opsis.UI library (`GlassSettingsPage`), so it needs a change there; the Glass tint already defaults to teal `#00BCD4` |

Still MISSING from everything above: auto-hide after idle, launch effects / glow, auto-organise rules, a UI for rules, the "Opsis teal" base theme.
