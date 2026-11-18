# Animated lock screen (GIF) - design, feature map, manual test

Status: implemented and unit tested against a FAKE cache folder. **Nothing of this has been run against a real Windows lock screen: NOT VERIFIED on real hardware.**

Idea and parts of the code: LockscreenGif by Leapward-Koex (MIT, notice in THIRD_PARTY_NOTICES.md). Not taken: Gifski (AGPL), PostHog analytics, its UI.

## Feature map (LockscreenGif -> Opsis)

| LockscreenGif | Opsis | Status |
|---|---|---|
| Choose a GIF, apply through the lock-screen cache | Personalization > Lock Screen Background: Choose GIF / Set as lock screen | done (unit tested on a fake cache) |
| Prerequisites panel: Picture mode, cache present, Windows feature | Same three checks, read-only, Refresh button | done |
| "Disable Windows feature" with UAC + reboot prompt (Not now / Reboot) | "Disable Windows feature (administrator)" + dialog "Restart now / Later" (`shutdown /r /t 0`, no /f) | done |
| Video -> GIF with clip trim + preview | Choose video + start / length / width / fps, FFmpeg palettegen/paletteuse, animated preview | partial: numeric trim, no timeline scrubber |
| Gifski for video | not used (AGPL) - FFmpeg only | by design |
| Download of FFmpeg | not done: the user picks an existing ffmpeg.exe (verified by `-version`) | not done |
| Save GIF... | Save GIF... | done |
| Logs (open folder, ZIP export) | Opens Opsis's log folder; helper details are written into `opsis.log` | partial: no ZIP export |
| PostHog analytics | none (a test fails if network/telemetry code appears) | dropped |
| Diagnostics report, feature-ID editor, verification runs | - | not done |
| (new) opt-in confirmation, backup, restore, `--restore-lockscreen`, uninstaller offer | see below | done |

## Safety design

1. OFF by default (`AppSettings.LockscreenGifConsent`). Turning it on shows a Yes/No dialog (default No) naming the files, the ACL change and the feature value.
2. Before any change the helper saves, into `C:\ProgramData\Opsis\lockscreen\` (admin-only; refused if the folder is owned by anyone else or is a link): every original cache file (`files\`), the original ACL of every touched folder/file (`icacls /save`, `acl\`), the original Windows feature override (`manifest.json`). The first original is never overwritten by later applies.
3. "Restore original lock screen" (UI), `Opsis.exe --restore-lockscreen` (no window, UAC prompt from the helper) and the uninstaller prompt all run the helper's `restore`: files back (a file Windows replaced after the apply is left alone), ACLs back (`icacls /restore`, owner back to SYSTEM when ownership was taken), feature override back (or removed if it did not exist), then the backup folder is deleted.
4. Elevated work only in `OpsisLockHelper.exe` (manifest `requireAdministrator`). Its whole command line is ONE word: `apply`, `restore` or `disable-feature` - no paths, SIDs or values. It reads only `%AppData%\Opsis\lockscreen\staging\anim.gif` (GIF header and size checked, links refused), writes only inside the cache root (LockScreen*\LockScreen.jpg and *_notdimmed.jpg, links refused) and its own backup folder, and touches only registry key `HKLM\SYSTEM\CurrentControlSet\Control\FeatureManagement\Overrides\8\2920700556`. Opsis itself never runs elevated and never starts the helper in an isolated (`OPSIS_APPDATA`) run.
5. No telemetry, no network code. Logs: Opsis's own log folder.

## Manual test on a real Windows 11 (user only - not done by the agent)

Use a VM or accept the risk; this changes system files.
1. Settings > Personalization > Lock screen: choose **Picture** and a picture. Press Win+L and unlock once (creates the cache).
2. Opsis > Personalization > Lock Screen Background: switch on **Enable the animated lock screen**, read and accept the dialog.
3. Press **Refresh**: Picture = [OK], cache = [OK]. On build 26200+ with the feature still active: **Disable Windows feature** (UAC), then **Restart now** (or Later).
4. **Choose GIF...** (or choose ffmpeg.exe + a video and **Convert to GIF**), then **Set as lock screen** (UAC). Status says the GIF was written.
5. Win+L: check the lock and sign-in screens. Playback is not guaranteed by Windows.
6. Check that `C:\ProgramData\Opsis\lockscreen\manifest.json` exists and `opsis.log` has "Lockscreen helper" lines.

**To restore**: press **Restore original lock screen** in the same card (UAC), or run `Opsis.exe --restore-lockscreen`, or uninstall Opsis and answer Yes to the restore prompt. Afterwards `C:\ProgramData\Opsis\lockscreen` is gone, the lock screen shows your original picture; restart if you had disabled the feature. If the helper cannot run at all, in an administrator command prompt: `icacls "C:\ProgramData\Microsoft\Windows\SystemData\<SID>\ReadOnly" /restore "C:\ProgramData\Opsis\lockscreen\acl\acl-N.txt" (run on the PARENT folder of each saved path)` and copy `files\*.bak` back by hand (names in manifest.json); or simply choose another lock-screen picture in Windows Settings, which makes Windows rebuild the cache.

## NOT VERIFIED (real system)

Whether `icacls /save|/restore` and `takeown` behave on the real `SystemData\<SID>\ReadOnly` ACLs (the helper falls back to takeown when the ACL cannot be read, and restores the owner to SYSTEM; unproven), whether writing the files in place is accepted while LogonUI has them open, whether the GIF then animates, the feature override semantics (taken from LockscreenGif's documentation, build 26200+, reboot), the installer's uninstall prompt (Inno script not compiled in this pass), and the UAC flow end to end.
