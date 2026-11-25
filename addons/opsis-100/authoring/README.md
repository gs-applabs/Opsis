# OPSIS 100 Wallpapers — Europe & Ancient Rome expansion (authoring)

**Distribution boundary (mandatory):**
- **Ancient Athens is built into Opsis itself.** It is never copied, advertised, or installed a second time by this addon.
- **Ancient Rome** and **50 European capital entries** belong to the optional **OPSIS 100 Wallpapers** add-on, not to another standalone installer or a second host.
- Existing **100** wallpaper entries are preserved; this expansion adds **51 additional** themes/scenes, making **at least 151 catalog entries** once all artwork is complete. Brand naming remains compatible; never silently replace one of the original 100.

## Current status (not a release)

This folder is a **source authoring contract**. It **does not contain the 51 final WebP asset sets**, a tested add-on installer, verified photographs, or GitHub Release. Existing R19 repo described in `docs/opsis-wallpapers-addon-r19/INTEGRATION-HANDOFF.md` is still review-only on GitHub. The authoring JSONs must be copied into the real standalone add-on source when available.

The 50 European capital targets are in `europe-capitals.json`, with coordinates, Greek/English labels, unique places, and the expected WebP paths. The **46 principal entries** follow the mapped list cited below; an extended geographical/political scope includes **Pristina, Tbilisi, Yerevan and Baku**. Because continent boundaries and state recognition differ, the scope for those four is explicitly flagged rather than silently presented as an undisputed list.

The Ancient Rome theme must depict **circa AD 320** (not a collage of different centuries). Archaeological landmarks and their spatial relations must be reviewed; in particular the completed Colosseum and Arch of Constantine can coexist in this era. No modern electric floodlights on historical monuments.

## Required assets and rendering

Each city/theme gets original or licensed **WebP** scenery (master-quality scene, poster, skyline/foreground mask), not a universal skyline reused with renamed labels. High-resolution profiles should target 3840×2160 where the source supports it, with an independently verified 1920×1080 fallback. Export with high-visual-quality photographic settings; test SSIM/visual crops (architecture, gradients, lettering), file sizes and WebView2 decode. Avoid aggressive lossy settings that smear masonry or create dawn/night banding.

The renderer handles location-specific astronomy, live weather observations, sun/moon, cloud cover, rain/snow/fog/storms and birds with correct occlusion. Monuments remain static, with city-lights masks **excluding all monuments**. Each scene must preserve a consistent camera angle between phases to prevent apparent building movement during crossfade.

Packaging layout, once sources and installation code are available:
```text
wallpapers/packs/opsis-100/
  eu-amsterdam/index.html, wallpaper.json, poster.webp, ...
  eu-rome/index.html, wallpaper.json, poster.webp, ...
  ...
  ancient-rome-320/index.html, wallpaper.json, poster.webp, ...
```
All themes are installed via the **same add-on installer and Gallery discovery**. Each theme gets an independent ID and categories `European Capitals` or `Historical Cities`. New historical Rome must not overwrite modern `eu-rome`. Built-in Ancient Athens is excluded by manifest validation.

## Release gates

1. Verify the existing 100 wallpapers and their SHA-256 files unchanged, plus all 51 extra entries; update signed checksums.
2. Include every actual WebP texture, masks, proper licenses, and checksums, and satisfy `node addons/opsis-100/authoring/validate.cjs --release`.
3. Validate time-of-day (including polar summer/winter conditions), DST, city-specific live weather, fog/particles/celestial sky occlusion.
4. Test on Windows 11 with real WebView2/GPU, 1080p/4K, mixed DPI, multimonitor, low power, and Safe Mode fallback.
5. Run upgrade/install/uninstall with the R19 package, showing no duplicate Athens and no change to unrelated plugins.
6. Only then publish the GitHub add-on installer/Release; no false completion or placeholder advertising in Gallery.

## Geography and architecture references

- [European capitals geographical coordinate list](https://geogra.org/en/geography-games/europe/capitals-of-europe)
- [Council of Europe capitals/map](https://www.eurogeography.eu/wp-content/uploads/2025/04/Poster-COE-EU-2025.pdf)
- [Rome Reborn research](https://www.rome-reborn.net/)
- [Ancient Rome 3D archaeological reconstruction (Rome Reborn)](https://www.livescience.com/archaeology/romans/soar-over-ancient-romes-temples-brothels-and-baths-in-epic-new-3d-reconstruction)
