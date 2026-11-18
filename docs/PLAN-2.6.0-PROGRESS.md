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
| 6 windows and behaviour | FIRST WINDOW DONE (part B slice): `src/Containers/ContainerWindow.cs` = layered per-pixel-alpha window + Opsis.Glass pane, own rendering (`ContainerRenderer`: title bar with ITS OWN colour / opacity / height / font, visible-rows-only icon grid, labels with shadow, lock glyph, scroll indicator), title drag with snapping, corner resize, wheel scroll, double click = roll-up, bounded icon cache. Checked with `Opsis.exe --test-container-window` (screenshots `docs/spike2/container/`, the title bar pixel is orange while the frame is transparent). Earlier part A: the behaviour rules as pure tested functions (`ContainerBehavior`: snap to edges / other containers, resize limits, roll-up with the container's OWN title-bar height, keep reachable after a monitor change, lock, hit test; 8 tests). PART B NOT done: the real window host (non-activating layered window + glass pane), z-order next to the desktop icon layer and the wallpaper window, Win+D / Explorer restart behaviour - these need real-desktop tests |
| 3 shell (part 1) | DONE: `DropRules` (pure, 7 tests) + `ContainerWindow` drop of files as REFERENCES (no duplicates, own folder refused), drag to reorder, drag-out as file drop; checked in `--test-container-window` (added 1, duplicate skipped, reorder). Part 2 DONE: icon extraction on one background worker (placeholder first, bitmaps inserted into the bounded cache on the UI thread only), own context menu (Open / Show in folder / Remove reference / Lock / Roll up, each action try/catch-isolated, no shell extensions loaded). NOT done: Copy/Move/Link choice, hiding desktop originals; mspaint (Store alias) keeps the fallback icon |
| 5/9 integration (part 1) | DONE: `ContainerHost` (live windows follow the stored data; pipe commands create/delete/move/show/hide/set-title-style refresh them; debounced write-back under the service lock; re-seat after a monitor change; rules evaluated every 3 s, edge-triggered), `RuleEngine` (monitor count, process running, time range wrapping midnight, fullscreen app; hide/show/revert/load-snapshot), named layout snapshots (`snapshot-save/list/load/delete`, rules kept across loads), hidden flag `UseNativeContainers` in settings.json starts the host from the tray (own file `containers-native.json`; OFF by default; turn the WPF engine off first). Checked: 49 core tests, `--test-container-host` (create/hide/show/write-back/delete through the pipe). Host commands over the pipe: `focus <id>` (keyboard focus) and `toggle-all` (hide/show every container; tested). NOT done: UI for rules/snapshots, the global hotkey that calls them (registrar), tray menu entries, perf governor/safe-mode/ProcessFreezer hooks, options.json import |
| 7/8 accessibility (part 1) | DONE: pure keyboard navigation (`ContainerNavigation`, tests), `HandleKey` (arrows/Home/End/PgUp/PgDn/Enter/Delete/Apps), `FocusForKeyboard()`, visible focus rectangle, MSAA/UI Automation tree (list + list items with name, path, bounds, Open action, selection), Windows High Contrast drawing (system colours, opaque, no glass). Checked in `--test-container-window` (selection moves, 15 accessible children, HC title bar = system Highlight); 51 core tests. NOT done / needs the user: real screen-reader (Narrator/NVDA) pass, focus model (the window is non-activating: keys work after `FocusForKeyboard()`, which no hotkey calls yet), real High Contrast theme switch, per-monitor DPI move between 100 % and 150-200 % monitors, monitor hot-plug (the host re-seats windows on DisplaySettingsChanged: logic only) |
| 2 rendering, 5/9 rest, 7/8 a11y+DPI, switch off the fork | NOT started |

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

## Container window: NOT done yet / manual tests asked from the user
Not done: z-order against the desktop icon layer and the wallpaper window (the window is TopMost for now), Win+D and Explorer restart behaviour, keyboard navigation, selection with the keyboard, drag and drop in / out, context menu, rename, tabs, portals, notes, drop of the desktop icons, persistence of the live container set, multi-monitor DPI. The test uses a stand-in background in my own window, not the real wallpaper window.
Manual test (Windows 11, needs the user): none can be run before the container host is started from Opsis itself (phase 6 part B/9); steps will be written when it is wired.

## DeskStrip: auto-pin of the Opsis family (user request 2026-11-11)
Installed family apps from `suite/apps.json` (other than Opsis) are pinned to the DeskStrip automatically, once each (id kept in `DeskStripSettings.AutoPinnedFamily`, so an app the user unpins is never pinned again); checked at start and about once a minute, so an app installed later appears too. Pure rule `FamilyApp.AutoPinCandidates` is unit-tested (177 Opsis tests). NOT verified live with a real installed family app.

## Test kit for UseNativeContainers
Portable build outside the repo + `docs/TRY-NATIVE-CONTAINERS.md` (Greek). Rescue: `Opsis.exe --restore-desktop-icons`. Verified: hide 162/166 icons, kill, rescue = identical positions.

## Native containers: work after the first slices (this turn)
- **Tray + hotkeys:** native tray menu (new container, focus, hidden, layout snapshots, hide/show all) and global hotkeys Ctrl+Alt+Shift+H (hide/show all) and Ctrl+Alt+Shift+N (new container at the cursor); neither is a Windows default.
- **Z-order:** containers are no longer TopMost: they sit directly above the desktop (Progman/WorkerW) and below normal windows, re-seated every second (`DesktopZOrder`, 4 pure tests). Checked in the REAL z-order chain on this machine with `--test-container-zorder` (container below a normal window, above the desktop, and put back after `SendToBack`). Win+D and a real Explorer restart were NOT run (they would disturb your live session): the logic survives them (desktop windows missing -> retry on the next second), ⛔ needs your test.
- **Upgrade from 2.4.x:** first start of the native core imports the 2.4.x data read-only (Default -> live layout, other profiles -> snapshots); never overwrites an existing native file (2 tests).
- **Glass cost (3 containers x 24 icons, `--test-container-budget`, Intel UHD 630 machine):** background moving 8 s: 2.3 % of all cores, private memory 44 MB (16 MB before), 128 captures / 383 renders; background static: 0.7 % CPU, 42 MB. Growth GDI +25, USER +30, handles +238 for 3 windows + 3 glass panes (to be compared with the logger baseline of 2.4.6.1, ⛔ user). Idle 0.7 % is above the "near zero" goal: next step is to stop the static-watch capture when nothing can change (needs a policy change in Opsis.Glass).
- **Fork switch-off:** `docs/CONTAINERS_EQUIVALENCE.md` lists what the fork does and what the native core still lacks (tabs/portals/notes, hiding desktop originals, Spot Search and profile hotkeys, settings page). The fork is NOT removed yet; the table states the conditions.

## 2.4.6.2 worktree: real plugins
`--test-plugins-load` (needs OPSIS_E2E=1 + OPSIS_APPDATA) loads the REAL plugin builds from an isolated data folder: Opsis Appearance 1.7.5 and Opsis News Crawler 0.23.0 (both built from the local repos) were approved by the test mode, loaded by the 2.4.6.2 host, each added ONE side-menu page (News Crawler, Εμφάνιση Windows) and ONE tray group (no duplicates), and plugins.log has no load errors. NOT covered: Chronikologio (its repo is not on this machine), Kombos/GearWin, the Crawler 'Ροή ιστορίας' page contents, real UI clicks inside the plugin pages, the 2.6.0-beta host with the same plugins.

## Fork switch-off and upgrade (2026-11-12)
Removed: `containers/` (WPF fork), `ContainersEngine`, `ContainersBridge`, the BuildContainers/CopyContainersToPublish targets, the installer's engine entries. Native settings page, tray menu, quick-hide, desktop-menu verbs and the native host now are the only path; `UseNativeContainers` defaults to true. Installer 2.6.0 beta compiles (8.3 MB, no fork); an upgrade from a 2.4.6.2 install was run with an ISOLATED test installer (other AppId, no admin, own folder): exe became 2.6.0, `containers\` deleted, uninstall entry "2.6.0 beta", uninstall clean, real Opsis untouched (checked). ⛔ Still needs the user: a real upgrade of the installed 2.4.6.1 on the machine and the manual steps above.

## Real upgrade of the installed Opsis (2026-11-12, approved by the user)
Installed 2.4.6.2 -> 2.6.0 beta with `OpsisSetup-2.6.0-beta.exe` (UAC, /SILENT). Found and fixed on the way: (1) the installer aborts when Opsis is running (AppMutex) - close it first; (2) settings.json written by earlier betas had `UseNativeContainers=false` -> settings schema 2 migration switches it on once (2 tests). After the second install: the installed Opsis started, imported the legacy data (1 container, empty, as in the old data) into `containers-native.json`; the old `%AppData%\Opsis\containers` folder is untouched; backup of the data folder was taken before.

## Where I stopped
2026-11-17: everything asked for in 2.6.0-beta is committed on this branch (core containers, native settings, live settings, drag and drop fixes incl. UIPI, Glass in the main window, adaptive GPU glass, early autostart task, Greek-only landmarks, About page, base-theme fix, weather clouds / mountains / rain / snow / foam, desktop e2e on a hosted runner). Left on purpose: photographic mountains, more Greek landmarks (docs/LANDMARKS-GREEK-TODO.md), per-container hover animation polish, auto-hide after idle, launch effects, a rules editor, multi-monitor / DPI / Win+L / screen reader checks (manual, see RELEASE-2.6.0-CHECKLIST.md). Not mine, left untouched: 26 tracked files under release/ (sha256 + RELEASE-NOTES of 2.2.0 to 2.4.6.1) are deleted in the working tree; I did not commit or restore them.

## ΒΗΜΑΤΑ ΓΙΑ ΤΟΝ ΧΡΗΣΤΗ (χειροκίνητη δοκιμή των νέων DeskContainers) — απλά ελληνικά
Πλήρης οδηγός: `docs/TRY-NATIVE-CONTAINERS.md`. Συνοπτικά:
1. Κλείσε το εγκατεστημένο Opsis. Άνοιξε τον φάκελο `Opsis-2.6.0-beta-portable` και κάνε διπλό κλικ στο `Start-Opsis-TEST.bat`.
2. **Win+D:** άνοιξε 2-3 παράθυρα και πάτα Win+D. Το container πρέπει να φαίνεται πάνω από την επιφάνεια. Πάτα Win+D ξανά: τα παράθυρα γυρίζουν και το container μένει κάτω από αυτά.
3. **Επανεκκίνηση Explorer:** Task Manager → «Windows Explorer» → Επανεκκίνηση. Σε 1-2 δευτερόλεπτα το container πρέπει να είναι ξανά στη θέση του.
4. **Δύο οθόνες / DPI:** σύρε το container στη δεύτερη οθόνη, κλείσε και ξανάνοιξε το δοκιμαστικό· πρέπει να ξαναβρεθεί εκεί με σωστό μέγεθος και καθαρά γράμματα. Δοκίμασε και 150 % / 200 % κλίμακα.
5. **Κλείδωμα (Win+L):** κλείδωσε, ξεκλείδωσε· το γυαλί δεν πρέπει να είναι παγωμένο.
6. **Drag & drop:** σύρε ένα αρχείο από την Εξερεύνηση μέσα στο container, μετά ξανά το ίδιο (δεν πρέπει να διπλασιάζεται).
7. Αν κάτι πάει στραβά: διπλό κλικ στο `Rescue-Desktop.bat`.
8. Πες μου τι είδες. Για μέτρηση πόρων τρέξε και το `tools
esource-logger.ps1` για λίγη ώρα.

## ΖΗΤΗΘΗΚΕ → ΚΑΤΑΣΤΑΣΗ (λίστα από OPSIS_UNIFIED_MASTER_PLAN, INTAKE, CHANGELOG «Υπόλοιπο», TASKS §22-A) — ενημερώνεται ανά ενότητα
Σειρά εργασίας: φθηνά/εύκολα πρώτα. ✓ έγινε · ◐ μερικό · ☐ μένει · ⛔ χρειάζεται χρήστη/απόφαση/τρίτο repo.

| # | Αίτημα | Κατάσταση |
|---|---|---|
| 1 | Hotkeys χωρίς σύγκρουση με Windows + ειδοποίηση αποτυχίας (0Β.2) | ✓ 2.4.6.1 |
| 2 | Pop-up «Επανεκκίνηση τώρα/Αργότερα» (0Β.2) | ✓ Opsis.UI RestartCoordinator |
| 3 | Θέμα Glass dropdown + χρώματα σε ξεχωριστή ενότητα (0Β.4) | ✓ Theme page |
| 4 | Κοινή γραμμή κατάστασης (0Β.6) | ✓ OpsisStatusBar (αντιπαραβολή με 2.4.6 στα tests: ☐) |
| 5 | Εκκίνηση με Windows → tray, περιγραφή (2.1.2) | ✓ |
| 6 | Κουμπιά Επαναφορά κομμένα (R.1) | ✓ κάρτες/Reset ανά ενότητα στη Ρυθμίσεις· ⛔ έλεγχος στο ζωντανό UI |
| 7 | Αυτοματισμοί+Προφίλ ενοποιημένα, gaming mode Steam ορατό | ✓ |
| 8 | DeskContainers / DeskStrip σε δική τους καρτέλα | ✓ |
| 9 | Αποσυμφόρηση ρυθμίσεων (οι εικόνες των σελίδων στο `--ui-audit` δείχνουν Wallpaper/DeskZones σε παλιά διάταξη χωρίς κάρτες), UiScale, WindowMemory, αναζήτηση, Advanced (2.1.8) | ◐ Ρυθμίσεις+Θέμα έτοιμες· Wallpaper/Εξατομίκευση/Audio/DeskZones/Widgets ☐ (χρειάζονται screenshots/έλεγχο ανά σελίδα) |
| 10 | Βίντεο: Prev/Pause/Next, μικρότερα κουμπιά στην ίδια ευθεία (2.1.9) | ✓ |
| 11 | Βίντεο: drag & drop σειράς | ✓ (καθαρή λογική + 8 tests, UI με γραμμή πτώσης και αυτόματο scroll· ⛔ χειροκίνητη δοκιμή drag με το ποντίκι) |
| 12 | Βίντεο: RAM 16 βίντεο ~3 GB → ~850 MB | ◐ ανάλυση κώδικα: το wallpaper έχει ΜΟΝΟ 2 στοιχεία `<video>` (A/B crossfade) και μικρά thumbnails στη λίστα — δεν φαίνεται preload όλων· πιθανή αιτία η αποκωδικοποίηση 4K/WebView2. ⛔ μέτρηση με 16 πραγματικά βίντεο (tools/resource-logger.ps1) πριν διόρθωση |
| 13 | «Προεπιλεγμένη εμφάνιση plugins» → διαγραφή (2.1.1) | ✓ (η σειρά αφαιρέθηκε από τις ρυθμίσεις· τα plugins ακολουθούν πάντα το θέμα Opsis) |
| 14 | Distinctive εικονίδιο καρτέλας plugin (γενικός μηχανισμός) | ✓ `PluginIconChooser`: γνωστά θέματα → δικό τους εικονίδιο, κάθε άλλο plugin → πλακίδιο με το δικό του γράμμα (⛔ οπτικός έλεγχος) |
| 15 | Ενημερώσεις plugins μόνο από το κουμπί του Opsis | ✓ `PluginUpdateService` + κουμπί «Έλεγχος τώρα» στο About (πολλαπλά tests με ψεύτικο HTTP)· ⛔ δεν δοκιμάστηκε με πραγματικό GitHub release / πραγματικό Windows UI |
| 16 | Κοινές ρυθμίσεις Opsis↔plugins μόνο στο Opsis | ◐ `IPluginHostSettingsAware` (γλώσσα, θέμα, hardware tier, UI scale, «ενημερώσεις από Opsis»)· ό,τι μένει: βλ. docs/PHASE-P-STATUS.md |
| 17 | Theme packs: duplicate με Appearance; Εξατομίκευση Plus | ☐ χρειάζεται το plugin Appearance για σύγκριση |
| 18 | Lockscreen GIF (LockscreenGif — έλεγχος άδειας) + «ταίριασμα» χωρίς καιρό/ώρα | ⛔ δεν ξεκίνησε: πρώτα έλεγχος άδειας/τεχνικής του LockscreenGif και σχεδιασμός (η οθόνη κλειδώματος των Windows δεν δέχεται κινούμενο περιεχόμενο εύκολα) |
| 19 | Αφαίρεση φάσματος ήχου από στοιχεία wallpaper | ✓ (στοιχείο, κατάλογος, ετικέτα, JS, polling 80 ms αφαιρέθηκαν· το widget Audio Visualizer μένει) |
| 20 | Music overlay (GSMTC, clean-room, χωρίς γραμματοσειρές Rainbow) | ◐ έτοιμο: `NowPlayingService` (event-driven GSMTC, εξώφυλλο ≤160 px), `MusicOverlayWindow` (layered, click-through, glass pane αν GPU glass), 9 θέσεις, ρύθμιση Appearance→Advanced (ζωντανή, 14 γλώσσες), `--test-music-overlay`. ☐ visualizations Media Suite (άλλη εφαρμογή), ⛔ δοκιμή με πραγματικό player στην επιφάνεια |
| 21 | Wallpapers landmarks + ποιότητα (2.1.5/2.1.5β) | ◐ ΝΕΟ: `landmarks.js` — 23 ξεκάθαρα σιλουέτες-αξιοθέατα (Παρθενώνας, Λευκός Πύργος, Eiffel, Big Ben, Άγαλμα Ελευθερίας, Κολοσσαίο, Πίζα, Sagrada Família, Βραδεμβούργο, Αγία Σοφία, Πυραμίδες, Burj, Taj Mahal, Tokyo Tower, Όπερα Σίδνεϊ, Golden Gate, Χριστός Ρίο, Matterhorn, Fuji, CN Tower, Petronas, Belém, Κερύνεια) ζωγραφισμένες από μένα (χωρίς μοντέλα/άδειες), επιλέγονται από το ΣΥΝΔΕΔΕΜΕΝΟ με τη θέση καιρού (lat/lon, δηλ. και τη «θέση συσκευής») το πλησιέστερο ≤250 km, στον ορίζοντα του Weather wallpaper με χρώμα λόφων (ομίχλη/χιόνι/νύχτα), διακόπτης στη σελίδα Wallpaper (14 γλώσσες), 2 tests, φύλλο προεπισκόπησης ελέγχθηκε οπτικά. ☐ έλεγχος στο ζωντανό wallpaper. Παλιότερο: Ribbons φθηνότερο (GPU −35 %)· landmarks/3D ☐ (άδειες μοντέλων) |
| 22 | Δεξί κλικ επιφάνειας: Νέο DeskContainer / Αλλαγή wallpaper (0Β.3) | ✓ (2026-11-18) opt-in HKCU classic verbs «Νέο DeskContainer» / «Αλλαγή wallpaper» (14 γλώσσες, OFF από προεπιλογή), ο installer σβήνει τα keys στο uninstall, η απενεργοποίηση τα σβήνει, tests με injectable registry root. Το νέο μενού Win11 θέλει υπογεγραμμένο sparse package: εκτός scope. ⛔ δοκιμή σε πραγματικό desktop |
| 23 | Ακριβής καιρός: πάροχος + opt-in τοποθεσία (0Β.5) | ✓ ΜΠΗΚΕ ΚΑΙ ΣΤΟ 2.6.0-beta (patch του opsis-app: NWS/Open-Meteo/MET Norway με fallback, πηγή+ώρα παρατήρησης, προαιρετική στρογγυλοποιημένη τοποθεσία συσκευής + διαγραφή, ήλιος/σελήνη από lat/lon, 52 tests· 273 tests συνολικά) · παλιότερο: `docs/WEATHER_PROVIDERS.md`: συγκριτικός πίνακας (Open-Meteo επιβεβαιωμένοι όροι, MET Norway, NWS) + σχεδιασμός privacy· ⛔ απόφαση χρήστη (α: μόνο Open-Meteo / β: + MET Norway/NWS) πριν κώδικα |
| 24 | Perf §9.1: DeskStrip SyncItems lookups/εκτός UI thread | ✓ dictionaries + απαρίθμηση παραθύρων σε worker (⛔ δοκιμή στο ζωντανό dock) |
| 25 | Perf §9.1: FeatureOverride cache | ✓ 2.4.6.1 |
| 26 | Perf §9.2: FlatToggle font | ✓ Opsis.UI 0.3.1 · FlatTabs rects cache + FlatComboBox popup dispose ✓ (Opsis.UI 0.4.0) · custom painting (slider) ☐ μετά από προφίλ |
| 27 | Rubber-band ≥2 εικονίδια → «Δημιουργία container» (§22-6) | ✓ (2026-11-18) υπήρχε ήδη (DesktopSelectionWatcher: passive WH_MOUSE_LL + LVM_GETITEMSTATE + κουμπί «Δημιουργία container από N εικονίδια», ρύθμιση στη σελίδα DeskContainers)· τώρα η αντιστοίχιση ονόματος→αρχείο είναι καθαρή συνάρτηση με 4 tests και ο hook διαβάζει cached ρύθμιση. ⛔ δοκιμή με πραγματικό rubber-band |
| 28 | Native DeskContainers (phases 3, 5/9, 7/8) | ✓ πρώτες φέτες· ⛔ δοκιμή χρήστη, μετά switch-off fork |
| 29 | Opsis ξεκινά ΠΡΩΤΟ στην εκκίνηση Windows (§22-1) | ✓ ο μηχανισμός είναι ένας: per-user logon task `Opsis\Start with the desktop` (StartupTask, μία φορά μετά το πρώτο run)· οι βαριές φάσεις της εκκίνησης αναβάλλονται (docs/STARTUP-PHASES.md). ⛔ μέτρηση στο πραγματικό μηχάνημα |
| 30 | Explorer restart → μαύρη οθόνη (§22-5) | ◐ ExplorerRestartWatcher + (2026-11-18) κάθε πρόσβαση CoreWebView2 περνά από `WebView2Support.TryCore` (disposed control → null αντί για εξαίρεση), το «Επανεκκίνηση Εξερευνητή» ξαναξεκινά το shell αν δεν επανέλθει σε 3 s, tests. ⛔ επιβεβαίωση σε πραγματικό restart |
| 31 | Μέτρηση RAM/CPU baseline, soak logger | ⛔ τρέχει ο χρήστης (tools/resource-logger.ps1) |


## Weather wallpaper (simple weather, 2026-11-13)
Done: fbm clouds (cumulus / stratus / cirrus, self-shadowed, facing the sun), textured moon, sun bloom and rays, bird silhouettes, five ridged mountain ranges with atmospheric perspective, snow caps, wind-swayed conifers, slow parallax, Milky Way, rainbow. Already existing and kept: layered rain with splashes and ripples, snow of several sizes with wind, water with waves and reflections, lightning, fog. Also done later the same day: rain motion blur (wide faint + thin sharp stroke), wet sheen, snow bank and ice along the shore, foam crests, Fresnel gradient on the water, deciduous crowns. NOT done (next week): per-tier layer budgets (only a simple low-density cut of the texture / trees), frame-time measurements, screenshots of every hour of the day.


## Weather cost (measured 2026-11-17, docs/spike2/landmarks/bench.html, 1280x720, canvas in the WebView2 harness, mean of 90 frames)
High: Clear 8.4 ms, Rain 12.4 ms, Night 10.9 ms, Thunderstorm 15.1 ms, Snow 10.8 ms. Balanced: Rain 12.4 ms. Low Power: Rain 10.0 ms, Snow 7.1 ms. Battery: Rain 8.1 ms. JS heap about 10 MB in every case. Budgets by profile (tierLevel): High = everything; default = fewer clouds (x0.75), half of the trees, Milky Way and foam, no rock texture; Low Power / Battery = clouds x0.4, no Milky Way, foam, trees or texture. NOT measured: GPU time, process RAM of the wallpaper window, a 4K screen.

## Merge pass (2026-11-17, branch claude/2-6-0-app-418714 merged into 2.6.0-beta)
Checked on the merged tree: build 0 errors, 466 + 53 tests, ui-audit with a folder (108 controls, no problems), containers host / z-order diagnostics. Duplicates found and removed: (1) early start: the app's own StartupTask (per-user logon task Opsis\Start with the desktop, Settings 'start early') replaces my installer task, the installer now only removes both task names on uninstall; (2) About: my family section removed, the app's catalogue-based family section stays (the 2.6.0 notes stay). Theme: the Settings page list and the Theme page now mirror each other (GlassHost.NoteMode), 'Follow Windows' works from both. Weather / location: one implementation (DeviceLocation), no duplicate.

## 2026-11-18 (branch 2.6.0-features-desktop)
Done: rows 22, 27 (logic), 29 note, 30 (WebView2 after dispose), Δ3 (container settings only in the container menu: settings, rename (F2 / menu, Enter/Esc, empty rejected, duplicates numbered), hide, delete; page keeps global options + live list; repaint only when the painted style changed), Δ7 (docs/STARTUP-PHASES.md). Left: rows 15-17 (plugins), 9 (dense pages), UI items; everything marked NOT VERIFIED needs a real Windows desktop.
