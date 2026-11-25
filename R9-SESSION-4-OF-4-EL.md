# OPSIS 2.8.0.2500 — Session 4/4 (R9) — Τεχνική παράδοση

**Ημερομηνία:** 23 Νοεμβρίου 2026  
**Βάση:** R8-S3 · **Εργασίες:** #12, #13, #32, #33, #44, #45, #78, #92, #93, #99.  
**Κατάσταση:** πηγαίος κώδικας R9 — **ΟΧΙ επαληθευμένη τελική έκδοση**.

## Αλλαγές που εφαρμόστηκαν

1. **#12 — Δικαιώματα plugins:** προστέθηκε manifest `opsis-permissions.json`, καταγραφή πρόσβασης σε δίκτυο, ήχο και αρχεία, ενσωμάτωση στο SHA fingerprint, εμφάνιση κατά την αποδοχή, και ανάκληση έγκρισης. **Περιορισμός:** τα plugins φορτώνονται in-process· η δήλωση **δεν αποτελεί sandbox ούτε επιβάλλει OS-level περιορισμούς**. Απαιτείται απομόνωση σε ξεχωριστή διεργασία για πλήρη επιβολή.
2. **#13 — Συναίνεση web widget:** `WidgetConsentStore`, SHA-256 manifest/entry, ερώτηση συγκατάθεσης στην πρώτη πρόσβαση CPU/RAM, δυνατότητα ανάκλησης, περιορισμός συχνότητας κλήσεων. Η grant δεν επιτρέπει browser network permissions.
3. **#32, #33 — Πανόραμα:** επιλογή δύο γειτονικών monitors και 0–400px bezel gap, WebView image cropping σε ανεξάρτητα παράθυρα. Προς επαλήθευση: διαφορετικά DPI, πολλαπλές διάταξεις οθονών, κινούμενα βίντεο.
4. **#44, #45 — DeskZones:** έλεγχος γειτνίασης ζωνών, μετατόπιση κοινού ορίου με όρια και ατομικό preview, προσωρινή επέκταση παραθύρου με undo. Προστέθηκε επαναφορά της προηγούμενης κατάστασης auto-tiling όταν δεν απομένουν επεκτάσεις.
5. **#78 — Visualizer:** επιλογή μικροφώνου ή συσκευής εισόδου σε WASAPI, διατήρηση default output loopback, αποθήκευση επιλογής. **Περιορισμός:** κοινή WASAPI υπηρεσία και άλλοι consumers χρειάζονται απομόνωση πηγών· απαιτείται πραγματικός έλεγχος Windows/άδειας μικροφώνου.
6. **#92 — Εξαρτήσεις:** cancellation token για εγκατάσταση μέσω winget, διατήρηση ασφαλούς `/DEPS=none` σε silent installer, Windows QA καταγραφή runtimes και WebView2. Οι 4 καθαρές VM διαμορφώσεις **δεν έχουν δοκιμαστεί**.
7. **#93 — Explorer:** δυνατότητα `-ForceClassicMenu` χωρίς native C++ compiler, εγκατάσταση/ανάκληση πέντε commands στο μητρώο από R9 Windows QA. Επαλήθευση native shell extension **εκκρεμεί**.
8. **#99 — Update/side-by-side:** Windows QA script ελέγχει έκδοση αρχείων, uninstall registration, uninstall και προαιρετική αναβάθμιση από SHA-επαληθευμένο προηγούμενο installer, καθώς και παλαιό 2.4.6.1 δίπλα στο νέο όταν το αντίστοιχο επαληθευμένο αρχείο διατίθεται. Δεν εκτελέστηκε Windows QA.

## Πραγματικοί έλεγχοι που εκτελέστηκαν

| Έλεγχος | Αποτέλεσμα |
|---|---|
| `node --test` (Node.js 22.16.0, Linux) | **37 PASS · 0 FAIL** |
| Επαλήθευση δομής/συνδέσεων σε 8 νέα static regression tests | PASS, περιλαμβάνονται στα 37 |
| `dotnet publish` για win-x64 | **BLOCKED** — το .NET SDK δεν είναι εγκατεστημένο |
| WinForms UI audit, Windows 2022/2025, GPU/installer tests | **NOT RUN** — Windows runner και R9 branch μη διαθέσιμα εδώ |
| Inno Setup 7 `ISCC.exe` | **BLOCKED** — απαιτεί Windows και Inno Setup |
| Αναβάθμιση προηγούμενης έκδοσης / side-by-side 2.4.6.1 | **NOT RUN** — χρειάζονται SHA-ελεγμένοι installers και Windows VM |

**ΣΗΜΑΝΤΙΚΟ:** Μια επιτυχημένη εκτέλεση workflow σε παλαιότερο branch του GitHub **δεν επαληθεύει** το R9. Το `.github/workflows/r9-release-qa.yml` πρέπει να εκτελεστεί πάνω σε branch που περιέχει **ακριβώς τα αρχεία αυτού του ZIP**.

## Πύλη Windows για παραγωγή installer

Από αυτόν τον source tree, σε **απομονωμένο Windows VM/CI** με .NET 11 SDK και Inno Setup 7:

```powershell
# Μόνο σε αναλώσιμο Windows περιβάλλον:
./tools/qa-r9-windows.ps1 -Compiler 'C:\Program Files\Inno Setup 7\ISCC.exe' -AllowLocalDisposableVm
```

Προαιρετικά `-PreviousInstaller <διαδρομή> -PreviousSha256 <64-hex>`, `-LegacyInstaller <διαδρομή> -LegacySha256 <64-hex>`. Το workflow αρχειοθετεί `qa-results` και δημοσιεύει τον installer ως **UNRELEASED validated build** μόνον αν όλες οι ενεργές πύλες περάσουν· skipped scenarios αναφέρονται `NOT_VERIFIED`.

## Κατάσταση 100 σημείων μετά το R9

**66** νέες υλοποιήσεις · **17** προϋπάρχουσες · **14** μερικές / προς QA · **3** υποχρεωτικές Windows επικυρώσεις. **83** έχουν κώδικα νέας/προϋπάρχουσας λειτουργίας, όχι όλες επιβεβαιωμένη λειτουργία. **Η 2.8.0.2500 δεν είναι ακόμη έτοιμη για release.**
