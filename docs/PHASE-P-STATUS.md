# Φάση P — νέος μηχανισμός plugin 1.0.0 (Opsis 2.6.0): κατάσταση

Κλάδοι: Opsis `2.6.0-plugins` (βάση 2.6.0-beta 1ba02d1) · Crawler `phase-p-contract-1.0` (βάση main 602ef63 = 0.24.1, που μένει ανέγγιχτο στο `main` για το Opsis 2.4.6) · Χρονικολόγιο: ΜΟΝΟ οδηγίες, `docs/PHASE-P-CHRONIKOLOGIO.md`.
Τοπικά commits, καμία δημοσίευση.

## Αποφάσεις
1. **Contract 1.0.0.0** (`OpsisNewsCrawler.Plugin.Abstractions`): μόνο προσθήκες — `PluginUpdateSource`, `IPluginUpdateSource`, `PluginHostSettings`, `IPluginHostSettingsAware`. Όλα τα παλιά αμετάβλητα (έλεγχος με surface test του san-shmera, όπου τα ονόματα τύπων κανονικοποιούνται 0.8.0.0→1.0.0.0).
2. **«Host 1.0.0.0»**: το assembly του Opsis μένει `2.6.0.0` (ήδη έτσι στη beta). Το `UpdateCheckService`/`AppVersion` διαβάζουν το assembly version του host· αν γινόταν 1.0.0.0 ο Opsis θα έβλεπε κάθε release 2.x ως «νεότερο». Το «1.0.0.0 του host» υλοποιείται ως **έκδοση του plugin API του host** (`PluginHost.ContractVersion` = 1.0.0.0, το assembly που δίνει ο PluginLoadContext). ΑΠΟΦΑΣΗ ΧΡΗΣΤΗ αν θέλει κάτι άλλο.
3. **Παλιά plugins (0.8.0.0) σε νέο host: ΦΟΡΤΩΝΟΥΝ.** Ο `PluginLoadContext.Load` επιστρέφει πάντα το contract assembly του host και το runtime το δέχεται όποια έκδοση κι αν ζήτησε το plugin (δοκιμασμένο με πραγματικό DLL χτισμένο με 0.8.0.0 — `PluginContractLoadTests`). Χωρίς binding redirect.
4. **Plugin χτισμένο με ΝΕΟΤΕΡΟ contract από του host: απορρίπτεται** με μήνυμα «needs a newer Opsis … update Opsis» (`PluginContractCheck`, διαβάζει μόνο metadata, δεν τρέχει κώδικας plugin) στο `plugins.log` και στο About (`PluginHost.Skipped`). Επίσης `ReflectionTypeLoadException` → ίδιο μήνυμα.
5. **PluginTrust**: SHA-256 έγκριση παραμένει υποχρεωτική. Γνωστό plugin με άλλο hash (π.χ. ενημέρωση από το κουμπί του Opsis) ΔΕΝ φορτώνει και ζητά ΜΙΑ νέα έγκριση με ξεχωριστό κείμενο «Plugin updated» (`PluginTrustRequest.IsUpdate`, `PreviousSha256`). Ποτέ σιωπηλή εμπιστοσύνη.
6. **Ενημερώσεις plugins (γραμμή 15)**: μόνο από το κουμπί «Έλεγχος τώρα» στο About του Opsis (δεν υπάρχει αυτόματος έλεγχος plugins). `PluginUpdateService`: μόνο releases API του repo που δηλώνει το plugin (`IPluginUpdateSource`), owner `gs-applabs` Ή `giorstergiopoulos-design`, asset μόνο από `https://github.com/<owner>/<repo>/releases/download/`, SHA-256 (GitHub digest ή `<zip>.sha256`) πριν το unzip, `SafeZip` σε staging `%AppData%\Opsis\plugin-updates\<φάκελος>`, τοποθέτηση στην επόμενη εκκίνηση ΠΡΙΝ φορτωθεί οτιδήποτε, μετά η ερώτηση έγκρισης. Το plugin ενημερώνεται (`HostManagesUpdates=true`) να μην τρέχει δικό του updater.
7. **Κοινές ρυθμίσεις (γραμμή 16)**: ο host στέλνει `PluginHostSettings` (γλώσσα, θέμα dark, hardware tier, κλίμακα UI, «ενημερώσεις από Opsis») σε κάθε plugin που υλοποιεί `IPluginHostSettingsAware`, στην εκκίνηση και σε αλλαγή γλώσσας/θέματος/profile. Το Crawler 0.25.0 κρύβει την κάρτα ενημερώσεων και δεν τρέχει updater όταν είναι hosted.
8. **CI SHA**: `libs/contract.sha256` + `PluginContractLoadTests.TheHostContractIs1000AndLibsHoldTheSameFile`, `tools/check-contract-sha.ps1` (βήμα στο `plugin-e2e.yml`)· στον Crawler `reference/contract.sha256`, `ContractCopiesTests`, `tools/check-contract-sha.ps1` (βήμα στο `windows-ci.yml`) και post-build που αντιγράφει το canonical DLL στο output του plugin/app (το output ΕΙΝΑΙ το ίδιο αρχείο, SHA-256 `f5f1a21c…`). Το αντίγραφο του Χρονικολόγιου ελέγχεται με `-Extra <διαδρομή>` όταν ο agent του το ενημερώσει.

## Πίνακας φόρτωσης (load matrix)
| Plugin → Host | Αποτέλεσμα | Απόδειξη |
|---|---|---|
| παλιό (0.8.0.0) → Opsis 2.6.0 (1.0.0.0) | ΦΟΡΤΩΝΕΙ, δεν ζητά τα νέα interfaces | test με DLL StubOld |
| νέο (1.0.0.0, δεν χρησιμοποιεί νέους τύπους) → Opsis 2.4.6 (0.8.0.0) | ΦΟΡΤΩΝΕΙ | test: context που μιμείται το PluginLoadContext του 2.4.6 + contract 0.8.0.0 |
| νέο που υλοποιεί `IPluginUpdateSource`/`IPluginHostSettingsAware` (π.χ. Crawler 0.25.0) → Opsis 2.4.6 | ΑΠΟΤΥΧΙΑ φόρτωσης (TypeLoadException «IPluginUpdateSource»)· το 2.4.6 γράφει plugins.log και το παραλείπει, δεν πέφτει | ίδιο test· ΓΙ' ΑΥΤΟ το Crawler 0.25.0 «απαιτεί Opsis 2.6.0», και το 0.24.1 μένει για 2.4.6 |
| νέο → Opsis 2.6.0 | ΦΟΡΤΩΝΕΙ, παίρνει update source + shared settings | test StubNew |
| plugin με contract ΝΕΟΤΕΡΟ από του host → ο host | ΑΠΟΡΡΙΠΤΕΤΑΙ με «needs a newer Opsis» | `PluginContractCheck` test |
Το 2.4.6 δεν δοκιμάστηκε με το πραγματικό exe του (μόνο μίμηση του load context) → **NOT VERIFIED σε πραγματικό 2.4.6**.

## Σειρά συντονισμένου release
1. Crawler: merge `phase-p-contract-1.0` (0.25.0) σε ΞΕΧΩΡΙΣΤΟ κλάδο/tag ώστε το `main` 0.24.1 να μείνει για 2.4.6 (απόφαση συντονιστή: π.χ. κλάδος `main-2.6` ή tag πάνω από main με τη διαφορά ότι η 0.24.x γραμμή φύλαγε contract 0.8.0.0).
2. Opsis: merge `2.6.0-plugins` στο `2.6.0-beta`, αντιγραφή libs ήδη έγινε (SHA `f5f1a21c…`), `tools\check-contract-sha.ps1`.
3. Release Opsis 2.6.0 (installer με bundled plugin Crawler 0.25.0) — ο χρήστης εγκρίνει SHA-256 του plugin μία φορά.
4. Release Crawler 0.25.0 (assets: Setup, portable, `OpsisNewsCrawler-Plugin.zip` + `.sha256`).
5. Χρονικολόγιο: οδηγίες στο `PHASE-P-CHRONIKOLOGIO.md`, release ΜΕΤΑ τον Opsis 2.6.0.
6. Δοκιμή σε εγκατεστημένο Opsis (e2e + χειροκίνητη).

## Τι έγινε / τι ΜΕΝΕΙ
- Έγινε: όλα τα παραπάνω + tests (15 νέα στο Opsis `PluginContractLoadTests`, 6 στον Crawler `ContractCopiesTests`) + 14 γλώσσες για τα νέα strings (Opsis: 10 κλειδιά, Crawler: history).
- Κοινές ρυθμίσεις που ΜΕΝΟΥΝ (το contract δεν τις καλύπτει ακόμη): ρυθμίσεις περιεχομένου (χώρα/περιοχή του χρήστη για feeds, προεπιλεγμένη πόλη καιρού), proxy/δίκτυο, ρύθμιση «εκκίνηση με Windows» (το Crawler την κρύβει ήδη όταν hosted, αλλά δεν υπάρχει γενικός μηχανισμός), αλλαγή `UiScalePercent` ζωντανά (στέλνεται στην εκκίνηση/γλώσσα/θέμα, όχι σε δική της αλλαγή), διαφάνεια παραθύρου (`WindowOpacity` δεν είναι στο `PluginHostSettings`· το SetTheme την έχει).
- Δεν έγινε: έλεγχος ενημερώσεων plugin από ιδιωτικά repos (χρειάζεται token — το san-shmera είναι ιδιωτικό)· κουμπί «Επανεκκίνηση τώρα» μετά το staging (δείχνει μήνυμα· εφαρμογή στην επόμενη εκκίνηση)· ενημέρωση plugin που είναι στο Program Files (ο φάκελος στο AppData έχει προτεραιότητα με την νεότερη έκδοση, ο εγκατεστημένος μένει)· οπτικός έλεγχος του About/της ερώτησης σε πραγματικά Windows.
