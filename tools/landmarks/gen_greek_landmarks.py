"""
Generates the Greek prefecture-capital landmark data used by the wallpaper:
  - src/Widgets/wallpaper/landmarks.js  (silhouette list, info table, scenes; rewritten from the template in git HEAD-independent form: run once on the 2.6.0-beta file)
  - src/Widgets/LandmarkCatalog.cs      (choice list for the Wallpaper page)
Run from the repo root:  python -I tools/landmarks/gen_greek_landmarks.py
The silhouettes are OWN drawings (templates in greek_templates.js); photos (sprites) are listed in SPRITE_IDS and docs/LANDMARK-IMAGES.json.
"""
import re, io, os, json, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
JS = os.path.join(ROOT, 'src', 'Widgets', 'wallpaper', 'landmarks.js')
CS = os.path.join(ROOT, 'src', 'Widgets', 'LandmarkCatalog.cs')
TPL = os.path.join(ROOT, 'tools', 'landmarks', 'greek_templates.js')

REG = ['Attica', 'CentralGreece', 'WesternGreece', 'Peloponnese', 'Epirus', 'Thessaly', 'WesternMacedonia', 'CentralMacedonia', 'EastMacedoniaThrace', 'IonianIslands', 'NorthAegean', 'SouthAegean', 'Crete']
# id, capital (en), capital (el), region, landmark (en), landmark (el), lat, lon, material, scene, ground, drawing expression (None = photo sprite / existing drawing)
D = [
 ('parthenon', 'Athens', 'Αθήνα', 'Attica', 'Parthenon, Athens', 'Παρθενώνας, Αθήνα', 37.9715, 23.7267, 'marble', 'photo', None, None),
 ('piraeus', 'Piraeus', 'Πειραιάς', 'Attica', 'Municipal Theatre, Piraeus', 'Δημοτικό Θέατρο Πειραιά', 37.9420, 23.6470, 'marble', 'land', 'rock', 'neoclassic({ wide: 0.8, h: 0.42, cols: 6 })'),
 ('chalkida', 'Chalkida', 'Χαλκίδα', 'CentralGreece', 'Euripus Bridge, Chalkida', 'Γέφυρα του Ευρίπου, Χαλκίδα', 38.4630, 23.5960, 'iron', 'sea', None, 'euripus()'),
 ('karpenisi', 'Karpenisi', 'Καρπενήσι', 'CentralGreece', 'Stone bridge, Karpenisi (Evrytania)', 'Πέτρινο γεφύρι, Καρπενήσι (Ευρυτανία)', 38.9140, 21.7960, 'stone', 'land', 'grass', 'bridge(2, 0.24, 0.2, 0.75)'),
 ('lamia', 'Lamia', 'Λαμία', 'CentralGreece', 'Castle of Lamia', 'Κάστρο Λαμίας', 38.9000, 22.4330, 'stone', 'land', 'rock', 'fort({ hh: 0.28, x: 0.8, towers: [[-0.8, 0.18, 0.3], [0.8, 0.18, 0.3], [0, 0.16, 0.4]] })'),
 ('amfissa', 'Amfissa', 'Άμφισσα', 'CentralGreece', 'Castle of Amfissa (Salona)', 'Κάστρο Άμφισσας (Σάλωνα)', 38.5300, 22.3790, 'stone', 'land', 'rock', 'fort({ hh: 0.38, x: 0.6, towers: [[-0.6, 0.2, 0.3], [0.6, 0.2, 0.3]], keepAt: [0, 0.24, 0.34] })'),
 ('livadeia', 'Livadeia', 'Λιβαδειά', 'CentralGreece', 'Castle of Livadeia', 'Κάστρο Λιβαδειάς', 38.4350, 22.8780, 'stone', 'land', 'rock', 'fort({ hh: 0.2, x: 0.7, towers: [[-0.7, 0.22, 0.5], [0.7, 0.18, 0.3]], gate: null })'),
 ('agrinio', 'Agrinio', 'Αγρίνιο', 'WesternGreece', 'Agios Konstantinos church, Agrinio', 'Ι.Ν. Αγίου Κωνσταντίνου, Αγρίνιο', 38.6210, 21.4070, 'white', 'land', 'grass', 'church({ bell: 0.35, domeR: 0.18 })'),
 ('patras', 'Patras', 'Πάτρα', 'WesternGreece', 'Lighthouse of Patras', 'Φάρος της Πάτρας', 38.2467, 21.7295, 'stone', 'sea', None, None),
 ('rioantirrio', 'Patras (Rio)', 'Πάτρα (Ρίο)', 'WesternGreece', 'Rio-Antirrio Bridge', 'Γέφυρα Ρίου-Αντιρρίου', 38.3200, 21.7740, 'white', 'sea', None, None),
 ('pyrgos', 'Pyrgos', 'Πύργος', 'WesternGreece', 'Temple of Zeus, Olympia (Ilia)', 'Ναός του Δία, Ολυμπία (Ηλεία)', 37.6750, 21.4410, 'marble', 'land', 'grass', 'ruinsTemple([0.62, 0.62, 0.45, 0.62, 0.3, 0.62, 0.55, 0.2])'),
 ('nafplio', 'Nafplio', 'Ναύπλιο', 'Peloponnese', 'Bourtzi, Nafplio', 'Μπούρτζι, Ναύπλιο', 37.5640, 22.8000, 'stone', 'sea', None, None),
 ('tripoli', 'Tripoli', 'Τρίπολη', 'Peloponnese', 'Agios Vasileios church, Tripoli', 'Ι.Ν. Αγίου Βασιλείου, Τρίπολη', 37.5100, 22.3730, 'stone', 'land', 'grass', 'church({ bell: 0.4, domeR: 0.17, w: 0.45 })'),
 ('corinth', 'Corinth', 'Κόρινθος', 'Peloponnese', 'Acrocorinth', 'Ακροκόρινθος', 37.9060, 22.8780, 'stone', 'land', 'rock', 'fort({ hh: 0.55, x: 0.8, wallTop: 0.14, towers: [[-0.8, 0.16, 0.26], [0.8, 0.16, 0.26], [0, 0.14, 0.22]], gate: null })'),
 ('sparta', 'Sparta', 'Σπάρτη', 'Peloponnese', 'Statue of Leonidas, Sparta', 'Άγαλμα Λεωνίδα, Σπάρτη', 37.0740, 22.4300, 'marble', 'land', 'grass', 'statueLeonidas()'),
 ('kalamata', 'Kalamata', 'Καλαμάτα', 'Peloponnese', 'Castle of Kalamata', 'Κάστρο Καλαμάτας', 37.0390, 22.1140, 'stone', 'land', 'rock', 'fort({ hh: 0.3, x: 0.75, towers: [[-0.75, 0.2, 0.3], [0.75, 0.2, 0.3], [0, 0.16, 0.36]] })'),
 ('arta', 'Arta', 'Άρτα', 'Epirus', 'Bridge of Arta', 'Γεφύρι της Άρτας', 39.1590, 20.9850, 'stone', 'land', 'grass', 'bridge(4, 0.3, 0.14, 0.95)'),
 ('igoumenitsa', 'Igoumenitsa', 'Ηγουμενίτσα', 'Epirus', 'Harbour lighthouse, Igoumenitsa', 'Φάρος λιμανιού, Ηγουμενίτσα', 39.5000, 20.2660, 'white', 'sea', None, 'lighthouse({ w: 0.13, tw: 0.09, h: 0.6, base: 0.08, baseW: 0.7 })'),
 ('ioannina', 'Ioannina', 'Ιωάννινα', 'Epirus', 'Aslan Pasha Mosque, Ioannina Castle', 'Τζαμί Ασλάν Πασά, Κάστρο Ιωαννίνων', 39.6650, 20.8530, 'stone', 'land', 'grass', 'mosque({ minaretX: 0.82 })'),
 ('preveza', 'Preveza', 'Πρέβεζα', 'Epirus', 'Clock Tower, Preveza', 'Ρολόι Πρέβεζας', 38.9580, 20.7510, 'stone', 'sea', None, 'clockTower(0.22, 0.74)'),
 ('karditsa', 'Karditsa', 'Καρδίτσα', 'Thessaly', 'Lake Plastiras dam (Karditsa)', 'Λίμνη Πλαστήρα, φράγμα (Καρδίτσα)', 39.3650, 21.9210, 'stone', 'land', 'grass', 'plastiras()'),
 ('larisa', 'Larissa', 'Λάρισα', 'Thessaly', 'Ancient Theatre of Larissa', 'Αρχαίο Θέατρο Λάρισας', 39.6390, 22.4190, 'marble', 'land', 'grass', 'theatre()'),
 ('volos', 'Volos', 'Βόλος', 'Thessaly', 'Argo, the Argonauts ship, Volos', 'Αργώ, το πλοίο των Αργοναυτών, Βόλος', 39.3600, 22.9420, 'stone', 'sea', None, 'argo()'),
 ('trikala', 'Trikala', 'Τρίκαλα', 'Thessaly', 'Castle of Trikala', 'Κάστρο Τρικάλων', 39.5550, 21.7680, 'stone', 'land', 'grass', 'fort({ hh: 0.25, x: 0.8, wallTop: 0.22, towers: [[-0.8, 0.18, 0.32], [0.8, 0.18, 0.32], [-0.3, 0.14, 0.28]], keepAt: [0.35, 0.2, 0.3] })'),
 ('grevena', 'Grevena', 'Γρεβενά', 'WesternMacedonia', 'Stone bridge, Grevena', 'Πέτρινο γεφύρι, Γρεβενά', 40.0840, 21.4260, 'stone', 'land', 'grass', 'bridge(3, 0.3, 0.05, 0.8)'),
 ('kastoria', 'Kastoria', 'Καστοριά', 'WesternMacedonia', 'Panagia Koubelidiki, Kastoria', 'Παναγία Κουμπελίδικη, Καστοριά', 40.5190, 21.2680, 'stone', 'land', 'grass', 'church({ domeR: 0.26, w: 0.42, h: 0.28 })'),
 ('kozani', 'Kozani', 'Κοζάνη', 'WesternMacedonia', 'Agios Nikolaos church, Kozani', 'Ι.Ν. Αγίου Νικολάου, Κοζάνη', 40.3000, 21.7890, 'stone', 'land', 'grass', 'church({ bell: 0.4, domeR: 0.16, w: 0.45, h: 0.34 })'),
 ('florina', 'Florina', 'Φλώρινα', 'WesternMacedonia', 'Stone bridge, Florina', 'Πέτρινο γεφύρι, Φλώρινα', 40.7810, 21.4090, 'stone', 'land', 'grass', 'bridge(3, 0.22, 0.06, 0.8)'),
 ('veria', 'Veria', 'Βέροια', 'CentralMacedonia', 'Clock Tower (Roloi), Veria', 'Ρολόι, Βέροια', 40.5240, 22.2020, 'stone', 'land', 'grass', 'clockTower(0.2, 0.8)'),
 ('whitetower', 'Thessaloniki', 'Θεσσαλονίκη', 'CentralMacedonia', 'White Tower, Thessaloniki', 'Λευκός Πύργος, Θεσσαλονίκη', 40.6264, 22.9484, 'white', 'sea', None, None),
 ('kilkis', 'Kilkis', 'Κιλκίς', 'CentralMacedonia', 'Kilkis-Lachanas memorial', 'Μνημείο Κιλκίς-Λαχανά', 40.9930, 22.8740, 'marble', 'land', 'grass', 'kilkis()'),
 ('edessa', 'Edessa', 'Έδεσσα', 'CentralMacedonia', 'Waterfalls of Edessa', 'Καταρράκτες Έδεσσας', 40.8020, 22.0460, 'rock', 'land', 'grass', 'falls()'),
 ('katerini', 'Katerini', 'Κατερίνη', 'CentralMacedonia', 'Mount Olympus (Pieria)', 'Όλυμπος (Πιερία)', 40.2720, 22.5020, 'rock', 'land', 'grass', 'mountain([[-0.25, 0.9, 0.55], [0.2, 1.0, 0.5], [0.68, 0.7, 0.4], [-0.75, 0.55, 0.35]])'),
 ('serres', 'Serres', 'Σέρρες', 'CentralMacedonia', 'Bezesteni, Serres', 'Μπεζεστένι, Σέρρες', 41.0850, 23.5480, 'stone', 'land', 'grass', '[M(rect(-0.8, 0, 0.8, 0.28)), ...[-0.6, -0.3, 0, 0.3, 0.6].map(x => M(dome(x, 0.28, 0.15, 0.13))), ...arcade(-0.8, 0.8, 0, 0.2, 10, 0.5)]'),
 ('polygyros', 'Polygyros', 'Πολύγυρος', 'CentralMacedonia', 'Mount Athos (Chalkidiki)', 'Άθως (Χαλκιδική)', 40.3770, 23.4430, 'rock', 'land', 'grass', 'mountain([[0.05, 1.0, 0.7], [-0.6, 0.3, 0.5], [0.7, 0.28, 0.5]])'),
 ('drama', 'Drama', 'Δράμα', 'EastMacedoniaThrace', 'Agia Sophia church, Drama', 'Ι.Ν. Αγίας Σοφίας, Δράμα', 41.1500, 24.1470, 'stone', 'land', 'grass', 'church({ domeR: 0.22 })'),
 ('kavala', 'Kavala', 'Καβάλα', 'EastMacedoniaThrace', 'Kamares aqueduct, Kavala', 'Καμάρες, Καβάλα', 40.9390, 24.4120, 'stone', 'land', 'grass', 'aqueduct()'),
 ('alexandroupoli', 'Alexandroupoli', 'Αλεξανδρούπολη', 'EastMacedoniaThrace', 'Lighthouse of Alexandroupoli', 'Φάρος Αλεξανδρούπολης', 40.8470, 25.8740, 'white', 'sea', None, 'lighthouse({ w: 0.1, tw: 0.07, h: 0.88 })'),
 ('xanthi', 'Xanthi', 'Ξάνθη', 'EastMacedoniaThrace', 'Old town mansion, Xanthi', 'Αρχοντικό Παλιάς Πόλης, Ξάνθη', 41.1340, 24.8880, 'white', 'land', 'grass', 'mansion()'),
 ('komotini', 'Komotini', 'Κομοτηνή', 'EastMacedoniaThrace', 'Imaret, Komotini', 'Ιμαρέτ, Κομοτηνή', 41.1220, 25.4060, 'stone', 'land', 'grass', 'mosque({ minaretX: 0.78, domeR: 0.28 })'),
 ('zakynthos', 'Zakynthos', 'Ζάκυνθος', 'IonianIslands', 'Campanile of Agios Dionysios, Zakynthos', 'Καμπαναριό Αγίου Διονυσίου, Ζάκυνθος', 37.7830, 20.8990, 'stone', 'sea', None, '[...campanile(0.1, 0.22, 0.85), M(rect(-0.9, 0, -0.04, 0.26)), M([[-0.92, 0.26], [-0.02, 0.26], [-0.47, 0.4]])]'),
 ('corfu', 'Corfu', 'Κέρκυρα', 'IonianIslands', 'Old Fortress, Corfu', 'Παλαιό Φρούριο, Κέρκυρα', 39.6240, 19.9220, 'stone', 'sea', None, 'corfuFort()'),
 ('argostoli', 'Argostoli', 'Αργοστόλι', 'IonianIslands', 'De Bosset Bridge, Argostoli', 'Γέφυρα Δε Μπόσσετ, Αργοστόλι', 38.1750, 20.4890, 'stone', 'sea', None, 'argostoli()'),
 ('lefkada', 'Lefkada', 'Λευκάδα', 'IonianIslands', 'Santa Maura Fortress, Lefkada', 'Φρούριο Αγίας Μαύρας, Λευκάδα', 38.8310, 20.7070, 'stone', 'sea', None, 'fort({ hh: 0.06, x: 0.85, wallTop: 0.24, towers: [[-0.85, 0.2, 0.4], [0.1, 0.2, 0.5], [0.85, 0.2, 0.36]], gate: -0.4 })'),
 ('heraklion', 'Heraklion', 'Ηράκλειο', 'Crete', 'Koules Fortress, Heraklion', 'Κούλες, Ηράκλειο', 35.3430, 25.1350, 'stone', 'sea', None, 'koules()'),
 ('agnikolaos', 'Agios Nikolaos', 'Άγιος Νικόλαος', 'Crete', 'Spinalonga Fortress (Lasithi)', 'Φρούριο Σπιναλόγκα (Λασίθι)', 35.1900, 25.7160, 'stone', 'sea', None, 'fort({ hh: 0.12, x: 0.85, wallTop: 0.26, towers: [[-0.85, 0.18, 0.32], [0.85, 0.18, 0.32], [0.1, 0.16, 0.4]], gate: null })'),
 ('rethymno', 'Rethymno', 'Ρέθυμνο', 'Crete', 'Fortezza, Rethymno', 'Φορτέτσα, Ρέθυμνο', 35.3700, 24.4740, 'stone', 'land', 'rock', '[...fort({ hh: 0.25, x: 0.9, wallTop: 0.22, towers: [[-0.9, 0.2, 0.3], [0.9, 0.2, 0.3]], gate: -0.5 }), M(rect(0.08, 0.47, 0.32, 0.52)), M(dome(0.2, 0.52, 0.12, 0.1))]'),
 ('chania', 'Chania', 'Χανιά', 'Crete', 'Venetian Lighthouse, Chania', 'Ενετικός Φάρος, Χανιά', 35.5190, 24.0180, 'stone', 'sea', None, 'lighthouse({ w: 0.17, tw: 0.12, h: 0.62, base: 0.1, baseW: 0.95 })'),
 ('rhodes', 'Rhodes', 'Ρόδος', 'SouthAegean', 'Palace of the Grand Master, Rhodes', 'Παλάτι του Μεγάλου Μαγίστρου, Ρόδος', 36.4510, 28.2260, 'stone', 'land', 'rock', 'rhodesPalace()'),
 ('ermoupoli', 'Ermoupoli', 'Ερμούπολη', 'SouthAegean', 'Town Hall, Ermoupoli (Syros)', 'Δημαρχείο Ερμούπολης (Σύρος)', 37.4440, 24.9420, 'marble', 'land', 'rock', 'neoclassic({ wide: 0.9, h: 0.46, cols: 6, turret: true })'),
 ('mytilene', 'Mytilene', 'Μυτιλήνη', 'NorthAegean', 'Castle of Mytilene', 'Κάστρο Μυτιλήνης', 39.1100, 26.5550, 'stone', 'sea', None, 'fort({ hh: 0.38, x: 0.85, towers: [[-0.85, 0.2, 0.3], [0.85, 0.2, 0.3], [0.2, 0.2, 0.42], [-0.3, 0.16, 0.28]] })'),
 ('samos', 'Samos', 'Σάμος', 'NorthAegean', 'Heraion of Samos (the lone column)', 'Ηραίο της Σάμου (η μοναχική κολώνα)', 37.7540, 26.9770, 'marble', 'land', 'grass', 'lonelyColumn()'),
 ('chios', 'Chios', 'Χίος', 'NorthAegean', 'Castle of Chios', 'Κάστρο Χίου', 38.3710, 26.1360, 'stone', 'sea', None, 'fort({ hh: 0, x: 0.9, wallTop: 0.36, towers: [[-0.9, 0.26, 0.5], [-0.3, 0.26, 0.5], [0.9, 0.26, 0.5]], gate: -0.6 })'),
]
assert len({d[0] for d in D}) == len(D)

THEATRE = """    const theatre = () => { const o = [M([...arc(0, 0, 0.92, 0.5, 0, Math.PI, 28), [-0.92, 0]])]; for (let k = 1; k <= 6; k++) { const r = k / 7; o.push(P('l', [...arc(0, 0, 0.92 * r, 0.5 * r, 0, Math.PI, 24), ...arc(0, 0, 0.92 * r - 0.012, 0.5 * r - 0.007, Math.PI, 0, 24)])); } o.push(M(rect(-0.3, 0, 0.3, 0.05)), P('d', arc(0, 0.05, 0.3, 0.1, 0, Math.PI, 16))); return o; };
"""

def num(v): return ('%.4f' % v).rstrip('0').rstrip('.')

def main():
    js = io.open(JS, encoding='utf-8').read()
    if 'BEGIN GREEK' in js:
        raise SystemExit('landmarks.js already generated; restore it from git first')
    tpl = io.open(TPL, encoding='utf-8').read() + THEATRE
    lines = []
    for d in D:
        i, cen, cel, reg, nen, nel, lat, lon, mat, scene, ground, expr = d
        if expr: lines.append("        L('%s', '%s', %s, %s, '%s', %s)," % (i, nen, num(lat), num(lon), mat, expr))
    info = ["        %s: { city: '%s', cityEl: '%s', region: '%s', nameEl: '%s' }," % (d[0], d[1], d[2], d[3], d[5]) for d in D]
    scenes = []
    for d in D:
        if d[9] == 'photo': scenes.append("        %s: { scene: 'photo' },                                    // a real aerial photograph of the Acropolis and Athens replaces sprite + landscape (scenes/acropolis.webp)" % d[0])
        elif d[9] == 'land': scenes.append("        %s: { scene: 'land', ground: '%s' }," % (d[0], d[10]))
        else: scenes.append("        %s: { scene: 'sea' }," % d[0])
    block = ("    // BEGIN GREEK (generated by tools/landmarks/gen_greek_landmarks.py)\n" + tpl +
             "    const GREEK = [\n" + "\n".join(lines) + "\n    ];\n"
             "    // capital of the prefecture, region and Greek name of every landmark (one per capital; Rio-Antirrio is an extra of Patras)\n"
             "    const INFO = {\n" + "\n".join(info) + "\n    };\n"
             "    const REGIONS = [" + ", ".join("'%s'" % r for r in REG) + "];\n    // END GREEK\n\n")
    js = js.replace("    const LANDMARKS = [\n", block + "    const LANDMARKS = [\n", 1)
    js = js.replace("        L('nafplio', 'Bourtzi, Nafplio', 37.5640, 22.8000, 'stone', []),\n    ];", "        L('nafplio', 'Bourtzi, Nafplio', 37.5640, 22.8000, 'stone', []),\n        ...GREEK,\n    ];", 1)
    js = js.replace("        // photo-only landmarks (no vector fallback): place and name only\n", "        // photographic landmarks: place and name only (no drawing needed)\n")
    # every landmark is offered now: photographs where an allowed licence was found, our own silhouettes for the rest
    js = js.replace("    // Photographic sprites (landmarks/<id>.webp, cut-outs of Wikimedia Commons photos with a clear sky, see landmarks/ATTRIBUTION.md). Only landmarks WITH a photo are offered: the hand-built\n    // vector shapes above look like drawings, so they stay as a fallback for the future (photoOnly = false) and are not shown by default.\n",
                    "    // Photographic sprites (landmarks/<id>.webp, cut-outs of Wikimedia Commons photos with a clear sky, see landmarks/ATTRIBUTION.md). Every other landmark is our own vector silhouette (drawProcedural).\n")
    js = js.replace("    let photoOnly = true;\n", "")
    js = js.replace("    function byId(id) { const m = LANDMARKS.find(x => x.id === id && SPRITE_IDS.includes(x.id)); return m ? { id: m.id, name: m.name, km: 0 } : null; }",
                    "    function byId(id) { const m = LANDMARKS.find(x => x.id === id); return m ? { id: m.id, name: m.name, km: 0 } : null; }\n"
                    "    // 'Random' choice: one landmark per day (stable while the wallpaper runs), from the whole list\n"
                    "    function randomLandmark(seed) { const d = new Date(), s = typeof seed === 'number' ? seed : d.getFullYear() * 400 + d.getMonth() * 31 + d.getDate(); const m = LANDMARKS[((s * 2654435761) >>> 0) % LANDMARKS.length]; return { id: m.id, name: m.name, km: 0 }; }\n"
                    "    const isSprite = id => SPRITE_IDS.includes(id);\n"
                    "    const infoOf = id => INFO[id] || null;")
    js = js.replace("function pickLandmark(lat, lon, maxKm = 250) {", "function pickLandmark(lat, lon, maxKm = 100000) {")
    js = js.replace("for (const m of LANDMARKS) { if (photoOnly && !SPRITE_IDS.includes(m.id)) continue; const d", "for (const m of LANDMARKS) { const d")
    js = js.replace("    // nearest landmark within maxKm of the position; null when nothing is near (the plain hills stay)", "    // nearest prefecture capital's landmark to the position (the weather / opt-in location); within maxKm, no limit by default so there is always one")
    js = js.replace("root.OpsisLandmarks = { LANDMARKS, pickLandmark, byId,", "root.OpsisLandmarks = { LANDMARKS, INFO, REGIONS, infoOf, isSprite, randomLandmark, pickLandmark, byId,")
    # scenes: replace old SCENE table
    js = re.sub(r"    const SCENE = \{.*?\n    \};", lambda m: "    const SCENE = {\n" + "\n".join(scenes) + "\n    };", js, count=1, flags=re.S)
    io.open(JS, 'w', encoding='utf-8', newline='').write(js)

    cs = ["namespace MotionDesk.Widgets", "{",
          "    /// <summary>Every landmark of the weather wallpaper: one per Greek prefecture capital (photo where an allowed licence exists, otherwise our own silhouette). Generated by tools/landmarks/gen_greek_landmarks.py; must match LANDMARKS in wallpaper/landmarks.js (a test checks it).</summary>",
          "    public static class LandmarkCatalog", "    {",
          "        public sealed record Entry(string Id, string Name, string NameEl, string Capital, string CapitalEl, string Region, double Lat, double Lon);", "",
          "        /// <summary>The 13 regions, in the order of the choice list (locale keys Wallpaper.Region.&lt;name&gt;).</summary>",
          "        public static readonly string[] Regions = { " + ", ".join('"%s"' % r for r in REG) + " };", "",
          "        public static readonly Entry[] Items =", "        {"]
    for d in D:
        cs.append('            new("%s", "%s", "%s", "%s", "%s", "%s", %s, %s),' % (d[0], d[4], d[5], d[1], d[2], d[3], num(d[6]), num(d[7])))
    cs += ["        };", "",
           '        public const string Auto = "Auto", Off = "Off", Random = "Random";', "",
           "        public static bool IsValid(string? id) => id is Auto or Off or Random || Items.Any(e => e.Id == id);", "",
           "        /// <summary>The nearest landmark to a position (the opt-in weather location); the same rule as pickLandmark in landmarks.js.</summary>",
           "        public static Entry Nearest(double lat, double lon) => Items.OrderBy(e => Km(lat, lon, e.Lat, e.Lon)).First();", "",
           "        private static double Km(double la1, double lo1, double la2, double lo2)", "        {",
           "            const double r = Math.PI / 180; double dLa = (la2 - la1) * r, dLo = (lo2 - lo1) * r;",
           "            double a = Math.Sin(dLa / 2) * Math.Sin(dLa / 2) + Math.Cos(la1 * r) * Math.Cos(la2 * r) * Math.Sin(dLo / 2) * Math.Sin(dLo / 2);",
           "            return 6371 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));", "        }", "    }", "}", ""]
    io.open(CS, 'w', encoding='utf-8', newline='').write("\r\n".join(cs) if '\r\n' in open(CS, 'rb').read().decode('utf-8', 'ignore') else "\n".join(cs))
    print(len(D), 'landmarks')

main()
