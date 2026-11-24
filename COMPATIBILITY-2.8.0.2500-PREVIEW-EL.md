# OPSIS 2.8.0.2500 — πύλες ελέγχου συμβατότητας (PREVIEW, όχι release)

Ημερομηνία: 23/11/2026. Αρχική βάση: ολοκληρωμένο source ZIP 2.7.7.2403.

## Εφαρμοσμένες βελτιώσεις στο source

1. `suite/apps.json`: προσθήκη Χρονικολόγιου (`san-shmera`), ορθών paths εγκαταστάτη/εκτελέσιμου, δίγλωσσων περιγραφών και ενσωμάτωσης `calendar` + «Σαν σήμερα». Τα Opsis News Crawler και Media Suite διατηρούνται.
2. `src/Services/PluginHost.cs`: κοινό host assembly για `Opsis.UI` και `OpsisNewsCrawler.Plugin.Abstractions`, ώστε να μην δημιουργούνται πολλαπλές ανεξάρτητες στατικές καταστάσεις θέματος/χρωμάτων/Glass ανά AssemblyLoadContext. Έλεγχος της απαιτούμενης έκδοσης assemblies.
3. `src/Services/DesktopDiagnostics.cs`: η υπάρχουσα δοκιμή `--test-plugin-widget` αναφέρει επιπλέον τα πραγματικά loaded plugin IDs και τα registered feed keys, για έλεγχο ότι φορτώθηκαν και οι δύο βιβλιοθήκες (Crawler + Χρονικολόγιο), έγινε αντικατάσταση ημερολογίου και καταχωρίστηκε η ροή `san-simera`.
4. `src/Services/MediaSuiteDetector.cs`: απόρριψη μη απόλυτων / κενών root paths όταν λείπουν μεταβλητές περιβάλλοντος, ώστε να αποφεύγονται λανθασμένες ανιχνεύσεις.
5. `.github/workflows/plugin-compatibility.yml`: νέος Windows-2025 CI release-gate με build και tests για OPSIS/.NET11, News Crawler/.NET10, Χρονικολόγιο/.NET10, πραγματική ταυτόχρονη φόρτωση plugins σε isolated AppData, widget/feed checks, ABI assemblies, και ανεξάρτητα tests/self-test Electron Media Suite.
6. `tests/PluginCompatibility.test.cjs`: 6 νέοι έλεγχοι καταλόγου, συστήματος plugins, contract και συμβατότητας μεταδεδομένων (23/23 JavaScript tests μαζί με τα παλαιότερα 17).
7. `Opsis.csproj`/`installer.iss`/`windows-audit.yml`: στόχος έκδοσης 2.8.0.2500 με `InformationalVersion=2.8.0.2500-compatibility-preview` μέχρι την τελική επικύρωση.

## Διασυνδέσεις και εκδόσεις που εξετάστηκαν

| Υποσύστημα | Repo | Τρέχουσα ταυτότητα | Μορφή ενσωμάτωσης |
|---|---|---|---|
| Κοινή διεπαφή Opsis.UI | `gs-applabs/Opsis.UI` (private) | 0.4.5, AssemblyVersion 0.1.0.0 | DLL στο host και plugins |
| Crawler | `gs-applabs/OpsisNewsCrawler` | 0.24.1 | .NET plugin + αυτόνομο app |
| Χρονικολόγιο | `gs-applabs/san-shmera` | 0.9.0 | .NET plugin, calendar widget, «Σαν σήμερα» feed |
| Media Suite | `gs-applabs/OpsisMediaSuite` | 2.4.1 | ανεξάρτητο Electron app/installer, ΟΧΙ DLL plugin |

Το Χρονικολόγιο και το News Crawler χρησιμοποιούν το κοινό `IOpsisNewsCrawlerPlugin` contract. Το Χρονικολόγιο καταχωρίζει τη ροή `san-simera` μέσω `PluginFeeds` και αντικαθιστά το builtin widget `calendar`. Προσοχή: οι παλαιότερες εκδόσεις των embedded DLL στα plugin projects έχουν ίδια ABI assembly identity αλλά ενδέχεται να διαφέρουν ως προς τα API· το E2E load είναι απαραίτητο.

## Επαληθευμένα / Μη επαληθευμένα

**PASS τοπικά:** `node --test` 23/23· JSON catalogue unique IDs και πλήρη entries· XML `.csproj` parse· workflow YAML parse· αρχεία source διαθέσιμα.

**ΜΗ ΕΠΑΛΗΘΕΥΜΕΝΑ:** `dotnet publish` .NET11 σε Windows, δυναμικό φορτίο DLL plugins, καταχώριση feed στο runtime, συμβατότητα DLL APIs, πλήρη Opsis.UI tests, Media Suite renderer tests, installer Inno 7, upgrade/uninstall, Defender, security scan, πολυοθονικές δοκιμές, Windows 10/11 χρήστη.

Το draft PR `https://github.com/gs-applabs/Opsis.UI/pull/1` προσθέτει CI matrix Windows 2022/2025 και SDK .NET10/11 και διορθώσεις για `Debouncer` / `SettingsStore.Watch`. Οι πρώτες εκτελέσεις αποκάλυψαν αστάθεια των ασύγχρονων tests· μέχρι το πέρασμα ΟΛΩΝ των jobs δεν είναι πράσινη πύλη.

## Release gate πριν από την τελική 2.8.0.2500

1. Ολοκλήρωση όλων των συμφωνημένων features/100 proposals χωρίς παλινδρομήσεις.
2. Επιτυχή `Opsis.UI` tests σε όλες τις προβλεπόμενες εκδόσεις Windows/SDK και εξέταση gallery εικόνων (και των themes, DPI).
3. Επιτυχή C# build/publish και self-tests + JavaScript/Weather tests του OPSIS.
4. Ταυτόχρονο E2E Crawler και Χρονικολόγιο σε **καθαρό runner**, περιλαμβανομένης σύνδεσης `PluginFeeds`, γραφικών θεμάτων, απενεργοποίησης/επανενεργοποίησης.
5. Media Suite tests/selftest, ανίχνευση εγκατάστασης, έλεγχος launch/close χωρίς δεύτερη διεργασία player.
6. Installer E2E σε Windows: παλαιότερη έκδοση → 2.8 upgrade, διατήρηση προφίλ/plugins, uninstall, rollback από αποτυχία, checksum, security.
7. Performance/idle soak, SSD write frequency, GPU/CPU, μνήμη, πόροι GDI/USER, 100%/150%/200% DPI, πολυοθονικά, Windows 10 και 11.
8. Security checks: NuGet/NPM package vulnerabilities, gitleaks, Microsoft Defender, μη υπογεγραμμένο/υπογεγραμμένο binary status, έλεγχος ακεραιότητας assets.
9. Τελικό installer EXE μόνο μετά από πράσινες πύλες, manifest SHA-256 και ενημερωμένες σημειώσεις έκδοσης.

**Το παρόν αποτελεί σημείο επανέναρξης υλοποίησης και ΔΕΝ είναι τελική δημοσιευμένη 2.8.0.2500.**

## Νέο αίτημα: έκδοση DLL plugins 1.0.0.0

- **OpsisNewsCrawler.OpsisPlugin.dll**: AssemblyVersion + FileVersion **1.0.0.0** σε draft PR στο `gs-applabs/OpsisNewsCrawler` (#2).
- **Chronikologio.OpsisPlugin.dll**: AssemblyVersion + FileVersion **1.0.0.0** σε draft PR στο `gs-applabs/san-shmera` (#1).
- Το πεδίο `Version` που λαμβάνει το OPSIS παραμένει η έκδοση **προϊόντος** (Crawler 0.24.1, Χρονικολόγιο 0.9.0), ώστε ο μηχανισμός προτίμησης της νεότερης εγκατάστασης να συνεχίσει να λειτουργεί. Στο diagnostic `pluginDlls` καταγράφεται ξεχωριστά η ταυτότητα DLL `1.0.0.0`.
- **ΔΕΝ** αλλάζει αυθαίρετα το `OpsisNewsCrawler.Plugin.Abstractions` (κοινό contract ABI 0.8.0.0) ούτε το `Opsis.UI` (κοινή βιβλιοθήκη controls, άλλο DLL). Η αλλαγή αυτών θα απαιτούσε συντονισμένη αντικατάσταση των binaries σε όλη την οικογένεια, διαφορετικά προκαλεί `FileLoadException`.
- Το E2E `.github/workflows/plugin-compatibility.yml` ελέγχει τα δύο πραγματικά DLL 1.0.0.0, το κοινό contract, την αναφορά version από runtime, την ταυτόχρονη φόρτωση, το calendar widget και το `san-simera` feed. Προσωρινά χρησιμοποιούνται τα δύο staging branches μέχρι να συγχωνευθούν τα PRs.
- Αυτή είναι **πηγαία προεπισκόπηση**. Τα νέα binaries θα είναι διαθέσιμα μόνο μετά από επιτυχή Windows publish και διανομή. Δεν έχει δημιουργηθεί τελικός OPSIS installer.

## Πρόσθετες βελτιώσεις (επόμενος κύκλος)

- `PluginUiStyle.Publish`: αποφεύγονται οι αμετάβλητες εγγραφές JSON σε κάθε εκκίνηση, με ασφαλή αντικατάσταση αλλοιωμένων δεδομένων.
- `DesktopDiagnostics`: `versions` προϊόντος και `pluginDlls` AssemblyVersion ξεχωριστά.
- Windows PRs σε Crawler / Χρονικολόγιο με DLL version 1.0.0.0. Η πρώτη δοκιμή Χρονικολογίου βρήκε σφάλμα WinForms/GDI· προστέθηκαν πραγματικές διορθώσεις στο αποθετήριο, νέα tests και serial execution.
- Τοπικές δοκιμές Node: **27/27 PASS**.


- Ενημερώθηκε ο κατάλογος Media Suite με τις λειτουργίες της v2.4.1 (video/audio, FFmpeg μετατροπές, φωτογραφίες, 20 γλώσσες).
