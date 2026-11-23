# Opsis Widget SDK v1

A package is a ZIP with `widget.json` at its root and the web assets of the widget. Open **Widgets → Widget add-on catalog → Install ZIP**. The bundled `catalog/local-time` and `catalog/day-progress` directories are working examples. ZIP the files inside either sample directory to make a package. Give a modified sample a new ID before installing it.

```json
{
  "id": "my-clock",
  "name": "My Clock",
  "version": "1.0.0",
  "description": "A local clock",
  "entry": "index.html",
  "width": 320,
  "height": 210,
  "api": 1
}
```

Load JavaScript and CSS as separate local files. The widget receives state from Opsis through WebView2's message bridge.

```js
window.chrome.webview.addEventListener("message", ({ data }) => {
  if (data.type !== "opsis:state" || data.api !== 1) return;
  document.documentElement.dataset.theme = data.dark ? "dark" : "light";
  document.documentElement.style.setProperty("--accent", data.accent);
});
window.chrome.webview.postMessage({ type: "opsis:ready" });
```

`opsis:state` includes `language`, `dark`, `accent`, and `now` as an ISO date with offset. State is sent when the page opens and when the application theme or language changes. Use ordinary browser APIs for interval-based updates and local storage. The package's origin is restricted to its own files. Supported assets include HTML, CSS, JS, JSON, PNG, JPEG, WebP, GIF, and WOFF2. Packages can contain up to 300 entries and 40 MiB uncompressed. External requests and navigation, new windows, nested frames, downloads, and OS host objects are unavailable.
