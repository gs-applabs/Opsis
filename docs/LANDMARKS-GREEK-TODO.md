# Greek landmarks: what is in 2.6.0 and what is still missing

Rule (user, 2026-11): ONLY Greek landmarks, the sights of the capitals of the prefectures (nomoi); photographs only from public domain / CC0 / CC BY, checked per image (manifest in `docs/LANDMARK-IMAGES.md`).

## In 2.6.0 (5)
| Capital / place | Landmark | Form |
|---|---|---|
| Athens | Parthenon / Acropolis | real aerial photograph (CC0) with day / dusk / night |
| Patras | Lighthouse of Patras | photo cut-out |
| Rio - Antirrio (Achaea / Aetolia-Acarnania) | Rio-Antirrio bridge | photo cut-out |
| Nafplio (Argolis) | Bourtzi | photo cut-out |
| Thessaloniki | White Tower | vector drawing only: no photograph with an allowed licence was found yet |

## Missing (suggested landmark for each; a licensed photograph has to be found per entry)
Thessaly and Macedonia: Larissa (ancient theatre), Volos (Argonauts' ship / seafront), Trikala (Asklipieion / clock tower), Karditsa (Lake Plastiras), Kozani, Veria (Old Jewish quarter bridge), Katerini, Kastoria (lake and old town), Florina, Serres (Bezesteni), Drama (Agia Varvara springs), Kavala (Kamares aqueduct), Edessa (waterfalls), Giannitsa, Polygyros.
Thrace: Xanthi (old town), Komotini, Alexandroupoli (lighthouse).
Epirus: Ioannina (castle and lake), Arta (Bridge of Arta), Preveza, Igoumenitsa.
Central Greece and Evia: Lamia (castle), Livadeia, Chalkida (Euripus bridge), Karpenisi, Amfissa, Thebes.
Western Greece: Agrinio, Messolonghi (lagoon / Garden of Heroes), Pyrgos (Olympia is the sight of the prefecture).
Peloponnese: Corinth (Acrocorinth), Argos, Tripoli, Sparta, Kalamata (castle), Monemvasia.
Ionian: Corfu (Old Fortress), Zakynthos, Argostoli, Lefkada, Ithaca.
Crete: Heraklion (Koules fortress), Chania (Venetian lighthouse), Rethymno (Fortezza), Agios Nikolaos / Sitia.
Aegean: Rhodes (Palace of the Grand Master), Kos, Mytilene (castle), Chios, Samos (Pythagoreion), Syros (Ermoupoli town hall), Mykonos (windmills), Santorini (Fira / Oia), Naxos (Portara), Tinos, Andros, Kalymnos, Limnos, Thasos, Skiathos, Skopelos, Skyros.
Attica: Piraeus (Zea / Mikrolimano), Eleusis, Cape Sounion (Temple of Poseidon), Marathon.

Add one at a time: find the Commons file, check the licence on its own page, run `tools/landmarks/build_sprites.py`, add the entry to `landmarks.js` (`SPRITE_IDS`, `SCENE`), `LandmarkCatalog.cs`, and the credit lines.
