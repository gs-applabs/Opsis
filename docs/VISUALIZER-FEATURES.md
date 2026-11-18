# Music visualizer - feature mapping

Opsis element: wallpaper element "Music visualizer" (Wallpaper page, card of the same name). Code: `src/Widgets/wallpaper/visualizer.js`,
`src/Widgets/VisualizerConfig.cs`, `src/UI/MainWindow.Visualizer.cs`; audio from the existing `AudioSpectrumService` (WASAPI loopback).

Clean-room note: the feature list below comes only from the public README / property list of the Lively wallpaper "Rainbow Music Visualizer"
(Aprotonix, no licence). No code, font, image or GIF of it was read or copied; the renderer, maths and UI are Opsis's own.

| Original setting | Opsis setting (`VisualizerConfig`) | Status |
|---|---|---|
| Background (colour) | UseBackgroundColor + BackgroundColor | done |
| Backround wallpaper / Background select (.jpg/.png/.webm) | UseBackgroundImage + BackgroundImagePath (file picker; image or video) | done |
| Custom Gradient | CustomGradient (off = rainbow) | done |
| Color Begin / Color End | Color1 / Color2 | done |
| White light inside | WhiteInside | done |
| Show Music Visualizer | Enabled (whole element) + ShowRays (rays only) | done |
| Clean line end | CleanLineEnd (butt caps, no shadow) | done (own interpretation) |
| Interpolate (more line) | Interpolate (120 instead of 60 rays) | done |
| Rounded Mode | Rounded | done |
| Hide when no music | HideWhenSilent (fades out after 1.2 s of silence) | done |
| Reverse Audio | Reverse | done |
| Minimal Audio Value | MinLevel (0..1000) | done |
| Line Width | LineWidth | done |
| Intensity | Intensity (0..99) | done |
| Add audio value | AddLevel (0..500) | done |
| Angle of the line | RayAngle (rounded mode) | done |
| Increases treble | TrebleBoost | done |
| Y Music Visualizer | PosY (+ PosX, extra) | done |
| Regenerate random spectre | IdleSeed / "Regenerate idle pattern" button | done |
| Text font | Font (list of installed system fonts) | done |
| Text bold | Bold | done |
| Show Hour / Hour Size / Hour Color | ShowClock / ClockSize / ClockColor | done |
| X, Y Hour position | ClockX / ClockY | done |
| Show Seconds | ShowSeconds | done |
| 12 hour clock format | Hour12 | done |
| Show Date / Date Size / Date Color | ShowDate / DateSize / DateColor | done |
| X, Y Date position | DateX / DateY | done |
| Show Day Name / Day Number / Year | ShowDayName / ShowDayNumber / ShowYear | done |
| Date format MM/DD/YYYY | NumericDate | done |
| Shadow | Shadow | done |
| Shadow Intensity | ShadowIntensity (0..500) | done |
| Shadow with music | ShadowFollowsMusic | done |
| Update notification | - | not applicable: Opsis has its own update check |
| (extra) Linear layout | Layout = Linear | Opsis addition |

Performance: one requestAnimationFrame loop, only while the element is enabled, the page visible, the wallpaper not paused/occluded and the performance mode is not Battery;
audio polled <= 30/s (15/s on low tiers) with one request in flight; typed arrays and pre-built colour strings (no per-frame allocation); the dynamic performance tier
(`cfg.perf`: fps cap, render scale, glow) is honoured; when the loop stops the loopback capture is released (`StopAudioBands`).

NOT VERIFIED: visual output in a real WebView2 wallpaper window and real audio (no live run in this pass); only the config, wiring and JS syntax are tested.
