# Opsis contribution manifest (schema 1)

A plugin and a separate wallpaper pack can contribute content to the **same** Gallery without a custom GUI, independent service or executable:

`<Opsis>/plugins/<plugin-id>/opsis-contributions.json`
`%AppData%/Opsis/plugins/<plugin-id>/opsis-contributions.json`
`%AppData%/Opsis/gallery-packs/<pack-id>/opsis-contributions.json`

Example:

```json
{
  "Schema": 1,
  "PluginId": "gr.opsis.greek-scenes",
  "Name": "Greek Landscapes",
  "Contributions": [
    {
      "Id": "acropolis-day",
      "Type": "wallpaper",
      "Category": "Greece / Athens",
      "Title": "Acropolis — Day",
      "Asset": "assets/acropolis.webp",
      "Author": "Opsis",
      "Description": "Original static landscape"
    },
    {
      "Id": "athens-clock",
      "Type": "widget",
      "Category": "Clock",
      "Title": "Athens Clock",
      "Asset": "assets/athens-clock.json",
      "Author": "Opsis",
      "Description": "Widget metadata; execution must be separately approved"
    }
  ]
}
```

## Accepted Types
`wallpaper`, `widget`, `theme`, `icon-pack`, `cursor-pack`, `automation-template`, `layout`.

`Type` and `Category` define the shared Gallery grouping. A plugin may contribute multiple items in different categories. Missing/broken contributions are skipped instead of taking down the host.

**Important:** Discovery is metadata-only. It never executes JavaScript, DLLs or native code. Only explicitly selected *static image wallpaper* contributions can be applied by the current Gallery; accepted extensions are PNG, JPEG, WebP, GIF and BMP. Executable widgets, HTML wallpapers and templates need separate permission and host integration; listing a contribution is **not** a grant to execute code.

## Security and compatibility

- `PluginId`, `Id` use ASCII alphanumeric characters, dash, underscore and dot (1–80 chars).
- Asset paths must be relative forward-slash paths inside the contribution folder. Absolute paths, `..`, root escapes and reparse/symlink path components are rejected.
- Exactly one manifest per pack, maximum 100 contributions; manifest maximum 128 KiB; asset maximum 500 MiB for gallery metadata, static images limited to 64 MiB on apply.
- Duplicate `PluginId:Id` pairs are de-duplicated; no implicit overwrites.
- Shell mods must opt into `OpsisModCore` with explicit Windows-build compatibility, dependencies and conflict declarations.
- Native plugins still go through the existing `PluginHost` executable approval workflow.
- Feature updates trigger a Windows compatibility review; activation that risks changing Taskbar/Explorer is held until reviewed.
- Never copy proprietary Windhawk or Rainmeter code into Opsis without examining license and provenance. An interoperable capability need not load their binaries.

## Repository / design

The shared contract is implemented by `src/Services/OpsisEcosystemGallery.cs`. The application displays contributions in `src/UI/MainWindow.cs`, grouped by category/type, and can apply vetted image wallpapers with backup. This is an initial manifest contract; richer widget content APIs, visual editors and downloadable catalogs are separately tracked as planned features.
