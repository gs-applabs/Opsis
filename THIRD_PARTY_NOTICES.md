# Third-party notices

## DesktopFences ("Desktop Fences +", historical releases)

Opsis 2.2.0 bundled the Desktop Fences + source code (https://github.com/limbo666/DesktopFences, WPF, version 2.6.6.234)
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

## Feature references

These features are implemented in Opsis using its existing WinForms UI and services.

- Command Palette acronym and fuzzy search follow the behavior of [Flow Launcher](https://github.com/Flow-Launcher/Flow.Launcher/blob/dev/Flow.Launcher.Infrastructure/StringMatcher.cs), licensed under the [MIT License](https://github.com/Flow-Launcher/Flow.Launcher/blob/dev/LICENSE), copyright (c) 2019 Flow-Launcher and (c) 2015 Wox.
- Timed system wakefulness follows [Microsoft PowerToys Awake](https://github.com/microsoft/PowerToys/tree/main/src/modules/awake), licensed under the [MIT License](https://github.com/microsoft/PowerToys/blob/main/LICENSE), copyright (c) Microsoft Corporation.

## Adapted source code

### PecoFence v0.0.1

[Upstream source](https://github.com/DayuanJiang/PecoFence/tree/4f6356369267835c8f5361c4dc6617e409a77a11). The fence placement and folder-portal behavior were adapted to WinForms.

> MIT License
>
> Copyright (c) 2026 PecoFence contributors
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

### LockscreenGif

[Upstream source](https://github.com/Leapward-Koex/LockscreenGif/tree/ad4c030b5e0877e39eb52e76d00e2f46dc905e7a). Opsis adapts the GIF inspection and Windows cache layout for its existing lock-screen UI.

> MIT License
>
> Copyright (c) 2026 Leapward-Koex
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

## Other components (each under its own licence)

- LibreHardwareMonitorLib (github.com/LibreHardwareMonitor/LibreHardwareMonitor) - Mozilla Public License 2.0; reads GPU sensors for the System Monitor widget.
- NAudio / NAudio.Wasapi (github.com/naudio/NAudio) - MIT; WASAPI loopback capture and FFT.
- Microsoft.Web.WebView2 (Microsoft Edge WebView2 SDK) - used to render web and video wallpapers.
- .NET / Windows Forms runtime - MIT, (c) .NET Foundation and contributors.
- Opsis.UI (shared controls of the Opsis family) - MIT, same author.
- Weather data comes at run time from the free Open-Meteo API (open-meteo.com) and, as a fallback, wttr.in (github.com/chubin/wttr.in), without an API key; only the requested coordinates or city name are sent.

## Disclaimer

The software is provided "as is" (see LICENSE). It injects a window behind the desktop icons and customises system icons and cursors: use it at your own risk.
