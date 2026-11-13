# Backport του νέου μηχανισμού καιρού στην 2.4.6.2

Πηγή: κλάδος `claude/2-6-0-app-418714` (2.6.0-app), commit «Weather: providers behind one interface…».
Patch έτοιμο: `docs/weather-backport/0001-weather-providers-for-2.4.6.2.patch` (git format-patch, γραμμένο ώστε να εφαρμόζεται και στο `main` της 2.4.6.x).

## Επαλήθευση που έγινε
Σε **ξεχωριστό προσωρινό worktree** (όχι στο worktree της 2.4.6.x) πάνω στο `main` @ `9c97bf1`:
`git am --3way` εφαρμόστηκε χωρίς συγκρούσεις (αυτόματη συγχώνευση του `MainWindow.cs`) και τα tests περνούν: **221 passed, 0 failed** (52 καινούργια του καιρού). Η 2.4.6.x δεν έχει `AppVersion`, γι' αυτό ο κώδικας διαβάζει την έκδοση απευθείας από το assembly.

## Βήματα για τη συνεδρία της 2.4.6.2
Από τον φάκελο του worktree 2.4.6.x (ΜΟΝΟ όταν ο συντονιστής το περάσει):
```bash
git status                      # να είναι καθαρό· αν όχι, commit/αποθήκευση πρώτα
git am --3way <ΔΙΑΔΡΟΜΗ_ΤΟΥ_REPO_2.6.0-app>/docs/weather-backport/0001-weather-providers-for-2.4.6.2.patch
cd tests/Opsis.Tests
dotnet test -m:1 -p:UseSharedCompilation=false -nodeReuse:false
```
Αν το patch έχει μετατραπεί σε CRLF από το checkout (σφάλμα «corrupt patch»): `git am --3way --ignore-whitespace …` ή `dos2unix` στο αρχείο. Αν το `git am` σταματήσει με σύγκρουση σε `src/UI/MainWindow.cs` ή `locales/*.json`: τα σημεία είναι (α) το μπλοκ μετά το `panel.Controls.Add(locRow);` (κουμπιά τοποθεσίας) και η `SourceLine`, (β) 10 νέα κλειδιά `Wallpaper.Weather*` μετά το `Wallpaper.WeatherLocationCurrent` σε κάθε γλώσσα. Λύσε χειροκίνητα, `git add`, `git am --continue`.
Εναλλακτικά `git cherry-pick <sha>` από το repo του 2.6.0 αν ο κλάδος είναι ορατός (shared git store).

## Αρχεία που αγγίζει
Νέα: `src/Widgets/WeatherProviders.cs`, `src/Widgets/DeviceLocation.cs`, `tests/Opsis.Tests/WeatherProvidersTests.cs`, `docs/WEATHER-PROVIDERS.md` (μετονομασία από `WEATHER_PROVIDERS.md`).
Αλλαγές: `src/Widgets/WeatherService.cs` (το wallpaper καλεί το `WeatherHub`), `src/UI/MainWindow.cs`, `locales/*.json` (14), `THIRD_PARTY_NOTICES.md`.

## Μετά το backport
- Το τελευταίο έλεγχο (ζωντανά): άνοιξε Wallpaper → Πληροφορίες και έλεγξε τη γραμμή «Πηγή: …· παρατήρηση …», «Χρήση της τοποθεσίας μου», «Διαγραφή αποθηκευμένης τοποθεσίας» (ανεπιβεβαίωτα σε πραγματικά Windows, βλ. `docs/WEATHER-PROVIDERS.md`).
- Καμία αλλαγή στο `weather.js` ή στα δεδομένα χρήστη· το JSON που διαβάζει το wallpaper κρατά όλα τα παλιά κλειδιά.
- Σημείωση CHANGELOG: «Καιρός: NWS (ΗΠΑ) / Open-Meteo best_match / MET Norway με fallback, πηγή και ώρα παρατήρησης στις ρυθμίσεις, προαιρετική τοποθεσία συσκευής (στρογγυλοποιημένη), υπολογισμός ήλιου/σελήνης».

## Μέρος 2 (2026-11-13): ακριβής περιοχή, contract, διόρθωση σελίδας — πάνω στην 2.4.6.2 που έχει ήδη το μέρος 1
Patches (με `--binary`, εφαρμόστηκαν και δοκιμάστηκαν σε προσωρινό worktree πάνω στο τρέχον `main` της 2.4.6.x: **227 tests passed**): `docs/weather-backport/2.4.6.2/0001…0004`.
1. Offline gazetteer GeoNames (CC BY 4.0, ~15.000 οικισμοί/συνοικίες της Ελλάδας, 290 KB, lazy, weak reference) + προαιρετικό reverse geocoding OpenStreetMap Nominatim **μόνο όταν πατιέται «Χρήση της τοποθεσίας μου»** (πολιτική χρήσης: 1 αίτημα/δευτ., User-Agent, cache, καμία περιοδική/μαζική χρήση)· στις ρυθμίσεις φαίνονται όνομα, ιεραρχία και πηγή.
2. Μόνο UI/ρυθμίσεις/μεταφράσεις (14 γλώσσες) του παραπάνω.
3. **Contract plugins με τα rich feed types** (ίδιο assembly 0.8.0.0): χωρίς αυτό το Crawler 0.24 δίνει «Could not load type PluginRichFeed» στην 2.4.6.x.
4. Οι αριθμητικές γραμμές κόβουν την αποθηκευμένη τιμή στα όρια (σφάλμα σελίδας DeskContainers).
```bash
git am --3way docs/weather-backport/2.4.6.2/*.patch      # από τον φάκελο του worktree 2.4.6.x, μόνο όταν το επιτρέψει ο συντονιστής
```
Όχι στην 2.4.6.2 (μόνο 2.6.0): ενσωμάτωση εμφάνισης Windows στην Εξατομίκευση, εκκίνηση-πρώτα, installer που αφαιρεί το παλιό Appearance και σκούρος installer, καθαρισμός καταργημένων εφαρμογών.
