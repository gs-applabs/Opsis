# Βελτιώσεις Weather Wallpaper — αυτόνομο υποέργο

## Σκοπός
Συνεχίζουμε **αποκλειστικά** την αναβάθμιση οπτικών του Weather Wallpaper σε 2.5D και 3D, χωρίς αλλαγές στα Start/Taskbar, στα plugins ή στην κύρια αρχιτεκτονική Opsis.

## Κώδικας και βάση
- Repo: `gs-applabs/Opsis`
- Branch: `feat/weather-wallpaper-25d-3d`
- Base: `feat/p1-shell-customization-2.8.0.2510`, η οποία πατά στο P0 branch της επίσημης `release/2.8.0.2510`.
- Έκδοση βάσης: 2.8.0.2510 · .NET 11 Windows, Weather engine σε WebView2.
- Πρωτεύοντα αρχεία:
  - `src/Widgets/wallpaper/weather-spatial.js`: ανεξάρτητο WebGL2 pipeline για πραγματική 3D perspective προβολή βροχής/χιονιού, fallback όταν δεν υπάρχει WebGL2.
  - `src/Widgets/wallpaper/weather.js`: ανεξάρτητη οπτική μετατόπιση του μακρινού ουρανού, το φωτογραφικό/χερσαίο foreground μένει σταθερό (2.5D).
  - `src/Widgets/wallpaper/index.html`: φόρτωση renderer, έλεγχος μέσω cfg, το overlay ώρας/πόλης πάνω από τα 3D layers.
  - `src/Widgets/WallpaperWindow.cs`: `WeatherSpatialMode` και `SetWeatherSpatialMode` με τιμές `Off`, `2.5D`, `3D`.
  - `src/UI/MainWindow.cs`: επιλογές στην υπάρχουσα ενότητα Weather Wallpaper.
- Δεν υπάρχουν τρίτες WebGL/3D βιβλιοθήκες ούτε online runtime dependencies.
- Default παραμένει `Off`. Στο Low Power/Battery απενεργοποιείται το GPU 3D layer. Το reduced-motion κλείνει το 3D.

## Τεχνική ερμηνεία
- **2.5D**: πραγματικό compositing διαφορετικών επιπέδων βάθους μέσα στον υπάρχοντα Canvas renderer με ήπια κίνηση ουρανού.
- **3D**: πραγματική 3D projection στο WebGL2 για τα σωματίδια ατμόσφαιρας (x/y/z + perspective). Δεν ισοδυναμεί ακόμη με πλήρως 3D φωτογραμμετρική ανακατασκευή μνημείων/πόλεων/εδάφους.
- Τα τοπόσημα της Gallery παραμένουν ως έχουν. Πραγματικά 3D models απαιτούν ξεχωριστά assets, επαληθευμένη γεωμετρία/τοποθεσία και νέο pipeline glTF/WebGL.

## Συμβατότητα / εκκρεμότητες
- Εκτελούνται δοκιμές `node --check`, Node fallback tests και Windows .NET 11 build/publish σε GitHub Actions.
- Για τελική αποδοχή χρειάζονται οπτικά screenshots με Windows WebView2 και ρεαλιστικό Weather mode: Αθήνα/Ακρόπολη, Πάτρα/φάρος και γέφυρα, Θεσσαλονίκη/Λευκός Πύργος.
- Ελέγχουμε ειδικά πολυοθονικό setup, DPI 100–200%, GPU/CPU, VRAM, fullscreen pause, WebGL context loss, battery saver και χρονική συνέπεια καιρού/ήλιου.
- Η 3D ατμόσφαιρα είναι ανεξάρτητη από τον πυρήνα· μελλοντικά θα γίνει weather renderer contribution στην ενιαία Gallery του Opsis.
- Δεν έχει γίνει merge/release στο επίσημο `release/2.8.0.2510`.

## Μήνυμα για έναρξη της ξεχωριστής συνομιλίας
«Συνέχισε το υποέργο **Βελτιώσεις Weather Wallpaper** από το GitHub branch `feat/weather-wallpaper-25d-3d` του `gs-applabs/Opsis`. Χρησιμοποίησε ως μοναδικό τεχνικό handoff αυτό το αρχείο. Ολοκλήρωσε τις 2.5D/3D γραφικές βελτιώσεις με αυθεντικά ελληνικά landmarks και σωστή χωρική γεωμετρία, ποιότητα performance tiers, screenshot/UI tests σε Windows VM, και διασύνδεση με την ενιαία Gallery/plugin catalog. Μην πειράξεις Start/Taskbar/Explorer. Μην δηλώσεις τελική πιστοποίηση χωρίς πραγματική δοκιμή WebView2 GPU.»

## Συνέχεια υλοποίησης — τοπία και μνημεία

Το weather-landscape.js κατασκευάζει τοπικό πλέγμα τριγώνων για την επιλεγμένη καταχώριση. Ο κοινός επιλογέας της Gallery και των ρυθμίσεων Weather χρησιμοποιεί την υπάρχουσα αποθηκευμένη τιμή. Η προοπτική, το βάθος, ο φωτισμός, η χιονοκάλυψη και η ομίχλη αποδίδονται στο WebGL2. Η σκηνή συντίθεται πριν από τα εφέ στο τζάμι και το κείμενο. Χωρίς ενεργό 3D παραμένει η προηγούμενη διαδρομή σχεδίασης.

Τα μοντέλα Αθήνας, Θεσσαλονίκης, Φάρου Πάτρας, Ρίου–Αντιρρίου και Μπουρτζίου είναι πρωτότυπες σχηματοποιημένες αναπαραστάσεις. Τα υπόλοιπα τοπία χρησιμοποιούν ανάγλυφο και αρχιτεκτονικά μοτίβα του καταλόγου και αναπαραγώγιμη σύνθεση ανά ID. Οι συντεταγμένες επιλέγουν τόπο. Η γεωμετρία δεν έχει τοπογραφική ή φωτογραμμετρική πιστοποίηση. Ο Παρθενώνας αποδίδεται ως αρχιτεκτονική αναπαράσταση με οροφή.

Αναφορές μορφολογίας:
- https://acropolisrestoration.ysma.gr/wp-content/uploads/2025/05/06_PARTHENWN_EN.pdf
- https://www.nafpaktos.gr/en/rio-antirrio-bridge-en

Η δοκιμή Node κατασκευάζει κάθε σκηνή και στις δύο ποιότητες. Το WebViewSmoke χρησιμοποιεί το δημοσιευμένο wallpaper σε WinForms/WebView2, ελέγχει pixels του WebGL foreground με καθαρό καιρό, καταγράφει πέντε PNG και ελέγχει την αποδέσμευση κατά την επιστροφή σε 2.5D και 2D. Η πραγματική GPU, το DPI και η πολυοθονική χρήση χρειάζονται ξεχωριστή μέτρηση.
