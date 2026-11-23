# OPSIS Desktop Customization — αναφορά ενσωμάτωσης 2.7.7.2403

**Ημερομηνία:** 23 Νοεμβρίου 2026  
**Βάση αλλαγών:** `Opsis-Desktop-Customization-2.7.7.2402-net11-performance-source.zip`  
**Αποτέλεσμα:** ολοκληρωμένο ενημερωμένο **source ZIP**. **ΟΧΙ** επιβεβαιωμένο Windows EXE / installer.  
**Κανάλι:** το ενημερωμένο source παραδίδεται ανεξάρτητα από το παλαιότερο `main` του GitHub.

## Αλλαγές που ενσωματώθηκαν στον πηγαίο κώδικα

1. **Ασφαλής εισαγωγή wallpaper ZIP** (`src/Services/WallpaperPackages.cs` και `src/UI/MainWindow.WallpaperMedia.cs`). Ανάγνωση/προεπισκόπηση αρχείου χωρίς αποσυμπίεση, αναφορά περιεχομένων και μεγέθους, ρητή συναίνεση για HTML/JS, άρνηση traversal/επικίνδυνων ονομάτων/συνδέσμων, όριο αρχείων και αποσυμπιεσμένου μεγέθους, staged εξαγωγή με καθαρισμό σε αποτυχία.
2. **Ανάκτηση προφίλ** (`src/Services/WorkspaceProfileService.cs`). Διατήρηση προηγούμενης κατάστασης πριν από αλλαγές σε settings, widgets και wallpaper και προσπάθεια rollback σε σφάλμα. Απομόνωση ρυθμίσεων εργασίας από γενικές προτιμήσεις όπως γλώσσα και θέμα. Η επαναφορά είναι *best-effort*: εάν και η ανάκτηση αποτύχει, γίνεται καταγραφή για safe mode.
3. **Μειωμένες εγγραφές στον SSD και ασφαλέστερη ανάκτηση JSON** (`src/Services/AtomicFile.cs`). Παράλειψη εγγραφής ίδιου περιεχομένου, συγχρονισμός ανά αρχείο, προσωρινό αρχείο και ατομική αντικατάσταση τόσο του κύριου JSON όσο και του `.bak` και των ανακτημένων δεδομένων. Τα backup τηρούν την ήδη υπάρχουσα χρονική αποφυγή υπερβολικών εγγραφών.
4. **Weather Wallpaper με offline συμπεριφορά και διαφάνεια** (`src/Widgets/WeatherService.cs`, `src/Widgets/wallpaper/weather.js`, `weather-freshness.js`, `index.html`). Όταν αποτύχει η ανανέωση, δεν σβήνει η τελευταία έγκυρη παρατήρηση. Η αλλαγή συντεταγμένων δεν χρησιμοποιεί θερμοκρασία προηγούμενης πόλης. Προβάλλονται πηγή και ηλικία παρατήρησης, με ένδειξη παλαιότητας.
5. **Αυτοματοποιημένη ποιότητα** (`src/Services/SelfTestRunner.cs`, `tests/WeatherFreshness.test.cjs`, `tests/WeatherOffline.test.cjs`, `.github/workflows/windows-audit.yml`). Νέος C# self-test για νόμιμα και κακόβουλα wallpaper ZIP, JavaScript regressions, Windows runner με .NET 11, Inno Setup build και manifest SHA-256 installer (workflow έτοιμο **στο source**, δεν εκτελέστηκε στην παρούσα συνεδρία).
6. **Συγχρονισμός metadata έκδοσης** σε `Opsis.Studio.csproj`, `installer.iss` και `README-EL.md` στο **2.7.7.2403**.

## Συσχέτιση με ROADMAP-100-EL.md

| Πρόταση | Κατάσταση | Σημείωση |
|---|---|---|
| #6, #7 | Προϋπήρχαν στη βάση 2.7.7.2402 | Κοινή δειγματοληψία και ανά οθόνη εξοικονόμηση |
| #11 | Ενσωματώθηκε | Προεπισκόπηση και ασφαλής εισαγωγή ZIP |
| #16 | Μερικώς ενσωματώθηκε | Rollback προφίλ κατά την αποτυχία φόρτωσης· όχι πλήρης ιστορικό εκδόσεων |
| #20 | Μερικώς ενσωματώθηκε | Ατομικά JSON/backup, όχι δοκιμασμένη πραγματική διακοπή ρεύματος |
| #26 | Ενσωματώθηκε στον renderer | Τελευταία έγκυρη παρατήρηση / ομαλή αλλαγή targets |
| #27 | Ενσωματώθηκε | Πηγή και χρόνος δεδομένων πάνω στο Weather Wallpaper |
| #98 | Μερικώς ενσωματώθηκε | Workflow Windows και SHA-256 προετοιμάστηκαν, δεν εκτελέστηκαν |
| #1–5, #8–10, #12–15, #17–19, #21–25, #28–97, #99–100 | Ανοιχτά ή απαιτούν επιπλέον έλεγχο | Δεν δηλώνεται υλοποίηση χωρίς επαλήθευση |

## Πραγματικοί έλεγχοι στην παρούσα συνεδρία

- `node --test` από τη ρίζα του project: **17/17 PASS** (με offline / αλλαγή τοποθεσίας και μηνύματα παλαιότητας).
- `node --check` για `weather.js` και `weather-freshness.js`: **PASS**.
- XML parsing του `Opsis.Studio.csproj`: **PASS**.
- Έλεγχος ακεραιότητας αρχικού ZIP: **PASS**.
- **ΜΗ ΕΠΑΛΗΘΕΥΜΕΝΑ:** C# compilation, C# selftest execution, Windows UI / Explorer tests, Inno Setup compilation, installer smoke test, υπογραφή binaries.

## Πώς να παραχθεί και να επιβεβαιωθεί ο Windows installer

Σε Windows x64 με **.NET 11 SDK** και **Inno Setup 7**:

```powershell
cd <φάκελος-αποσυμπιεσμένου-project>
node --test
dotnet publish .\Opsis.Studio.csproj -c Release -r win-x64 --self-contained false -o publish
.\publish\Opsis.Studio.exe --run-selftest
.\installeruild.ps1 -Compiler 'C:\Program Files\Inno Setup 7\ISCC.exe'
Get-FileHash .\Output\OpsisSetup-2.7.7.2403.exe -Algorithm SHA256
```

Για UI/installer audit εκκίνησε χειροκίνητα το `.github/workflows/windows-audit.yml` **αφού** ο ενημερωμένος κώδικας μεταφερθεί σε συμβατό branch του αποθετηρίου. Ο έλεγχος σε πραγματικά Windows και η υπογραφή είναι απαραίτητα πριν από δημοσίευση για τελικούς χρήστες.

## Περιορισμοί και επόμενη τεχνική προτεραιότητα

Το GitHub `gs-applabs/Opsis` έχει παλαιότερο `main` (2.4.6.2 στο χρόνο του ελέγχου), ενώ η βάση 2.7.7.2402 παραδόθηκε ως ZIP. Δεν τροποποιήθηκε το `main`, ώστε να μη χαθούν οι νεότερες λειτουργίες του source. Η πλήρης ολοκλήρωση όλων των 100 σημείων απαιτεί επιπλέον ενσωμάτωση και επαλήθευση. Πρώτες εκκρεμότητες: μετρήσεις CPU / WebView2, frame-time p95/p99, ανάκτηση ασφαλούς λειτουργίας, πολυοθονικές δοκιμές, Windows VM testing και reproducible installer.
