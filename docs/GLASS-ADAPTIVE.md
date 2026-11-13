# Adaptive GPU glass (widgets, DeskStrip, containers)

Default: **Automatic**. The user can pick Automatic / Always glass / Simple blur / Solid (Settings -> Appearance -> Advanced), and the page shows what is used now and why. An old explicit "GPU glass on" from an earlier beta is kept as "Always".

## Levels
| Level | What it is |
|---|---|
| Full | GPU glass with refraction and glossy highlight, 60 fps backdrop capture |
| Frosted | GPU glass, frosted, 30 fps capture |
| Blur | the old DWM / simple blur look |
| Solid | no glass |

## Minimum / maximum specs (Automatic)
| Computer | Level |
|---|---|
| High tier, >= 8 GB RAM, >= 4 physical cores, discrete GPU (or integrated with >= 12 GB RAM), mains power | Full |
| Medium tier; or 4-8 GB RAM; or 2-3 cores; or integrated GPU with < 12 GB; or on battery | Frosted |
| Low tier; < 4 GB RAM; 1 core; battery saver; a full-screen app is running; GPU path failed (device reset) | Blur |
| Windows < 11 (build < 22000), Safe Mode, High Contrast, Windows "transparency effects" off, locked or remote session | Solid (also when the user chose Always) |
| AMD / Radeon graphics | at most Frosted (refraction off by default: earlier rim-bitmap crash on AMD; "Always" lets the user try it) |

## Live adaptation
The load part is the existing `PerformanceGovernor`: the effective tier goes down after 10 s of pressure (CPU > 88 %, GPU > 92 %, RAM > 92 %, low battery) and comes back after 45 s of calm (hysteresis, no flicker). `GlassPolicy` follows that tier, re-decided every 2 s. Windows already open keep their glass object; a window created later follows the level at that time.

Not measured: frame time and VRAM (no reliable cheap source); the GPU-failure fallback is wired through the existing `_failed` flag.
