# OPSIS R19 — Wallpaper Add-On integration review (NOT a release)

This branch is an integration **review**, not the standalone add-on repository or a published GitHub Release. It deliberately keeps the older OPSIS main-branch source untouched.

## Verified local source deliverables
- Full standalone addon repository: `OPSIS-100-Moving-Wallpapers-Git-Repository.zip` including .git and annotated tag `v1.1.1`.
- Standalone source: `OPSIS-100-Moving-Wallpapers-Release-v1.1.1-Source.zip`.
- Git bundle: `OPSIS-100-Moving-Wallpapers-v1.1.1.git.bundle`.
- R17 to R19 Weather/Gallery source and patch: `OPSIS-R19-Weather-Gallery-Source.zip`.
- Manifest: 100 distinct wallpapers, 10 categories, 750 payload files with SHA-256, all locally verified.

## Host-side changes
These must be reviewed against the **R17** source checkpoint, not merged blindly into GitHub main:
- `src/UI/MainWindow.WallpaperGallery.cs` — category filters and pagination.
- `src/Services/WallpaperGalleryCatalog.cs` — auto-discovery from `wallpapers/samples` and `wallpapers/packs/opsis-100`.
- `src/Widgets/WallpaperManifest.cs` — wallpaper ID/category/collection metadata.
- `src/Widgets/wallpaper/weather.js` — removal of visible skyline outline and straight clipping seam.
- `src/Widgets/wallpaper/sky-mask.js` — mitigation of bright edge halo.
- `Opsis.Studio.csproj` — packaging changes and system-monitor sample retention.

### Important
Current GitHub main is not the R19 snapshot. Do not treat this draft PR as a verified R19 build, a 100-wallpaper upload, or a release. The full binary/payload bytes need to be pushed into a newly created standalone repository by a GitHub-authorized workstation, then the tag `v1.1.1` pushed to launch the release workflow.

The local release workflow builds the add-on setup EXE on Windows with Inno Setup, uploads a source ZIP and SHA-256 sums, and creates a GitHub Release on a pushed `v*` tag. Windows/WebView2 validation and Inno compilation remain unverified.
