# APP-IMPROVEMENTS-STATUS — ζητήθηκε → κατάσταση (εφαρμογή Opsis, χωρίς containers/Glass core)

Κλάδος `2.6.0-app` (από `2.6.0-beta`), τοπικά commits, χωρίς push/tag/release. Έκδοση εμφάνισης: «2.6.0 beta».
Πηγές: master plan, `PLAN-2.6.0-INTAKE.md`, `PLAN-2.6.0-PROGRESS.md` (λίστα στο τέλος), TASKS-MASTER §22-A, §26–§30.
Σύμβολα: ✓ έγινε · ◐ μερικό · ☐ μένει (θα γίνει) · ⛔ χρειάζεται χρήστη/απόφαση/δοκιμή σε πραγματικά Windows · ✗ εκτός ορίων (containers/Glass core, άλλη συνεδρία).

| Plan § | Αίτημα | Κατάσταση | Σημείωση |
|---|---|---|---|
| 2.1.1 | Distinctive εικονίδιο καρτέλας plugin | ✓ | `PluginIconChooser`· ⛔ οπτικός έλεγχος |
| 2.1.1 | Διαγραφή «προεπιλεγμένη εμφάνιση plugins» | ✓ | |
| 2.1.1 | Ενημερώσεις plugins μόνο από το κουμπί Opsis | ⛔ | κάθε plugin έχει δικό του updater (PluginHost.cs:424)· η ενοποίηση θέλει αλλαγή στα plugins/contract (άλλα repos σε αναμονή §30) |
| 2.1.1 | Κοινές ρυθμίσεις μόνο στο Opsis | ⛔ | φάση P (αλλαγή contract σε 3 repos) |
| 2.1.1 | DeskStrip σε δική του καρτέλα | ✓ | |
| 2.1.2 | Εκκίνηση στο tray | ✓ | `--background` |
| 2.1.2 | Ενοποίηση Αυτοματισμών/Προφίλ + gaming mode Steam | ✓ | |
| 2.1.2 | Κουμπιά «Επαναφορά» κομμένα | ✓ | ⛔ ζωντανό UI |
| 2.1.3 | Εξατομίκευση Plus / theme packs duplicate | ⛔ | χρειάζεται το plugin Appearance για σύγκριση (άλλο repo, σε αναμονή §30) |
| 2.1.8 | Αποσυμφόρηση ρυθμίσεων: Ρυθμίσεις, Θέμα | ✓ | κάρτες, Reset ανά κάρτα, Advanced, αναζήτηση |
| 2.1.8 | Αποσυμφόρηση: Wallpaper, Εξατομίκευση, Audio, DeskZones, Widgets | ☐ | σελίδα-σελίδα με screenshots `--ui-audit` |
| 2.1.8 | UiScale, WindowMemory | ◐ | κλίμακα μόνο στις κοινές γραμματοσειρές |
| 2.1.8 | Glass dropdown + χρώματα σε ξεχωριστή ενότητα | ✓ | |
| 2.1.9 | Πάντα ορατή βιβλιοθήκη, playlist ενεργά/ανενεργά | ✓ | 2.4.1 |
| 2.1.9 | Παύση/Προηγούμενο/Επόμενο, μικρά κουμπιά στην ίδια ευθεία | ✓ | |
| 2.1.9 | Drag & drop σειράς | ✓ | ⛔ δοκιμή με ποντίκι |
| 2.1.9 | RAM 16 βίντεο ~850 MB αντί ~3 GB | ⛔ | ο κώδικας έχει 2 στοιχεία `<video>`· χρειάζεται μέτρηση (`tools/resource-logger.ps1`) |
| 2.1.4 | Lockscreen GIF (LockscreenGif MIT) | ⛔ | Άδεια ελεγμένη: Leapward-Koex/LockscreenGif = MIT (επιτρέπεται αντιγραφή με attribution στο THIRD_PARTY_NOTICES). Τεχνική: αντικαθιστά τα αρχεία της cache εικόνας οθόνης κλειδώματος και απαιτεί απενεργοποίηση ενός χαρακτηριστικού των Windows (elevation + πιθανή επανεκκίνηση) και ο ίδιος ο συγγραφέας λέει ότι η επιτυχία δεν επαληθεύει αναπαραγωγή. Αλλάζει ρυθμίσεις συστήματος → δεν υλοποιείται χωρίς δοκιμή σε πραγματικά Windows 11 και ρητή έγκριση χρήστη (απόφαση: ναι/όχι) |
| 2.1.4 | «Ταίριασμα lockscreen» χωρίς καιρό/ώρα | ☐ | υπάρχει «Match lock screen to wallpaper» |
| 2.1.4 | Αφαίρεση φάσματος ήχου | ✓ | |
| 2.1.6 | Music overlay GSMTC (clean-room) | ✓ | ⛔ δοκιμή με πραγματικό player |
| 2.1.5/2.1.5β | Wallpapers landmarks/ποιότητα, μοντέλα | ◐ | Ribbons φθηνότερο· landmarks/3D ⛔ άδειες μοντέλων |
| 0Β.3 | Δεξί κλικ επιφάνειας | ◐ | opt-in classic verbs· νέο μενού Win11 ⛔ (sparse package/υπογραφή) |
| 0Β.5 | Ακριβής καιρός + opt-in τοποθεσία | ⛔ | πίνακας παρόχων `docs/WEATHER_PROVIDERS.md` — περιμένει απόφαση χρήστη |
| 0Β.2 | Hotkeys, pop-up επανεκκίνησης | ✓ | |
| 0Β.4/0Β.6 | Glass dropdown, status bar | ✓ | |
| §9 | DeskStrip PollTick εκτός UI thread | ✓ | |
| §9 | Cache `AppSettings.Load` (179 σημεία) | ☐ | οι callers αλλάζουν το αντικείμενο· ασφαλές μόνο με clone ή στοχευμένα στα hot σημεία |
| §9 | Weather renderer buffers | ✓ | στατικά brushes snow/bolt αντί νέου ανά frame (μικρό κέρδος· build OK)· ⛔ δεν μετρήθηκε |
| §22-A.1 | Opsis ξεκινά πρώτο στην εκκίνηση | ☐ | |
| §22-A.5 | Explorer restart → μαύρη οθόνη | ◐ | `ExplorerRestartWatcher` υπάρχει· ⛔ πραγματικό restart |
| 2.1.7/containers | Glass core, DeskContainers native, rubber-band | ✗ | άλλη συνεδρία |
| R | Soak/baseline | ⛔ | τρέχει ο χρήστης |

Ενημερώνεται ανά ενότητα μαζί με τα commits.
