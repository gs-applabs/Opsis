# Opsis - Desktop Customization 2.8.0.2510

Final build of the 2.8.0 line (base 2.8.0.2500 plus the stable parts of 2.6.0). .NET 11 Desktop Runtime x64 required.

## Changes since 2.8.0.2500
- Application name is "Opsis - Desktop Customization" everywhere ("Studio" removed). Data folder `%AppData%\Opsis` (automatic one-time move from the old folder); installs to `Program Files\Opsis - Desktop Customization`.
- Shared UI library Opsis.UI 0.6.0: unified controls, tokens and window chrome. All secondary windows (settings dialogs, zone editor, containers dialogs, prompts) now follow the Opsis design: skin colours, dark title bar, flat controls, dark scroll bars.
- DeskContainers: corner-radius setting for every look, smoother wheel scrolling.
- Desktop weather/time widget: text colour follows the automatic contrast setting (no more black text on dark photos).
- WebView2: the profile folder is keyed by the browser arguments, with a private fallback, so another Opsis edition running at the same time no longer breaks the wallpaper.
- Plugin host pushes the host settings to plugins (contract 1.0.0.0 union: Crawler 0.28 types plus Opsis host settings and update source).
- Fixed a hang found by the Windows audit: `ThemeManager.RefreshFromWindows` re-posted itself endlessly when its callback was dispatched by another synchronization context.
- Windows audit (`--ui-audit`) and self-test run on the GitHub Windows runner; the audit reads the real Windows transparency setting.

## Verification (exact commit 9376554)
- Windows 2025 runner: installer build, `--run-selftest`, wallpaper JavaScript tests, UI click audit (81 reversible controls), resource cycles: PASS (run 45492489483).
- NOT VERIFIED: interactive installation on a clean Windows 11 VM, upgrade from 2.6.0, mixed-DPI multi-monitor.

## Files
- `OpsisSetup.exe` (+ `.sha256`): installer. Unsigned.
- `Opsis-Source.zip`: source archive of the tagged commit.
