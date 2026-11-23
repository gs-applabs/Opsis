# OPSIS 2.8.0.2500 — Κατάσταση 100 αναβαθμίσεων (R7 · Session 2/4)

**Ημερομηνία:** 23 Νοεμβρίου 2026 · **Βάση:** R6 · **Νέο checkpoint:** R7-S2.

**Αποτύπωση:** **55** νέες υλοποιήσεις στον πηγαίο κώδικα, **17** προϋπάρχουσες, **8** μερικές/προς QA και **20** εκκρεμείς.

**Προειδοποίηση:** Δεν εκτελέστηκαν build, tests, Windows E2E ή installer. Η ένδειξη «υλοποιήθηκε» σημαίνει ότι προστέθηκε κώδικας, **όχι ότι έχει επαληθευτεί λειτουργικά**.

## Session 2 — αλλαγές σε 10 εκκρεμότητες

Δείτε το [αναλυτικό R7 changelog](R7-SESSION-2-OF-4-EL.md) και τα IDs: **#38, #41, #48, #57, #58, #59, #64, #66, #67, #80**.

## Πλήρης κατάσταση

| # | Σημείο | Κατάσταση | Τεκμηρίωση / κώδικας |
|---:|---|---|---|
| 1 | Δείκτης CPU ανά λειτουργία, με ξεχωριστή ένδειξη για τις διεργασίες WebView2. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Services/UiResourceAudit.cs |
| 2 | Καταγραφή του 95ου και 99ου εκατοστημορίου χρόνου καρέ ανά οθόνη. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/wallpaper/index.html · src/Widgets/WallpaperWindow.cs |
| 3 | Μέτρηση του χρόνου από το κλικ μέχρι το πρώτο ζωγραφισμένο καρέ της σελίδας. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/UI/MainWindow.cs |
| 4 | Σύγκριση private bytes μετά από επαναλαμβανόμενο άνοιγμα και κλείσιμο κάθε widget. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Services/UiResourceAudit.cs |
| 5 | Εντοπισμός αύξησης GDI και USER handles μετά από αλλαγές skin. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Services/UiResourceAudit.cs |
| 6 | Μία κοινή δειγματοληψία αισθητήρων για όλα τα widgets και τις οθόνες. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/Services/ |
| 7 | Αναστολή σχεδίασης χωριστά στην οθόνη που καλύπτεται από παιχνίδι. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/Widgets/ |
| 8 | Αυτόματη μείωση ανάλυσης του wallpaper όταν υπερβαίνεται ο στόχος χρόνου καρέ. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/wallpaper/index.html · src/Widgets/WallpaperWindow.cs |
| 9 | Πάγωμα του τελευταίου καρέ σε απομακρυσμένη συνεδρία με δυνατότητα εξαίρεσης. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/Widgets/ |
| 10 | Προβολή χρονογραμμής που συσχετίζει αιχμές CPU με αλλαγή σκηνής ή φόρτωση πρόσθετου. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Services/UiResourceAudit.cs |
| 11 | Προεπισκόπηση των αρχείων ενός πακέτου wallpaper πριν από την εισαγωγή. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/Widgets/ |
| 12 | Δήλωση δικαιωμάτων ανά πρόσθετο για δίκτυο, ήχο και πρόσβαση σε τοπικά αρχεία. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 13 | Ανακλητή συναίνεση ανά τοπικό web widget για τη λήψη στοιχείων συστήματος. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 14 | Εμφάνιση εκδότη και κατάστασης υπογραφής στο παράθυρο εγκατάστασης πρόσθετου. | ΜΕΡΙΚΟ · ΧΡΕΙΑΖΕΤΑΙ ΣΥΜΠΛΗΡΩΣΗ/QA | src/Services/PluginTrust.cs · src/Services/PluginHost.cs · src/Widgets/TrayApplicationContext.cs |
| 15 | Έλεγχος ακεραιότητας του update πριν από αντικατάσταση αρχείων. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/Services/UpdateInstallerService.cs |
| 16 | Επαναφορά προηγούμενης έκδοσης αν το νέο προφίλ αποτύχει να ανοίξει. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Services/VerifiedRollbackService.cs · src/Services/WorkspaceProfileService.cs · src/Services/UpdateInstallerService.cs · src/UI/MainWindow.cs |
| 17 | Αναίρεση μαζικής μετακίνησης εικονιδίων με αποθήκευση της αρχικής θέσης. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 18 | Προσωρινή απομόνωση συγκεκριμένου πρόσθετου μετά από διαδοχικές αστοχίες. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Services/PluginFailureQuarantine.cs · src/Services/PluginHost.cs |
| 19 | Εξαγωγή διαγνωστικών με προεπισκόπηση και απόκρυψη ονομάτων αρχείων και τοποθεσίας. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/Services/ |
| 20 | Έλεγχος επαναφοράς μετά από διακοπή ρεύματος την ώρα αποθήκευσης προφίλ. | ΜΕΡΙΚΟ · ΑΝΑΜΕΝΕΤΑΙ ΕΚΤΕΛΕΣΗ WINDOWS QA | src/Services/AtomicFile.cs · tests/AtomicWriteCrashHarness · tools/verify-atomic-powerloss.ps1 |
| 21 | Επικάλυψη της ανιχνευμένης γραμμής ουρανού στην προεπισκόπηση. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/wallpaper/sky-mask.js; src/Widgets/wallpaper/weather.js; src/UI/MainWindow.cs |
| 22 | Διόρθωση της μάσκας με πινέλο ανά φωτογραφία. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 23 | Αποθήκευση της μάσκας με αποτύπωμα εικόνας ώστε να μην εφαρμόζεται σε άλλη λήψη. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/wallpaper/sky-mask.js |
| 24 | Ταυτόχρονη προεπισκόπηση χαραυγής, μεσημεριού και νύχτας. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 25 | Επιλογή σημείου ενδιαφέροντος για την περικοπή σε κατακόρυφη οθόνη. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/WallpaperWindow.cs; src/UI/MainWindow.cs; src/Widgets/wallpaper/weather-atmosphere.js |
| 26 | Ομαλή μετάβαση στον τελευταίο διαθέσιμο καιρό όταν χαθεί η σύνδεση. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/Widgets/wallpaper/ |
| 27 | Ένδειξη ηλικίας και πηγής των δεδομένων καιρού επάνω στο widget. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/Widgets/wallpaper/ |
| 28 | Ανεξάρτητος έλεγχος έντασης για βροχή στο τζάμι και βροχή στο τοπίο. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/wallpaper/weather.js · src/Widgets/wallpaper/index.html · src/Widgets/WallpaperWindow.cs · src/UI/MainWindow.cs |
| 29 | Διαδρομή ήλιου και σελήνης με βάση ημερομηνία και γεωγραφικό πλάτος. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 30 | Έλεγχος αναγνωσιμότητας ρολογιού στην πραγματική θέση του πάνω στη φωτογραφία. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/wallpaper/index.html |
| 31 | Αναγνώριση διάταξης οθονών μετά τη σύνδεση docking station. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 32 | Πανοραμικό φόντο μόνο σε επιλεγμένο ζεύγος οθονών. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 33 | Αντιστάθμιση πλαισίων για πανοραμικές εικόνες. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 34 | Σταθερή θέση widget σε οθόνες με διαφορετικό DPI. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 35 | Προεπισκόπηση προφίλ με χρονόμετρο επαναφοράς. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/UI/MainWindow.cs · src/Services/WorkspaceProfileService.cs |
| 36 | Κανόνες προφίλ ανά συνδεδεμένο τροφοδοτικό και στάθμη μπαταρίας. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Services/AutomationService.cs · src/UI/MainWindow.cs |
| 37 | Επαναφορά παραθύρων στην αρχική οθόνη μετά από προσωρινή αποσύνδεση. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 38 | Διαφορετική πυκνότητα πληροφοριών σε κατακόρυφη δευτερεύουσα οθόνη. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Containers/ContainerWindow.cs |
| 39 | Μεταφορά προφίλ σε άλλο PC με χειροκίνητη αντιστοίχιση οθονών. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 40 | Εξαίρεση οθόνης παρουσίασης από προσωπικές ειδοποιήσεις και στοιχεία πολυμέσων. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 41 | Προεπισκόπηση ζώνης με το πληκτρολόγιο πριν από τη μετακίνηση. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/ZoneLayoutEditorForm.cs |
| 42 | Αναίρεση τελευταίας ομαδικής τακτοποίησης παραθύρων. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Services/AutoTileService.cs · src/UI/MainWindow.cs |
| 43 | Κανόνες αντιστοίχισης εφαρμογής και τύπου εγγράφου σε ζώνη. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 44 | Προσωρινή επέκταση παραθύρου σε δύο γειτονικές ζώνες. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 45 | Αλλαγή μεγέθους δύο γειτονικών ζωνών από κοινό όριο. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 46 | Διατάξεις ειδικά για κατακόρυφη εργασία κώδικα και τεκμηρίωσης. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/ZoneLayoutStore.cs · src/Widgets/ZoneLayoutEditorForm.cs |
| 47 | Εξαγωγή διάταξης σε αναγνώσιμο JSON με προεπισκόπηση εισαγωγής. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/ZoneLayoutStore.cs · src/Widgets/ZoneLayoutEditorForm.cs |
| 48 | Επαναφορά ομάδας εφαρμογών με σωστή σειρά εκκίνησης. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/DeskStripWindow.cs |
| 49 | Εξαιρέσεις για διαλόγους, παλέτες εργαλείων και προσωρινά παράθυρα. | ΜΕΡΙΚΟ · ΧΡΕΙΑΖΕΤΑΙ ΣΥΜΠΛΗΡΩΣΗ/QA | src/Services/AutoTileService.cs |
| 50 | Προεπισκόπηση των παραθύρων που θα μετακινηθούν όταν αλλάξει προφίλ. | ΜΕΡΙΚΟ · ΧΡΕΙΑΖΕΤΑΙ ΣΥΜΠΛΗΡΩΣΗ/QA | src/Services/AutoTileService.cs · src/UI/MainWindow.cs |
| 51 | Προσομοίωση κανόνα ταξινόμησης πριν μετακινηθεί αρχείο. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/UI/MainWindow.Organization.cs |
| 52 | Εντοπισμός σύγκρουσης όταν δύο κανόνες διεκδικούν το ίδιο αρχείο. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Containers/DesktopFiles.cs · src/UI/MainWindow.Organization.cs |
| 53 | Προβολή φακέλου ως container με παρακολούθηση μόνο όσο είναι ορατό. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Containers/ContainerWindow.cs |
| 54 | Φίλτρο καρτέλας κατά τύπο, ημερομηνία ή ετικέτα. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Containers/ContainerWindow.cs; src/Containers/ContainerWindow.Interaction.cs |
| 55 | Άμεση αναζήτηση σε όλα τα containers με μετάβαση στο αποτέλεσμα. | ΜΕΡΙΚΟ · ΧΡΕΙΑΖΕΤΑΙ ΣΥΜΠΛΗΡΩΣΗ/QA | src/Containers/SpotSearch.cs · src/Containers/ContainerHost.cs |
| 56 | Προσωρινή ανάπτυξη κλειστού container κατά τη μεταφορά αρχείου. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Containers/ContainerWindow.cs |
| 57 | Επισήμανση συντομεύσεων με ανύπαρκτο προορισμό και μαζική επιδιόρθωση. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Containers/BrokenShortcutTools.cs · src/Containers/ContainerWindow.cs · src/Containers/ContainerHost.cs |
| 58 | Κάδος πρόσφατων αλλαγών διάταξης με επαναφορά ανά container. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Containers/ContainerLayoutHistory.cs · src/Containers/ContainerHost.cs |
| 59 | Αντιγραφή διάταξης container σε δεύτερο workspace χωρίς αντιγραφή των αρχείων. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Containers/ContainerHost.Organization.cs · src/Containers/ContainerWindow.cs |
| 60 | Ένδειξη διαδρομής πριν από σύρσιμο μεταξύ τοπικού και δικτυακού φακέλου. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Containers/ContainerWindow.Interaction.cs |
| 61 | Ελληνική αναζήτηση χωρίς τόνους και με εναλλακτική πληκτρολόγηση Greeklish. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/UI/ |
| 62 | Εμφάνιση της συντόμευσης δίπλα σε κάθε αποτέλεσμα εντολής. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/UI/ |
| 63 | Ιστορικό εντολών με τοπική διαγραφή ανά εγγραφή. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/UI/ |
| 64 | Καρφίτσωμα αναζήτησης ως έτοιμης ενέργειας στο dock. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Containers/SpotSearch.cs · src/Widgets/DeskStripWindow.cs |
| 65 | Προεπισκόπηση εγγράφου από τον εκκινητή χωρίς εκκίνηση εφαρμογής. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/DesktopLauncher.cs · src/Widgets/QuickLookPreview.cs |
| 66 | Ομαδοποίηση πολλών παραθύρων της ίδιας εφαρμογής στο DeskStrip. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/DeskStripWindow.cs |
| 67 | Προσαρμογή σειράς εικονιδίων χωριστά για κάθε οθόνη. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/DeskStripWindow.cs |
| 68 | Ένδειξη ενεργού workspace δίπλα στο dock. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/DeskStripWindow.cs; src/UI/MainWindow.cs |
| 69 | Κατάλογος συγκρούσεων καθολικών συντομεύσεων με δοκιμή νέου συνδυασμού. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/HotkeyManager.cs · src/Widgets/HotkeyDiagnosticDialog.cs · src/Widgets/DesktopLauncher.cs |
| 70 | Εναλλακτική συμπαγής προβολή εντολών για μικρή ανάλυση. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/UI/MainWindow.cs |
| 71 | Κοινό πλέγμα στοίχισης για ρολόι, καιρό και system monitor. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/WidgetEngine.cs · src/UI/MainWindow.DeskZonesSnapping.cs · src/UI/MainWindow.cs |
| 72 | Προσωρινό ξεκλείδωμα όλων των widgets με αντίστροφη μέτρηση. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/WidgetEngine.cs · src/UI/MainWindow.cs |
| 73 | Προσαρμογή συχνότητας ανανέωσης ανά widget. | ΜΕΡΙΚΟ · ΧΡΕΙΑΖΕΤΑΙ ΣΥΜΠΛΗΡΩΣΗ/QA | src/Widgets/WidgetEngine.cs |
| 74 | Σήμανση μη διαθέσιμου αισθητήρα αντί για πλασματική μηδενική τιμή. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/Widgets/ |
| 75 | Επιλογή συγκεκριμένης GPU σε σύστημα με ενσωματωμένα και διακριτά γραφικά. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/Widgets/ |
| 76 | Απεικόνιση τάσης θερμοκρασίας σε κυλιόμενο διάστημα. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/WidgetEngine.cs |
| 77 | Σταθερό αναγνωριστικό κάρτας δικτύου όταν αλλάξει το όνομα σύνδεσης. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Services/AdvancedSystemMonitorService.cs |
| 78 | Επιλογή συσκευής εισόδου του visualizer ξεχωριστά από την προεπιλεγμένη έξοδο. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 79 | Προσωρινή απόκρυψη τίτλου τραγουδιού σε κοινή χρήση οθόνης. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Music/MusicOverlaySettings.cs; src/Music/MusicOverlayPage.cs; src/Music/WallpaperMusicSurface.cs |
| 80 | Ομαλή επιστροφή του visualizer μετά από αποσύνδεση Bluetooth. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Services/SharedLoopbackCapture.cs · src/Services/AudioSpectrumService.cs |
| 81 | Δείγμα κουμπιού σε κανονική κατάσταση, hover, πάτημα, εστίαση και απενεργοποίηση στον επεξεργαστή θέματος. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/UI/MainWindow.Themes.cs |
| 82 | Προεπισκόπηση θέματος με τις μεγαλύτερες μεταφρασμένες λεζάντες. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/UI/MainWindow.Themes.cs |
| 83 | Ελάχιστη αντίθεση κειμένου κατά την επιλογή προσαρμοσμένου χρώματος. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/UI/ |
| 84 | Ορατή σειρά πλοήγησης με Tab στον επεξεργαστή διάταξης. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/Widgets/ZoneLayoutEditorForm.cs |
| 85 | Αναγγελία αλλαγής ενεργού προφίλ μέσω αναγνώστη οθόνης. | ΜΕΡΙΚΟ · ΧΡΕΙΑΖΕΤΑΙ ΣΥΜΠΛΗΡΩΣΗ/QA | src/Services/WorkspaceProfileService.cs · src/UI/MainWindow.cs |
| 86 | Εφαρμογή της ρύθμισης μειωμένης κίνησης και στα web wallpapers. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/Widgets/wallpaper/ |
| 87 | Ξεχωριστή κλίμακα γραμματοσειράς για το dock και τις κάρτες. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/UI/UiTheme.cs · src/Widgets/DeskStripWindow.cs · src/UI/MainWindow.cs |
| 88 | Αυτόματη επιλογή accent από το φόντο με προεπισκόπηση πριν από εφαρμογή. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/UI/MainWindow.Appearance.cs |
| 89 | Προεπισκόπηση από δεξιά προς τα αριστερά για αραβική μετάφραση. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/UI/MainWindow.Themes.cs |
| 90 | Επεξήγηση του λόγου που ένα κουμπί είναι ανενεργό μέσα στην ίδια σελίδα. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/UI/MainWindow.cs · src/UI/MainWindow.Organization.cs |
| 91 | Ένας πίνακας έκδοσης για app, installer, βοήθεια και ενημερώσεις. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | Version.props · Opsis.Studio.csproj · installer.iss · src/Services/AppVersion.cs · src/Services/UpdateCheckService.cs · tools/emit-release-identity.ps1 |
| 92 | Δοκιμή της υπάρχουσας σελίδας εξαρτήσεων σε Windows με μόνο .NET 10, με .NET 11, χωρίς WebView2 και με ακύρωση λήψης. Και τα δύο branches έχουν έλεγχο runtime. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 93 | Επαλήθευση του νέου εναλλακτικού κλασικού μενού σε Windows χωρίς Visual C++ Build Tools. Δοκιμή και των πέντε εντολών και της μετάβασης στο εγγενές μενού μετά από πλήρες build. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 94 | Σύντομη προβολή νέων λειτουργιών μόνο στην πρώτη εκκίνηση μετά από αναβάθμιση. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/UI/ |
| 95 | Θεματική βοήθεια από κάθε κουμπί με F1. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/UI/MainWindow.cs · src/UI/HelpForm.cs |
| 96 | Αναζήτηση στη βοήθεια με συνώνυμα των ελληνικών ονομασιών. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | src/UI/HelpForm.cs |
| 97 | Κατάλογος συμβατότητας επεκτάσεων ανά branch. Πρώτα News Crawler και Appearance, που αναφέρονται στον παλιό installer, με έκδοση API και διαδρομή εγκατάστασης για κάθε πακέτο. | ΜΕΡΙΚΟ · ΑΝΑΜΕΝΕΤΑΙ ΕΚΤΕΛΕΣΗ WINDOWS QA | src/Services/PluginCompatibilityCatalog.cs · src/Services/PluginHost.cs · suite/plugin-compatibility.json |
| 98 | Εξαγωγή αναφοράς build με ακριβές στάδιο αποτυχίας και έκδοση εργαλείων. | ΥΛΟΠΟΙΗΘΗΚΕ ΣΤΟΝ SOURCE · ΧΩΡΙΣ BUILD | tools/write-build-report.ps1 · .github/workflows/windows-audit.yml |
| 99 | Δύο δοκιμές με αποθηκευμένες ρυθμίσεις: αναβάθμιση εντός του τρέχοντος branch και χωριστή εγκατάσταση δίπλα στην 2.4.6.1. Έλεγχος καταλόγου, autostart, απεγκατάστασης και εκκίνησης όσο τρέχει το άλλο branch. | ΕΚΚΡΕΜΕΙ ΥΛΟΠΟΙΗΣΗ | — |
| 100 | Προαιρετική καταγραφή συνεδρίας διάγνωσης με αυτόματο τερματισμό και τοπικό αρχείο. | ΥΠΗΡΧΕ ΣΤΗ ΒΑΣΗ · ΑΝΕΠΙΒΕΒΑΙΩΤΟ | src/Services/ |

## 20 εκκρεμή προς υλοποίηση

#12, #13, #17, #22, #24, #29, #31, #32, #33, #34, #37, #39, #40, #43, #44, #45, #78, #92, #93, #99.

## Επόμενα βήματα

Ολοκλήρωση των 20 εκκρεμών με τα Sessions 3/4 και 4/4. Μετά τις αλλαγές κώδικα: έλεγχοι όλων των 100 σημείων, των 8 μερικών/QA, της έκδοσης DLL plugins 1.0.0.0, του Crawler, Χρονολογίου, Media Suite, ασφάλειας, ανάκτησης και installer.
