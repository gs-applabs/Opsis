# Backport του νέου μηχανισμού καιρού στην 2.4.6.2

Πηγή: κλάδος `claude/2-6-0-app-418714` (2.6.0-app), commit «Weather: providers behind one interface…».
Patch έτοιμο: `docs/weather-backport/0001-weather-providers-for-2.4.6.2.patch` (git format-patch, γραμμένο ώστε να εφαρμόζεται και στο `main` της 2.4.6.x).

## Επαλήθευση που έγινε
Σε **ξεχωριστό προσωρινό worktree** (όχι στο `C:\w\Opsis-2.4.6.x`) πάνω στο `main` @ `9c97bf1`:
`git am --3way` εφαρμόστηκε χωρίς συγκρούσεις (αυτόματη συγχώνευση του `MainWindow.cs`) και τα tests περνούν: **221 passed, 0 failed** (52 καινούργια του καιρού). Η 2.4.6.x δεν έχει `AppVersion`, γι' αυτό ο κώδικας διαβάζει την έκδοση απευθείας από το assembly.

## Βήματα για τη συνεδρία της 2.4.6.2
Από τον φάκελο του worktree 2.4.6.x (ΜΟΝΟ όταν ο συντονιστής το περάσει):
```bash
git status                      # να είναι καθαρό· αν όχι, commit/αποθήκευση πρώτα
git am --3way <ΔΙΑΔΡΟΜΗ_ΤΟΥ_REPO_2.6.0-app>/docs/weather-backport/0001-weather-providers-for-2.4.6.2.patch
cd tests/Opsis.Tests
dotnet test -m:1 -p:UseSharedCompilation=false -nodeReuse:false
```
Αν το `git am` σταματήσει με σύγκρουση σε `src/UI/MainWindow.cs` ή `locales/*.json`: τα σημεία είναι (α) το μπλοκ μετά το `panel.Controls.Add(locRow);` (κουμπιά τοποθεσίας) και η `SourceLine`, (β) 10 νέα κλειδιά `Wallpaper.Weather*` μετά το `Wallpaper.WeatherLocationCurrent` σε κάθε γλώσσα. Λύσε χειροκίνητα, `git add`, `git am --continue`.
Εναλλακτικά `git cherry-pick <sha>` από το repo `C:\w\Opsis` αν ο κλάδος είναι ορατός (shared git store).

## Αρχεία που αγγίζει
Νέα: `src/Widgets/WeatherProviders.cs`, `src/Widgets/DeviceLocation.cs`, `tests/Opsis.Tests/WeatherProvidersTests.cs`, `docs/WEATHER-PROVIDERS.md` (μετονομασία από `WEATHER_PROVIDERS.md`).
Αλλαγές: `src/Widgets/WeatherService.cs` (το wallpaper καλεί το `WeatherHub`), `src/UI/MainWindow.cs`, `locales/*.json` (14), `THIRD_PARTY_NOTICES.md`.

## Μετά το backport
- Το τελευταίο έλεγχο (ζωντανά): άνοιξε Wallpaper → Πληροφορίες και έλεγξε τη γραμμή «Πηγή: …· παρατήρηση …», «Χρήση της τοποθεσίας μου», «Διαγραφή αποθηκευμένης τοποθεσίας» (ανεπιβεβαίωτα σε πραγματικά Windows, βλ. `docs/WEATHER-PROVIDERS.md`).
- Καμία αλλαγή στο `weather.js` ή στα δεδομένα χρήστη· το JSON που διαβάζει το wallpaper κρατά όλα τα παλιά κλειδιά.
- Σημείωση CHANGELOG: «Καιρός: NWS (ΗΠΑ) / Open-Meteo best_match / MET Norway με fallback, πηγή και ώρα παρατήρησης στις ρυθμίσεις, προαιρετική τοποθεσία συσκευής (στρογγυλοποιημένη), υπολογισμός ήλιου/σελήνης».
