# OPSIS Intro A V2 — production master

The approved 1920×1080, 20 FPS, 15-second VP9/Opus introduction is now part of this source tree. Its canonical bytes are stored in the ten ordered `master/intro.000.part` … `intro.009.part` files because the connector cannot upload the full binary in a single request. These are source assets, not temporary renders. `IntroAssets.targets` reconstructs and checks the master into obj during normal .NET build/publish and copies it to assets/intro/intro.webm. Installer compilation requires all six runtime assets; the existing build script checks the exact master SHA-256.

Approved master SHA-256: `398e8564ff27401011067a1f29bffa9c7ce134e6d30caf4b62ff2fd5f970daa6`.
Source assets and derived glyphs are in assets/ and icons/. `render.py` generates video and icons; `soundtrack.py` generates original stereo audio and muxes it without changing the visual stream. Use Python 3, requirements.txt, FFmpeg with libvpx-vp9 and libopus, and locally installed Lato fonts (OPSIS_FONT_DIR). No fonts redistributed.

```
python render.py --output intro.webm
python render.py --icons icons
python soundtrack.py intro.webm intro-with-audio.webm
```

The installed master is the approved version, not regenerated during build. Encoder/library versions may change regenerated output. Keep the approved binary unchanged unless the user approves a new master. For authoring, `python assemble_master.py path/to/intro.webm` reconstructs the exact approved file.

The installer offers the 14-language introduction before the optional normal launch, with skip, mute, reduced-motion fallback and a watchdog. Silent installs skip it. The standard startup splash is unchanged. Windows/.NET 11 and Inno Setup 7 validation remains required, including real playback, localization/RTL, install and upgrade, silent modes, missing runtime and normal launch. No version bump.
