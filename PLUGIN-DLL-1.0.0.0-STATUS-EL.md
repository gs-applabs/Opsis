# OPSIS 2.8.0.2500 — DLL Plugins 1.0.0.0 (στάδιο ενσωμάτωσης)

**Βάση:** source 2.8.0.2500 preview, .NET 11 host. **Δεν είναι τελικός installer.**

## Ακριβής σημασία της έκδοσης 1.0.0.0

| Δομικό στοιχείο | Windows FileVersion | AssemblyVersion | Έκδοση προϊόντος/ενημερώσεων |
|---|---:|---:|---|
| `OpsisNewsCrawler.OpsisPlugin.dll` | **1.0.0.0** | **1.0.0.0** | 0.24.1 |
| `Chronikologio.OpsisPlugin.dll` | **1.0.0.0** | **1.0.0.0** | 0.9.0 |
| `OpsisNewsCrawler.Plugin.Abstractions.dll` (κοινό συμβόλαιο, **όχι** plugin) | χωριστή έκδοση | **0.8.0.0** | 0.24.1 στο Crawler |
| `Opsis.UI.dll` (κοινά controls, **όχι** plugin) | 0.4.5 | **0.1.0.0** | 0.4.5 |
| Opsis Media Suite (Electron) | — | — | 2.4.1 |

Η διαφορά έκδοσης προϊόντος / assembly είναι σκόπιμη: ο μηχανισμός Opsis επιλέγει το νεότερο εγκατεστημένο plugin μέσω του `IOpsisNewsCrawlerPlugin.Version`. Αν η τιμή αυτή ήταν μόνιμα 1.0.0.0, μελλοντικές ενημερώσεις του ίδιου plugin δεν θα μπορούσαν να αντικαταστήσουν την παλιότερη έκδοση. Για το λόγο αυτό τα DLL έχουν AssemblyVersion/FileVersion 1.0.0.0, αλλά η έκδοση προϊόντος συνεχίζει να αυξάνεται ανεξάρτητα.

## GitHub αλλαγές

- Crawler PR: https://github.com/gs-applabs/OpsisNewsCrawler/pull/2
- Χρονικολόγιο PR: https://github.com/gs-applabs/san-shmera/pull/1
- Opsis.UI PR: https://github.com/gs-applabs/Opsis.UI/pull/1

## Εφαρμογές στον host του OPSIS

- Το `PluginUiStyle.Publish()` αποφεύγει επαναλαμβανόμενες πανομοιότυπες εγγραφές `plugin-ui.json`, μειώνοντας περιττές εγγραφές στον SSD και άσκοπες ειδοποιήσεις watcher.
- Ο έλεγχος `--test-plugin-widget` αναφέρει χωριστά `ids`, `versions` (product) και `pluginDlls` (assembly), μαζί με `feeds`.
- Το workflow `.github/workflows/plugin-compatibility.yml` κάνει πραγματικό Windows publish/φόρτωση του Crawler + Chronikologio και ελέγχει ότι **τα δύο *Plugin.dll είναι 1.0.0.0** και το κοινό συμβόλαιο παραμένει συμβατό. Τρέχει από τα staging branches μέχρι τη συγχώνευσή τους.
- Ελέγχονται επίσης το αντικατασταθέν ημερολόγιο, το `san-simera` feed και τα Media Suite tests/selftests.

## Κατάσταση επαλήθευσης

- **PASS:** τοπικά JS test suite 27/27 (συμπεριλαμβανομένων των νέων version/contract tests), YAML/JSON/XML parsing, ZIP CRC.
- **PASS:** Crawler Windows GitHub CI και ειδικό DLL 1.0.0.0 build/test/upload: https://github.com/gs-applabs/OpsisNewsCrawler/actions/runs/45173655617
- **PASS:** Opsis.UI κοινή CI matrix Windows Server 2022/2025 & SDK .NET 10/11: https://github.com/gs-applabs/Opsis.UI/actions/runs/45171522981
- **PASS (εστιασμένος έλεγχος):** Chronikologio `Chronikologio.OpsisPlugin.dll` 1.0.0.0 — Windows compile, metadata/contract tests και πλήρες publish artifact: https://github.com/gs-applabs/san-shmera/actions/runs/45176772949. Το πακέτο αυτό περιλαμβάνει τις νεότερες διορθώσεις διαχείρισης πόρων/παραθύρων του branch.
- **Εκκρεμεί πλήρες PASS:** το συνολικό Chronikologio WinForms CI μετά τις διορθώσεις για dark theme, fonts, tooltips, native USER/GDI handles και επαναλαμβανόμενες αλλαγές χώρας: https://github.com/gs-applabs/san-shmera/actions/runs/45176772967. Προηγούμενο πλήρες run είχε 83/84 επιτυχή με μία αποτυχία `Error creating window handle`, οπότε δεν έχει εγκριθεί ακόμη το host-level release.
- **ΜΗ ΕΠΑΛΗΘΕΥΜΕΝΑ:** πλήρες .NET 11 host publish με όλα τα plugins ταυτόχρονα, installer Inno Setup 7, αναβάθμιση/απεγκατάσταση, Defender, performance/idle/GPU/DPI/SSD soak και φυσική χρήση Windows 10/11. Το παρόν περιβάλλον δεν διαθέτει το .NET SDK/Windows runtime για αυτούς τους ελέγχους.

**Ο κανόνας παράδοσης:** ο installer 2.8.0.2500 παραδίδεται ως τελικός μόνο μετά από επιτυχή ολοκλήρωση των παραπάνω υποχρεωτικών ελέγχων. Το τρέχον αρχείο είναι *source preview*. Τα χωριστά πακέτα Crawler και Χρονικολογίου προκύπτουν από επιτυχημένα εστιασμένα Windows builds, όχι ακόμη από ολοκληρωμένο διαλειτουργικό έλεγχο με το .NET 11 host.

## Διορθώσεις της τρέχουσας παρτίδας ελέγχων Χρονικολογίου

Το WinForms audit ανέδειξε ένα ήδη υπάρχον native-handle πρόβλημα σε πολλές διαδοχικές δημιουργίες/καταστροφές παραθύρων. Προστέθηκαν διορθώσεις στο `CalendarPanel` (πλέον επαναχρησιμοποιούνται οι bold γραμματοσειρές, απελευθερώνονται ToolTip/εικόνες/menus), και στο `MainWindow.Dispose` (αποδεσμεύεται το κοινό ημερολόγιο ακόμη κι όταν είχε αποσυνδεθεί από το TabsLayout). Προστέθηκαν tests lifecycle και χρήσης fonts. Απαιτείται πλήρες green regression CI για να θεωρηθεί κλειστό το θέμα.

## Πρόσθετη πύλη έκδοσης

Προστέθηκε το `tools/check-plugin-dll-versions.ps1`: αναλύει τα πραγματικά μεταγλωττισμένα DLL και ελέγχει το `AssemblyVersion` και για τα δύο plugins (1.0.0.0), το συμβόλαιο (0.8.0.0), το Opsis.UI (0.1.0.0), καθώς και το `FileVersion` των plugins. Εκτελείται στο Windows plugin-compatibility CI. Επίσης, στο `windows-audit.yml` η δημοσίευση του installer ως CI artifact μεταφέρθηκε μετά τον πλήρη UI/resource audit και γίνεται μόνο εφόσον ο έλεγχος περάσει. Αυτό **δεν** υποκαθιστά τον ανεξάρτητο E2E έλεγχο installer ή την πραγματική δοκιμή συνύπαρξης plugins.
