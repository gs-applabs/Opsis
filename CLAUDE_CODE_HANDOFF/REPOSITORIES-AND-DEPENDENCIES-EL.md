# Αποθετήρια και συμβάσεις OPSIS

**GitHub repo:** https://github.com/gs-applabs/Opsis (`main`). Το snapshot R10 προήλθε από ανεξάρτητο ZIP, όχι από αυτό το branch. Δεν έχει δημοσιευθεί αυτόματα στο `main` από την παρούσα παράδοση.

| Εφαρμογή | Repository | Τύπος ενσωμάτωσης | Ειδικό σημείο |
|---|---|---|---|
| OPSIS Desktop Customization | `gs-applabs/Opsis` | Windows WinForms host | .NET 11, έκδοση 2.8.0.2500 στο `Version.props` |
| Opsis.UI | `gs-applabs/Opsis.UI` (**ιδιωτικό**) | Κοινή WinForms assembly | Ιδιαίτερο assembly ABI (μην αλλάξεις αυτομάτως σε 1.0.0.0) |
| Opsis News Crawler | `gs-applabs/OpsisNewsCrawler` | In-process plugin + standalone | Plugin DLL `1.0.0.0`, κοινή ABI με host |
| Χρονικολόγιο | `gs-applabs/san-shmera` | In-process plugin + standalone | Plugin DLL `1.0.0.0`, calendar replacement, `san-simera` feed |
| Opsis Media Suite | `gs-applabs/OpsisMediaSuite` | Ξεχωριστή εφαρμογή Electron | Εντοπισμός, εκκίνηση, UI/installer συμβατότητα, **όχι** in-process DLL |
| Opsis Appearance | `gs-applabs/OpsisAppearance` | Επιπλέον plugin | Δοκιμή με τρέχον κοινό Opsis.UI και θέμα host |
| MediaPlayer Pro | `gs-applabs/OpsisMediaPlayerPro` | Ξεχωριστή εφαρμογή | Προαιρετικός έλεγχος οικογένειας |

Ο πηγαίος κώδικας των άλλων repositories **δεν αντιγράφεται** στο παρόν πακέτο· η παρούσα παράδοση περιέχει **όλο το R10 source του κύριου OPSIS** και τις ήδη συμπεριλαμβανόμενες βιβλιοθήκες `lib`. Για end-to-end έλεγχο χρειάζονται πραγματικά checkouts των εξωτερικών repositories και δικαιώματα πρόσβασης στο ιδιωτικό Opsis.UI.

## Σχετικές εργασίες / branches
- News Crawler: branch `release/plugin-dll-1.0.0.0-opsis-280` · PR https://github.com/gs-applabs/OpsisNewsCrawler/pull/2
- Χρονικολόγιο: branch `release/plugin-dll-1.0.0.0-opsis-280` · PR https://github.com/gs-applabs/san-shmera/pull/1
- Opsis.UI CI: PR https://github.com/gs-applabs/Opsis.UI/pull/1

Οι παραπάνω refs αποτελούν τεκμηριωμένα σημεία εκκίνησης από τις προηγούμενες συνεδρίες, **όχι διαβεβαίωση ότι είναι ακόμη οι τελευταίες εκδόσεις ή ότι έχουν συγχωνευθεί**. Έλεγξε την τρέχουσα κατάσταση στο GitHub πριν από merge.
