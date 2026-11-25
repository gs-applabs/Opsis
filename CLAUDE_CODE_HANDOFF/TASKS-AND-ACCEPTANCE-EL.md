# Κριτήρια ολοκλήρωσης για Claude Code — release 2.8.0.2500

## P0 — να εκτελεστούν πρώτα

| # | Εκκρεμότητα | Ελάχιστο αποδεκτό αποτέλεσμα | Απαιτούμενη απόδειξη |
|---:|---|---|---|
| **12** | Sandbox/δικαιώματα plugins | Τεχνικά επιβεβαιωμένη απομόνωση πρόσβασης σε δίκτυο/δίσκο/ήχο, ή τεκμηριωμένος περιορισμός και σαφής μηχανισμός εμπιστοσύνης. Ένα manifest **δεν είναι sandbox** για in-process .NET DLL. Μη διαφημίζεις ψευδή enforcement. | Threat model, δοκιμή κακόβουλου test plugin σε disposable VM, deny/allow/revoke και αρχεία log |
| **17** | Αυτόματη αναίρεση μετακίνησης εικονιδίων | Snapshot **πριν** από μαζική μετακίνηση, αξιόπιστη Undo και διατήρηση θέσεων όταν αλλάζει DPI/monitor. Εάν Windows Explorer δεν παρέχει αξιόπιστο event, ορατή προϋπόθεση/εναλλακτική χωρίς ψευδή υπόσχεση αυτόματης προστασίας. | Windows Shell integration test, rollback, restart Explorer |
| **20** | Power-loss/atomic write | Σενάρια kill/crash σε κάθε στάδιο εγγραφής και ανάκτηση είτε τελευταίου έγκυρου είτε προηγούμενου backup, χωρίς corrupt profile ή αθόρυβη απώλεια. | `tests/AtomicWriteCrashHarness`, `tools/verify-atomic-powerloss.ps1`, test logs και έλεγχος για *.tmp/*.corrupt |
| **24** | 3 ταυτόχρονες πραγματικές προεπισκοπήσεις | Τρεις ανεξάρτητες σκηνές (ξημέρωμα, μεσημέρι, νύχτα) από ίδιο asset/επιλογές, σωστός ήλιος, νεφοκάλυψη, σελήνη και η κίνηση που υπόσχεται το UI· αν παραμένει στατικό, να δηλώνεται ακριβώς. | Screenshots/video από Windows WebView2 VM, screenshots σε low/high tier |
| **32** | Πανοραμικό wallpaper δύο οθονών | Επιλογή γειτονικών οθονών, διαφορετικά DPI, διαφορετικές αναλύσεις, taskbar, bezels, monitor disconnect/reconnect και πλήρης επαναφορά. Χωρίς σύνδεση μη γειτονικών οθονών. | πραγματικό multi-monitor (ή multi-display VM όπου είναι εφικτό), οπτικά τεκμήρια, device IDs |
| **40** | Ιδιωτικότητα παρουσίασης | Απόκρυψη τίτλου/καλλιτέχνη, widgets και προσωπικών δεδομένων στην επιλεγμένη οθόνη· σαφές όριο: δεν μπορεί να κρύβει όλες τις ειδοποιήσεις τρίτων apps/Windows χωρίς ξεχωριστούς μηχανισμούς. | Screen capture, δοκιμή εναλλαγής παρουσίασης, ασφάλεια default |
| **97** | Plugin compatibility / DLL 1.0.0.0 | Πραγματικό host E2E σε .NET 11 που φορτώνει τα ΝΕΑ `OpsisNewsCrawler.OpsisPlugin.dll` και `Chronikologio.OpsisPlugin.dll` με `AssemblyVersion`+`FileVersion` **1.0.0.0**. Συμβατότητα `Opsis.UI`, `Plugin.Abstractions`, Opsis Appearance. Επιπλέον Media Suite ξεχωριστή εκκίνηση. | Paths, assembly/file version, startup logs, feed `san-simera`, calendar replacement, dark/light settings, unload/upgrade |

## Release gates — απαραίτητα για χαρακτηρισμό «τελική έκδοση»

| # | Έλεγχος | Ελάχιστο αποδεκτό αποτέλεσμα |
|---:|---|---|
| **92** | .NET 10/11, WebView2, ακύρωση εξαρτήσεων | Δοκιμές σε καθαρό Windows 11 χωρίς runtime, μόνο .NET 10, με .NET 11, χωρίς WebView2 και με ακύρωση. Χωρίς ημιτελή εγκατάσταση/ακούσιο download. |
| **93** | Εγγενές/κλασικό μενού Explorer | Build με και χωρίς VC++ Build Tools, οι **πέντε** εντολές διαθέσιμες, σωστά callbacks/ασφάλεια, καθαρό uninstall. |
| **99** | Installer upgrade / side-by-side / uninstall | Κενή εγκατάσταση, upgrade υπάρχοντος branch και διατήρηση ρυθμίσεων/plugins, side-by-side με 2.4.6.1, σωστά shortcuts/registry/autostart, ασφαλής απεγκατάσταση. |

## Υποχρεωτικός οριζόντιος έλεγχος
- Προέχει **πραγματικό compiler pass** πριν από release. Μη διορθώνεις σφάλματα μεταγλώττισης απενεργοποιώντας features.
- Εκτέλεσε το σύνολο των **100 σημείων** ως smoke/regression matrix, όχι μόνο τα 10 ανοιχτά. Φτιάξε νέες δοκιμές όπου δεν υπάρχει test coverage.
- Επιδόσεις: CPU idle/active, RAM, GDI/USER, διεργασίες WebView2, SSD writes (μη συνεχείς εγγραφές), frame p95/p99, διαρροές μετά από αλλαγές wallpaper/skin, εναλλαγή monitors και κλείσιμο app.
- Ασφάλεια: ZIP traversal, υπογραφές plugin, διαδρομές/ACL, command injection, WebView2 origin/IPC, native Shell commands, νωπά advisories άμεσων DLLs/NuGet/npm, secrets scan, έλεγχο attribution/licences.
- Εμφάνιση: 14 locales, Greeklish, RTL Arabic, 100/150/200% DPI, Windows high contrast, reduced motion, πληκτρολόγιο/F1/accessibility, low/high GPU tier.
- Καιρός: φωτογραφικά ελληνικά τοπία, ουρανός, μάσκα, δυναμικές καιρικές συνθήκες, offline fallback, daylight, ακρίβεια landmarks, διπλά/πολλαπλά monitors.
- Safe recovery: profile migration, import/export, rollback of installer, crash recovery, cleanup.
- Media Suite: αυτόνομο Electron app· να μη φορτωθεί ως .NET plugin.

## Η σωστή τελική παράδοση
1. `git diff` με διορθώσεις κώδικα, changelog, τεκμηρίωση και δοκιμές.
2. Πίνακα πραγματικής κατάστασης των **100** σημείων με αρχεία και αποτελέσματα ανά σημείο.
3. Windows CI links / logs / screenshots / δοκιμές upgrade, με OS, SDK, runner, hashes.
4. Source ZIP, Windows `publish`/portable package και εγκαταστάτη Inno Setup 7 **μόνο αν δημιουργήθηκαν**.
5. SHA-256 όλων των παραγόμενων artefacts, συμβατότητα DLL/API και τελική ένδειξη ready/not ready. Μη θεωρηθεί PASS απλώς επειδή υπάρχει workflow.
