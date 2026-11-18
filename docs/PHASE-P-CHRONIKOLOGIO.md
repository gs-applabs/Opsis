# Φάση P — οδηγίες για τον agent του Χρονικολόγιου (SAN-SHMERA)

Για τον agent που κατέχει το repo SAN-SHMERA (το τοπικό clone του `gs-applabs/san-shmera`; plugin «Σαν σήμερα», `Chronikologio.OpsisPlugin.dll`). Ο agent της φάσης P ΔΕΝ άγγιξε αυτό το repo.
Ισχύει για Opsis 2.6.0 (contract 1.0.0.0). Τοπικά commits, χωρίς push/tag/release· δοκιμές μόνο με `OPSIS_APPDATA=<temp>`.

## 0. Τι άλλαξε στο contract (μόνο προσθήκες)
- Assembly `OpsisNewsCrawler.Plugin.Abstractions` **1.0.0.0** (ήταν 0.8.0.0). Κανονικό αντίγραφο (canonical): `<repo OpsisNewsCrawler, branch phase-p-contract-1.0>\tests\OpsisNewsCrawler.Plugin.Tests\reference\OpsisNewsCrawler.Plugin.Abstractions.dll` (μετά το merge: `Opsis\libs\...` και `OpsisNewsCrawler\tests\...\reference\...`), 32 768 bytes, SHA-256 `f5f1a21cee0435abdc19e609fd785b762fc706103991579afe3eb13dcf10ffaf` (το αρχείο `contract.sha256` δίπλα του).
- Νέοι προαιρετικοί τύποι: `PluginUpdateSource(Repository, AssetName)` + `IPluginUpdateSource { GetUpdateSource() }`, `PluginHostSettings(Language, Dark, HardwareTier, UiScalePercent, HostManagesUpdates)` + `IPluginHostSettingsAware { SetHostSettings(...) }`. Όλα τα παλιά (`PluginFeeds`, `PluginRichFeed`, `IPluginWidgetProvider`, …) αμετάβλητα.
- Ο Opsis 2.6.0 δίνει ΠΑΝΤΑ το δικό του contract assembly σε κάθε plugin (PluginLoadContext), οπότε το υπάρχον Chronikologio (χτισμένο με 0.8.0.0) φορτώνει στον 2.6.0 ΧΩΡΙΣ αλλαγές (δοκιμασμένο με πραγματικό plugin DLL). Το rebuild χρειάζεται μόνο για να χρησιμοποιήσει τα νέα interfaces.

## 1. Βήματα
1. Αντίγραψε το canonical DLL πάνω στο `san-shmera\libs\OpsisNewsCrawler.Plugin.Abstractions.dll`· έλεγξε SHA-256 (`Get-FileHash`) = η τιμή πάνω. Μην χτίσεις δικό σου αντίγραφο του Abstractions (SHA-256 θα διαφέρει).
2. Στο csproj του plugin κράτα `<Reference ...><Private>false</Private>` (ο host μοιράζεται το assembly). Rebuild.
3. (Συνιστάται) Υλοποίησε στην κλάση του plugin:
   - `IPluginUpdateSource`: `new PluginUpdateSource("gs-applabs/san-shmera", "Chronikologio-Plugin.zip")` (ο owner πρέπει να είναι `gs-applabs` ή `giorstergiopoulos-design`).
   - `IPluginHostSettingsAware`: αποθήκευσε `settings.Language` (αντικαθιστά ό,τι δική σου ρύθμιση γλώσσας UI· το Region/Language των περιεχομένων `gr/cy × el/en` μένει δικό σου) και όταν `settings.HostManagesUpdates` είναι true ΜΗΝ τρέχεις δικό σου updater/έλεγχο ενημερώσεων και κρύψε σχετική κάρτα/κουμπί.
   - ΠΡΟΣΟΧΗ: μόλις το plugin υλοποιεί τα νέα interfaces, ΔΕΝ φορτώνει σε Opsis 2.4.6 (TypeLoadException στο plugins.log, το plugin παραλείπεται, ο Opsis δεν πέφτει). Αν θες ταυτόχρονη υποστήριξη 2.4.6, κράτα branch/έκδοση χτισμένη με το 0.8.0.0 DLL (παλιός tag) και βάλε στις σημειώσεις «απαιτεί Opsis 2.6.0» για τη νέα.
4. Έκδοση: ανέβασε όλα τα σημεία έκδοσης μαζί (csproj, `Version` της κλάσης, history σε όλες τις γλώσσες, CHANGELOG, installer) και πρόσθεσε «απαιτεί Opsis 2.6.0».
5. Release asset για το κουμπί ενημέρωσης του Opsis: zip με τα αρχεία του φακέλου του plugin ΣΤΗ ΡΙΖΑ του zip (`Chronikologio.OpsisPlugin.dll`, deps, κ.λπ., χωρίς υποφάκελο-περιτύλιγμα) με όνομα ακριβώς `Chronikologio-Plugin.zip` + `Chronikologio-Plugin.zip.sha256` (`<sha256>  Chronikologio-Plugin.zip`). Το GitHub digest (`sha256:`) αρκεί αν υπάρχει.
   - ⚠ Το repo `gs-applabs/san-shmera` είναι ΙΔΙΩΤΙΚΟ: ο Opsis ρωτά το δημόσιο releases API χωρίς token, άρα ΔΕΝ θα βρει releases (σιωπηλά «δεν υπάρχει ενημέρωση»). Ο συντονιστής πρέπει να αποφασίσει: δημόσιο repo releases ή δημόσιο mirror μόνο για τα zip (και τότε το `Repository` δείχνει εκεί). Μέχρι τότε η ενημέρωση γίνεται με τον installer του plugin.
6. Tests: πρόσθεσε/ενημέρωσε (α) test ότι το DLL στο `libs\` έχει SHA-256 όπως παραπάνω και assembly version 1.0.0.0· (β) test ότι η κλάση υλοποιεί τα δύο interfaces και ότι το `GetUpdateSource()` έχει owner `gs-applabs` και asset που τελειώνει σε `.zip`· (γ) αν υπάρχει surface test, ανανέωσέ το (τα ονόματα τύπων περιέχουν `Version=1.0.0.0`).
7. Έλεγχος φόρτωσης στον 2.6.0 (μετά το merge του Opsis): `OPSIS_APPDATA=<temp>`, αντιγραφή σε `<temp>\plugins\Chronikologio`, εκκίνηση, αναμένεται ΕΡΩΤΗΣΗ έγκρισης SHA-256 (νέο περιεχόμενο) και μετά φόρτωση· `plugins.log` χωρίς σφάλμα· `PluginFeeds` περιέχει `san-simera`. Αν είχε εγκριθεί παλιότερη έκδοση, το μήνυμα είναι «Το plugin άλλαξε (ενημέρωση)» — ΜΙΑ ερώτηση, ποτέ σιωπηλή εμπιστοσύνη.
8. Τρέξε όλα τα tests του repo· ό,τι δεν επαληθεύτηκε σε πραγματικά Windows = NOT VERIFIED.

## 2. Τι ΔΕΝ πρέπει να κάνεις
- Να αλλάξεις το contract ή να κρατήσεις δικό σου αντίγραφο με άλλο SHA-256.
- Να βάλεις δικό σου αυτόματο updater όταν `HostManagesUpdates` είναι true.
- Push/tag/release χωρίς ρητή εντολή του χρήστη.
