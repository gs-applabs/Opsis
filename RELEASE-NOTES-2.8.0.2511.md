# Opsis - Desktop Customization 2.8.0.2511

Refresh of 2.8.0.2510 (same code base, .NET 11 Desktop Runtime x64 required).

## Changes since 2.8.0.2510
- The start screen links every Windows-look page (Personalization, Automation, Performance, Profiles, Sound), describes each one and shows the notes of the running version.
- About shows the notes of the running version instead of a fixed old one; the plugin-compatibility heading and the plugin-skip log use the running version.
- Old "2.6.0" labels removed (window skin name, About section); the .NET line in About says .NET 11 in all languages; README updated for 2.8.0.2510 / 2.8.0.2511.
- Texts refreshed in all 14 languages (Arabic, Chinese, English, French, German, Greek, Hindi, Italian, Japanese, Korean, Portuguese, Russian, Spanish, Turkish): version notes 2.6.0 / 2.8.0.2510 / 2.8.0.2511, start screen.

## Verification
- Build 0 errors, `--run-selftest` exit 0, wallpaper/plugin JavaScript tests 42/42 locally; the Windows 2025 runner repeats installer build, self-test, UI click audit and resource cycles on the exact commit (see the release for the run).
- NOT VERIFIED: interactive installation on a clean Windows 11 VM, upgrade from 2.6.0 / 2.8.0.2510, mixed-DPI multi-monitor, a visual check of the new start screen in every language. Installer is unsigned.

## Files
- `OpsisSetup.exe` (+ `.sha256`), `Opsis-2.8.0.2511-Source.zip`.
