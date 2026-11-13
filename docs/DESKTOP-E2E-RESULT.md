# Desktop e2e on a hosted Windows runner (2026-11-13)
Workflow desktop-e2e.yml (run https://github.com/giorstergiopoulos-design/Opsis/actions/runs/43421997703, branch since deleted) ran on windows-2025 and PASSED:
- z-order (above the desktop, below a normal window, re-seated after SendToBack)
- live settings of containers
- real mouse: drop from a stand-in Explorer window, reorder, move between containers, single / double click launch, hover roll-up
- REAL desktop icons (a file and a folder created on the runner's desktop) dragged with the real mouse into a container: both arrived
- Win+D (show desktop) and an Explorer restart: the container stayed visible and above the desktop
Not covered by a hosted runner: several monitors / DPI, Win+L, a screen reader, a user's own elevated-vs-normal session (the likely reason the drop failed on the user's PC).
