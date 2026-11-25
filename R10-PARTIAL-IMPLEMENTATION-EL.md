# OPSIS Desktop Customization 2.8.0.2500 — R10

**23 Νοεμβρίου 2026 · Επεξεργασμένη βάση R9-S4.**

## Τι βελτιώθηκε στον πραγματικό κώδικα

| # | Παρέμβαση | Κατάσταση |
|---:|---|---|
| 14 | Στοιχεία εκδότη και πραγματικός έλεγχος αρχείου Authenticode μέσω WinVerifyTrust, με σαφή ένδειξη ότι δεν ελέγχεται ανάκληση πιστοποιητικού offline | Source complete · Windows QA pending |
| 49 | Παράθυρα με owner, τυπικοί διάλογοι #32770, tool windows, no-activate popups εξαιρούνται από αυτόματη διάταξη | Source complete · Windows QA pending |
| 50 | Προεπισκόπηση και εφαρμογή του ακριβώς εγκεκριμένου πλάνου διάταξης με έλεγχο αν άλλαξαν τα παράθυρα στο μεσοδιάστημα και Undo | Source complete · Windows QA pending |
| 55 | Επιλογή ανάμεσα σε πολλαπλά DeskContainers που αναφέρουν το ίδιο αρχείο | Source complete · Windows QA pending |
| 73 | Ατομική συχνότητα ενημέρωσης native widgets και SDK web widgets (state, refresh message, αποθήκευση, local-time και day-progress) | Source complete · 2 Node tests passed · Windows QA pending |
| 78 | Απομονωμένο WASAPI capture για το μουσικό overlay όταν επιλεγεί μικρόφωνο/line-in· το κοινό loopback παραμένει ανεπηρέαστο | Source complete · Windows audio QA pending |
| 85 | UI Automation polite live region για αναγγελία αλλαγής προφίλ, επιπλέον της status bar | Source complete · NVDA/Narrator QA pending |

## Σημεία που βελτιώθηκαν, αλλά παραμένουν μερικά

| # | Πραγματική εκκρεμότητα |
|---:|---|
| 12 | Το in-process DLL δεν μπορεί να δεσμευτεί σε δικαιώματα δίσκου/δικτύου/ήχου μόνο μέσω manifest. Χρειάζεται πραγματική απομόνωση σε άλλη διεργασία και RPC broker. |
| 17 | Υπάρχει χειροκίνητο στιγμιότυπο/undo, όχι αυτόματη λήψη πριν από κάθε μαζικό Explorer drag. |
| 20 | Ο crash/power-loss harness δεν εκτελέστηκε σε Windows. |
| 24 | Η τριπλή προεπισκόπηση προβάλλει πλέον την πραγματική επιλεγμένη φωτογραφία όταν υπάρχει, αλλά οι τρεις σκηνές δεν αποτελούν ανεξάρτητους ζωντανούς weather simulators. |
| 32 | Η επιλογή πανοραμικού ζεύγους ελέγχει οριζόντια γειτνίαση, αλλά δεν έχει γίνει πραγματικό multi-monitor/DPI/docking QA. |
| 40 | Προσωπικά στοιχεία OPSIS και οι ιδιωτικές ειδοποιήσεις του ίδιου του OPSIS αποκρύπτονται στην παρουσίαση. Δεν μπορεί να ελεγχθεί το σύνολο των ειδοποιήσεων τρίτων εφαρμογών ή του λειτουργικού. |
| 97 | Ο κατάλογος και ο έλεγχος metadata υπάρχουν, αλλά τα plugin DLL 1.0.0.0 δεν έχουν δοκιμαστεί με πλήρες Windows host E2E. |

## Release gates

- **Node.js:** 39/39 tests επιτυχή (συμπεριλαμβάνονται δύο νέα tests συχνότητας web widgets).
- **.NET 11 Windows build:** ΜΗ ΕΚΤΕΛΕΣΜΕΝΟ στο διαθέσιμο Linux περιβάλλον (λείπει το SDK).
- **WASAPI / UI Automation / multi-monitor QA:** ΜΗ ΕΚΤΕΛΕΣΜΕΝΑ.
- **Inno Setup 7 / installer / upgrade / uninstall:** ΜΗ ΕΚΤΕΛΕΣΜΕΝΑ.
- **Plugin contracts:** Το 1.0.0.0 αφορά τα δύο plugin DLLs (News Crawler και Χρονικολόγιο), όχι αυθαίρετη αλλαγή AssemblyVersion των κοινών Opsis.UI και Plugin.Abstractions.

Η R10 είναι **checkpoint πηγαίου κώδικα**, όχι πιστοποιημένη release έκδοση.
