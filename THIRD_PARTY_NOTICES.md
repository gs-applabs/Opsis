# Third-party notices

## DesktopFences ("Desktop Fences +")

Opsis 2.2.0 bundles the Desktop Fences + source code (https://github.com/limbo666/DesktopFences, WPF, version 2.6.6.234)
under `containers/` as the separate companion app `OpsisContainers.exe`, which provides the DeskContainers. Changes made for Opsis:
retargeted to .NET 10, the COM shortcut interop replaced by a late-bound shim (`WshShim.cs`), all data moved to
`%AppData%\Opsis\containers` (`DataRoot.cs`), the upstream update/news check and branding removed, every user-visible name changed to
"DeskContainer(s)", the settings moved into the container menu, the tray icon removed (Opsis owns the single tray icon),
and the process exits together with Opsis. Data of earlier builds is migrated automatically (`LegacyNames.cs`).

> MIT License
>
> Copyright (c) 2025 LIMBO666
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.
>
> This project includes code originally developed by HakanKokcu as part of the BirdyFences
> project, which was licensed under the MIT License at the time of this project's creation.

## Other components (each under its own licence)

- LibreHardwareMonitorLib (github.com/LibreHardwareMonitor/LibreHardwareMonitor) - Mozilla Public License 2.0; reads GPU sensors for the System Monitor widget.
- NAudio / NAudio.Wasapi (github.com/naudio/NAudio) - MIT; WASAPI loopback capture and FFT.
- Microsoft.Web.WebView2 (Microsoft Edge WebView2 SDK) - used to render web and video wallpapers.
- .NET / Windows Forms runtime - MIT, (c) .NET Foundation and contributors.
- Opsis.UI (shared controls of the Opsis family) - MIT, same author.
- Place names for Greece come from GeoNames (geonames.org), data under CC BY 4.0, shipped as a compact offline table (src/Widgets/Data, built by tools/build-gazetteer.py). Optional online place names (only when you allow it and press "Use my location") come from OpenStreetMap Nominatim: (c) OpenStreetMap contributors, ODbL, under the Nominatim usage policy (at most 1 request per second, identifying User-Agent, cached).
- Weather data comes at run time, without an API key, from free providers: the US National Weather Service (api.weather.gov, US only, open data), Open-Meteo (open-meteo.com, data CC BY 4.0, non-commercial use) and MET Norway (api.met.no, data CC BY 4.0) with wttr.in (github.com/chubin/wttr.in) as the last fallback. Only coordinates rounded to about 1 km, or the city name you type, are sent. Details: docs/WEATHER-PROVIDERS.md.

## Disclaimer

The software is provided "as is" (see LICENSE). It injects a window behind the desktop icons and customises system icons and cursors: use it at your own risk.

## Landmark photos (weather wallpaper)
Cut-outs of Wikimedia Commons photographs, each checked on its own (public domain, CC0 or CC BY only): Parthenon east side by Jebulon (CC0), plus the Greek landmark photographs listed in the manifest (Patras, Rio-Antirrio, Nafplio); changes: sky removed, cropped, scaled, converted to WebP. Full manifest: docs/LANDMARK-IMAGES.md and docs/LANDMARK-IMAGES.json.

Additional landmark photos: Lighthouse of Patras (public domain), Rio-Antirrio Bridge (public domain, see docs/LANDMARK-IMAGES.md), Bourtzi Nafplio (CC0).

Aerial scene of the Acropolis and Athens: The Acropolis of Athens from Mount Lycabettus on March 14, 2020, by George E. Koronaios (CC0), cropped, widened and faded (see docs/LANDMARK-IMAGES.md).

