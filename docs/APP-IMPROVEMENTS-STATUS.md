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
| 2.1.3 | Εξατομίκευση Plus / theme packs duplicate | ◐ | Αντικαταστάθηκε από την ενσωμάτωση του Opsis Appearance (σελίδα «Εμφάνιση Windows»): βλ. `docs/APPEARANCE-MIGRATION-LEDGER.md`. Θέματα `.opsistheme` + λίστα `.theme` σε μία ενότητα· εκκρεμούν όσα έχει το ledger §5 |
| 2.1.8 | Αποσυμφόρηση ρυθμίσεων: Ρυθμίσεις, Θέμα | ✓ | κάρτες, Reset ανά κάρτα, Advanced, αναζήτηση |
| 2.1.8 | Αποσυμφόρηση: Wallpaper, Εξατομίκευση, Audio, DeskZones, Widgets | ◐ | Wallpaper: οι ενότητες Καιρός και Πληροφορίες είναι πλέον κάρτες (Personalization είχε ήδη). Τα υπόλοιπα ☐. Το `--ui-audit` δείχνει μόνο το πάνω μέρος κάθε σελίδας, άρα οι κάρτες του κάτω μέρους ⛔ δεν ελέγχθηκαν οπτικά |
| 2.1.8 | UiScale, WindowMemory | ◐ | κλίμακα μόνο στις κοινές γραμματοσειρές |
| 2.1.8 | Glass dropdown + χρώματα σε ξεχωριστή ενότητα | ✓ | |
| 2.1.9 | Πάντα ορατή βιβλιοθήκη, playlist ενεργά/ανενεργά | ✓ | 2.4.1 |
| 2.1.9 | Παύση/Προηγούμενο/Επόμενο, μικρά κουμπιά στην ίδια ευθεία | ✓ | |
| 2.1.9 | Drag & drop σειράς | ✓ | ⛔ δοκιμή με ποντίκι |
| 2.1.9 | RAM 16 βίντεο ~850 MB αντί ~3 GB | ⛔ | ο κώδικας έχει 2 στοιχεία `<video>`· χρειάζεται μέτρηση (`tools/resource-logger.ps1`) |
| 2.1.4 | Lockscreen GIF (LockscreenGif MIT) | ⛔ | Άδεια ελεγμένη: Leapward-Koex/LockscreenGif = MIT (επιτρέπεται αντιγραφή με attribution στο THIRD_PARTY_NOTICES). Τεχνική: αντικαθιστά τα αρχεία της cache εικόνας οθόνης κλειδώματος και απαιτεί απενεργοποίηση ενός χαρακτηριστικού των Windows (elevation + πιθανή επανεκκίνηση) και ο ίδιος ο συγγραφέας λέει ότι η επιτυχία δεν επαληθεύει αναπαραγωγή. Αλλάζει ρυθμίσεις συστήματος → δεν υλοποιείται χωρίς δοκιμή σε πραγματικά Windows 11 και ρητή έγκριση χρήστη (απόφαση: ναι/όχι) |
| 2.1.4 | «Ταίριασμα lockscreen» χωρίς καιρό/ώρα | ✓ | ήδη υλοποιημένο: η λήψη κρύβει το overlay ώρας/καιρού (`CapturePngAsync`, `#wxInfo`)· ⛔ δεν ελέγχθηκε με πραγματική οθόνη κλειδώματος |
| 2.1.4 | Αφαίρεση φάσματος ήχου | ✓ | |
| 2.1.6 | Music overlay GSMTC (clean-room) | ✓ | ⛔ δοκιμή με πραγματικό player |
| 2.1.5/2.1.5β | Wallpapers landmarks/ποιότητα, μοντέλα | ◐ | Ribbons φθηνότερο· landmarks/3D ⛔ άδειες μοντέλων |
| 0Β.3 | Δεξί κλικ επιφάνειας | ◐ | opt-in classic verbs· νέο μενού Win11 ⛔ (sparse package/υπογραφή) |
| 0Β.5 | Ακριβής καιρός + opt-in τοποθεσία | ✓ | NWS(ΗΠΑ)→Open-Meteo best_match→MET Norway, fallback, ήλιος/σελήνη, πηγή+ώρα στις ρυθμίσεις, opt-in τοποθεσία, 14 γλώσσες, 52 tests· `docs/WEATHER-PROVIDERS.md`, backport `docs/WEATHER-BACKPORT.md`· ⛔ Windows Location και εμφάνιση στο wallpaper δεν δοκιμάστηκαν |
| 0Β.2 | Hotkeys, pop-up επανεκκίνησης | ✓ | |
| 0Β.4/0Β.6 | Glass dropdown, status bar | ✓ | |
| §9 | DeskStrip PollTick εκτός UI thread | ✓ | |
| §9 | Cache `AppSettings.Load` (179 σημεία) | ✓ στοχευμένα | νέο `AppSettings.Peek()` (μόνο-ανάγνωση snapshot 1,5 s, ακυρώνεται στο Save) στα hot σημεία: AppActivity.Poll, WidgetTheme, GlassService· τα υπόλοιπα 170 σημεία μένουν με `Load()` (κάνουν αλλαγές) |
| §9 | Weather renderer buffers | ✓ | στατικά brushes snow/bolt αντί νέου ανά frame (μικρό κέρδος· build OK)· ⛔ δεν μετρήθηκε |
| §22-A.1 | Opsis ξεκινά πρώτο στην εκκίνηση | ☐ | |
| §22-A.5 | Explorer restart → μαύρη οθόνη | ◐ | `ExplorerRestartWatcher` υπάρχει· ⛔ πραγματικό restart |
| 2.1.7/containers | Glass core, DeskContainers native, rubber-band | ✗ | άλλη συνεδρία |
| R | Soak/baseline | ⛔ | τρέχει ο χρήστης |

Ενημερώνεται ανά ενότητα μαζί με τα commits.

## Ενημέρωση 2026-11-12
- ✓ Installer σε σκούρο θέμα όταν τα Windows είναι σκούρα (`WizardStyle=modern dynamic` + dark εικόνες)· ⛔ δεν έτρεξε ο wizard σε πραγματικό σύστημα (μόνο compile).
- ✓ Ενσωμάτωση Appearance στην Εξατομίκευση (ίδια καρτέλα, κάρτες, «Για προχωρημένους»): `docs/APPEARANCE-MIGRATION-LEDGER.md` (§5 κατάσταση, §6 διπλότυπα/συγχωνεύσεις).
- ✓ Οικογένεια χωρίς καταργημένες εφαρμογές (catalogue, README, installer, μεταφράσεις, VersionNotes)· Waveframe → Opsis Media Suite.
- ✓ Καιρός: `docs/WEATHER-PROVIDERS.md` + `docs/WEATHER-BACKPORT.md`· Crawler: ίδιο σύστημα (MET Norway δεύτερη πηγή + opt-in τοποθεσία), commit 507eca6/e06cdc5 στο repo του.
- ✓ Εξατομίκευση: cache λίστας εικόνων slideshow, δείγμα χρώματος wallpaper με όριο μεγέθους και μνήμη, εγγραφή tray «Εναλλαγή Φωτεινό / Σκούρο» (14 γλώσσες).

## ΕΤΟΙΜΟ ΓΙΑ MERGE — κλάδος claude/2-6-0-app-418714 (ci-2.6.0-app), κώδικας στο commit c389cb4
(μετά από αυτό μόνο αυτή η ενότητα docs). Build 0 σφάλματα, 424 tests Opsis passed, UI audit + installer-e2e + plugin-e2e + security-scan πέρασαν στο GitHub runner (πριν τις τελευταίες αλλαγές· ξαναδοκιμάζονται παρακάτω).

**Έγιναν:** Ενσωμάτωση εμφάνισης Windows στην Εξατομίκευση (ίδια καρτέλα, κάρτες, «Για προχωρημένους», συγχώνευση θεμάτων/διαφάνειας, ledger `docs/APPEARANCE-MIGRATION-LEDGER.md`, εισαγωγέας μόνο-ανάγνωσης, installer ανιχνεύει/απεγκαθιστά το παλιό, ουδέτερα κείμενα) · οικογένεια/catalogue/README/installer/μεταφράσεις χωρίς καταργημένες εφαρμογές · σκούρος installer · καιρός με παρόχους NWS/Open-Meteo/MET + ήλιος/σελήνη + opt-in τοποθεσία + όνομα περιοχής (offline GeoNames, προαιρετικό OSM) + πηγή στις ρυθμίσεις · music overlay GSMTC · δεξί κλικ επιφάνειας (opt-in) · βιβλιοθήκη βίντεο (κουμπιά, drag&drop) · perf §9 · εκκίνηση πρώτα (opt-in εργασία Task Scheduler) · τίτλος παραθύρου «Opsis 2.6.0 beta» · contract plugins με rich feeds · πλευρικό μενού με όλα τα στοιχεία · διόρθωση σφάλματος DeskContainers · tray Φωτεινό/Σκούρο.
**Lockscreen GIF:** ΔΕΝ υλοποιήθηκε εγγραφή στο cache συστήματος (αλλάζει ACL/αρχεία συστήματος και ανεπίσημο χαρακτηριστικό Windows· δεν δοκιμάζεται χωρίς πραγματικό lock screen). Υπάρχει σαφές κείμενο στην Εξατομίκευση και κουμπί προς το έργο LockscreenGif (MIT).
**2.4.6.2:** `docs/weather-backport/` (patches + βήματα στο `docs/WEATHER-BACKPORT.md`).

**ΔΕΝ δοκιμάστηκε (χρειάζεται πραγματικό Windows 11):** εφέ παραθύρων/taskbar look/στρογγυλό taskbar · Windows Location και δικαιώματα · αίτημα Nominatim (μόνο mock)· εκκίνηση-πρώτα (η εργασία δεν δημιουργήθηκε ποτέ, μόνο XML σε test) · wizard/εγκατάσταση/απεγκατάσταση του installer (μόνο compile + silent e2e στον runner) · πλευρικό μενού σε μικρή οθόνη (μόνο λογική) · 150/200 % DPI · μνήμη 16 βίντεο · drag&drop με ποντίκι · μέτρηση πόρων.
**Εκτός πεδίου / εκκρεμή:** κάρτες στις υπόλοιπες πυκνές σελίδες (Audio, DeskZones, Widgets, υπόλοιπο Wallpaper) · ενοποίηση ενημερώσεων plugins (θέλει contract) · Lockscreen GIF εγγραφή cache.

## ΕΤΟΙΜΟ ΓΙΑ MERGE (τελικό) — κλάδος claude/2-6-0-app-418714 (και ci-2.6.0-app), κώδικας στο commit 95f4bb2
Συγχρονισμένο με 2.6.0-beta (merge χωρίς συγκρούσεις, δεν αγγίχτηκε ο πυρήνας containers/glass). Build 0 σφάλματα· 438 tests Opsis passed (3 συνεχόμενα περάσματα· δύο αποσπασματικά tests του HardwareProfile εξαρτώνται από κοινό temp settings όταν τρέχουν παράλληλα και ξαναπερνούν)· working tree καθαρό.

**Προστέθηκαν σε αυτό το πέρασμα:** ΜΙΑ κοινή τοποθεσία καιρού (wallpaper + widget· το widget διαβάζει την ίδια τοποθεσία, τους ίδιους παρόχους και την ίδια cache· παλιά τοποθεσία widget υιοθετείται μία φορά) · εκκίνηση πριν τις άλλες εφαρμογές ενεργοποιείται ΜΙΑ φορά μετά την πρώτη εκτέλεση όταν η Εκκίνηση με τα Windows είναι ενεργή (ρύθμιση στις Ρυθμίσεις) · «Σχετικά»: η οικογένεια Opsis (μόνο το catalogue) + σημειώσεις 2.6.0 (en/el) στο ιστορικό εκδόσεων.
**Lockscreen GIF (ζωντανή εγγραφή): ΔΕΝ υλοποιήθηκε** — απαιτεί αλλαγή ACL στον φάκελο συστήματος `SystemData\<SID>\ReadOnly` και απενεργοποίηση ανεπίσημου χαρακτηριστικού Windows (RtlSetFeatureConfigurations) με αναβάθμιση δικαιωμάτων· δεν μπορεί να δοκιμαστεί εδώ χωρίς να κλειδωθεί ο πραγματικός υπολογιστής και ένα λάθος αφήνει χαλασμένη οθόνη κλειδώματος. Υπάρχει εξηγητικό κείμενο + σύνδεσμος προς το LockscreenGif (MIT). Αν ο χρήστης το θέλει ρητά, να γίνει ως ξεχωριστό έργο με δοκιμή σε VM.
**ΔΕΝ δοκιμάστηκε σε πραγματικά Windows:** όσα αναφέρονται στην προηγούμενη ενότητα + η εργασία εκκίνησης στο Task Scheduler (δεν δημιουργήθηκε ποτέ, μόνο XML σε test) + widget καιρού με τη νέα κοινή τοποθεσία (μόνο build).

