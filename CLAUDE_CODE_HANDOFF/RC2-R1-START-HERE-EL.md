# OPSIS 3.0.0.3200 RC2-R1 — Παράδοση Claude Code

## Εκκίνηση και αμετάβλητες βάσεις
- Παγωμένη αποτυχημένη RC2: `0e6264ca7c55f8313381c626f3c5407137f0710a`. Μην τη μετακινήσεις.
- Νεότερος υποψήφιος: branch `release/3.0.0.3200-rc2-r1`. Αναγνώρισε ακριβές HEAD με git rev-parse HEAD. Ο ενιαίος κώδικας ανάπτυξης είναι integration/opsis-unified-r102.
- .NET 11, Windows x64, Inno Setup (όχι NSIS), offline-first. Μην απαιτήσεις Windhawk/Rainmeter/StartAllBack. Διατήρησε 205 παλιές ρυθμίσεις, 21 πρόσθετες, ABI 1.0.0.0. ReleaseBlocked=true μέχρι πραγματική πιστοποίηση.

## Αποτελέσματα RC2 και διορθώσεις RC2-R1
RC2: 10 workflows, 18 jobs, 4 PASS / 14 FAIL. JS 57/59, 7 C# errors σε 5 αρχεία, 8 warnings. Source handoff, standalone Media Suite, isolated process-crash recovery, NuGet audit passed. Installer/native/UI/host plugin/soak blocked. RC2-R1: RC2-F01, RC2-F02, RC2-F03, RC2-F04, RC2-F05, RC2-F06, RC2-F09, RC2-F10, RC2-F11, RC2-F12, RC2-F13, RC2-F14, RC2-F15, RC2-F16 διορθώθηκαν στον πηγαίο κώδικα, απαιτείται νέο CI. F07/F08/F17 δεν θεωρούνται κλειστά. Δεν έχει επιβεβαιωθεί μεταγλώττιση ή εγκαταστάτης αυτού του νέου SHA.

## Κανόνας πραγματικής λειτουργίας
Το κουμπί ή η ρύθμιση που υπάρχει δεν είναι υλοποίηση από μόνο του. Πριν από ανάπτυξη διασταύρωσε method body, UI caller, persistence, runtime owner και τυχόν Core DLL/συγγενές repo· μην διπλασιάζεις. Για κάθε καρτέλα: πραγματικό sidebar click, σωστή ενεργή σελίδα, κανένα error fallback, visible/usable controls, scroll και collapse/expand, screenshot review. Για κάθε χειριστήριο: πριν → ενέργεια μέσω UI → ανεξάρτητο readback του πραγματικού αποτελέσματος (αρχεία/registry/window geometry/service/renderer) → επανεκκίνηση όταν απαιτείται → restore. Καταχώρισε PASS μόνο αν όλα τα αντίστοιχα κριτήρια αποδεικνύονται. Για εξωτερική εφαρμογή/δικτύωση/admin/διαγραφή/τερματισμό χρησιμοποίησε disposable VM και ασφαλή fixture. Μην πατήσεις τυφλά όλα τα κουμπιά. Τα υπόλοιπα είναι BLOCKED/SKIPPED/NOT TESTED, όχι PASS.

## Πραγματικές καρτέλες και acceptance matrix
- **Dashboard**: Open via sidebar, actual content identity, cards/navigation; verify feature launch and status reflect service state.
- **Theme**: Change palette, contrast, mode; inspect actual app theme and reopen persisted settings; revert.
- **Personalization**: Open Start Studio/Explorer Studio/Taskbar Designer; verify start style, taskbar registry values and restored state; native vs companion explicitly.
- **Wallpaper**: Select Gallery category, static/video/weather; observe actual desktop and scene, switch and restore; offline, RDP, GPU; no claim from screenshot alone.
- **Widgets**: Request System Monitor by button, verify visible widget and live samples, close/reopen, ensure no redundant settings switch; add/remove each widget, persist custom layouts.
- **DeskZones**: Create/modify/apply zone to disposable test window; verify bounds and undo; keyboard preview is missing.
- **DeskContainers**: Create/rename/move/lock/tab/portal/rules/drag file in isolated fixture; verify actual filesystem and restore; test legacy migration.
- **DeskStrip**: Pin/unpin/reorder, edge, auto-hide, hover, actual window geometry and conflict with replacement taskbar.
- **DeskMusicVisualizer**: Toggle when safe, play test audio; verify real visualization, metadata privacy and session suspend/restart; no microphone without consent.
- **Performance**: Select tier, read service state and observed process metrics; verify diagnostics, cancel/export and no retained workers.
- **Automation**: Save profile, create safe rule, trigger in isolated data, verify effect and restoration; Profiles alias is intentional.
- **Settings**: Apply actual opacity/native alpha and persisted settings; toggle flags; reopen and restart; verify localization and themes.
- **About**: Open Help/History, navigate tabs and validate content; versions, license, installer identity.
- **AudioEnhancement**: Conditional Media Suite installation: present only when detected; verify DSP using test audio, absent case explicit.
- **plugin:***: Conditional on installed approved plugin; actual route/load with pinned provider DLL, check feeds/widget and unload/disable.
- **Subdialogs and risky commands**: Every visible enabled control has a stable ID and outcome/readback. Modal/destructive/network/admin controls isolated and explicitly approved; skipped or absent prerequisite never PASS.

## Διαδικασία δοκιμών
- Windows 2022/2025 GitHub runners, μετά πραγματικά Windows 11 24H2/25H2, δεύτερος VM provider, μικτό DPI/οθόνες/φυσική GPU. CI server δεν ισοδυναμεί με Win11.
- `$env:OPSIS_RC_VALIDATION='RC2'; python tests/integration/check_baseline_and_release.py` (μόνο RC validation; production gate παραμένει fail-closed).
- `node --test`; `dotnet build Opsis.csproj -c Release`; `dotnet run --project tests/Opsis.ExperienceSmoke/Opsis.ExperienceSmoke.csproj -c Release`; ShellSmoke, RcUiSmoke με φάκελο evidence, SignatureExplorerSmoke, WebViewSmoke, published --run-selftest, atomic crash harness, NuGet transitive audit, European production assets gate. CI 10 workflows. Διατήρησε logs, SHA, job IDs και screenshots. Δοκιμή `--ui-audit` μόνο με OPSIS_E2E=1 και προσωρινό OPSIS_APPDATA σε disposable VM.
- Επαλήθευσε πραγματική απόκριση, CPU/RAM/GDI/VRAM, 1 ώρα/24 ώρες, επαναλαμβανόμενη ενεργοποίηση/απενεργοποίηση, cold/warm starts, native fallback και exit. Μη δημιουργήσεις δεύτερο ιδιοκτήτη λειτουργίας (DeskStrip dock, PluginHost, shared capture, WidgetHost).
- Installer: publish win-x64, Inno Setup 7, clean/reinstall/uninstall/upgrade από πραγματικά SHA-verified παλαιότερα installers, dependencies, addon, rollback, SHA256, έλεγχος έκδοσης. Μην δημοσιεύσεις μη πιστοποιημένο setup. Διαθέσιμα artifacts μόνο μετά από επιτυχή jobs.

## Όλες οι εκκρεμότητες (canonical)
Τα `docs/SOURCE-CONSOLIDATION.md` και `.json` περιέχουν όλες τις αρχικές 100 απαιτήσεις, G01–G23, παλαιότερα B01–B14, 73 DEV, 8 ASSET, 19 QA και 17 RC2 findings (F17 παραπομπή, όχι διπλή εργασία). Το JSON διατηρεί τα πραγματικά αποτελέσματα RC2 και νέες διορθώσεις. Μην χρησιμοποιείς παλιά roadmap ως μοναδική απόδειξη. Προτεραιότητα: compile/JS, πραγματικές UI αποτυχίες, μερικές λειτουργίες, υπόλοιπες ανάπτυξης, assets, τελική πιστοποίηση.

### Γνωστά επιπλέον σημεία για επιθεώρηση
- WorkspaceProfileService ReadValidated backup fallback: Delete πρέπει να διασφαλίζει ότι δεν αναβιώνει διαγραμμένο προφίλ από .bak, προστασία καλού backup όταν primary είναι semantically invalid, .tmp πρώτης εγγραφής, preview/apply snapshot identity. Μη χαθούν ρυθμίσεις.
- ContainerWindow UsesRows ήδη περιέχει portrait single-column density· έλεγξε αν καλύπτει #38 πλήρως αντί να διπλοϋλοποιήσεις.
- CUR/ANI: parser structural fixture, native decode/actual registry/restore ανεπιβεβαίωτα.
- Εγκεκριμένα logo/Intro WebM/ήχος/Steel Oval Rails και European release gate: μην δημιουργήσεις υποκατάστατα. 145 ελληνικές εικόνες υπάρχουν ήδη, χρειάζονται οπτική ποιότητα όχι μαζική αναδημιουργία. Πακέτο 112 wallpapers ήδη δημοσιευμένο, έλεγξε payload.
- 73 DEV: αξιολόγησε πρώτα πραγματική υλοποίηση και tests. Native Start/Explorer/notification/quick settings, SDK2 isolation, true minimal Containers-only, custom widget restore, Glass/HDR, online gallery παραμένουν μερικά/ανοικτά. Ασφάλεια και απομόνωση plugins όχι ισχυρισμοί χωρίς OS enforcement.

## Παραδοτέα Claude Code
Ενημέρωσε canonical branch και μητρώο με ID, source evidence, test, result, artifact SHA, blocker, fix/retest. Διατήρησε ένα ενιαίο source ZIP, installer και addon (χωρίς παλαιά προσωρινά διπλότυπα). Ενημέρωσε Help/History/MIT και μην κάνεις merge/release main πριν περάσουν όλα τα gates. Αρχεία: `RC2-R1-UI-ACCEPTANCE.json`, `RC2-R1-OPEN-WORK.json`, canonical docs. Καμία απαίτηση δεν διαγράφεται χωρίς απόδειξη.
