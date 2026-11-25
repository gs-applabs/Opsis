'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
// These tests verify integration wiring and release gate structure, NOT a Windows build.
test('#12 plugin permissions are declared, readable, and included in trust digest', () => {
  const declaration = read('src/Services/PluginCapabilityManifest.cs');
  assert.match(declaration, /opsis-permissions\.json/);
  for(const cap of ['Network','Audio','LocalFiles']) assert.ok(declaration.includes(cap));
  assert.match(declaration, /not an OS sandbox/i);
  assert.match(read('src/Services/PluginTrust.cs'), /opsis-permissions\.json/);
  assert.match(read('src/UI/MainWindow.cs'), /RevokePluginTrust/);
});
test('#13 widget system data is SHA bound and opt-in, revocable', () => {
  const consent=read('src/Services/WidgetConsentStore.cs');
  assert.match(consent, /Fingerprint/);
  assert.match(consent, /SHA256/);
  assert.match(consent, /Allowed\(/);
  assert.match(consent, /Revoke\(/);
  assert.match(read('src/Widgets/WidgetSdkView.cs'), /opsis:request-system/);
  assert.match(read('src/Widgets/WidgetEngine.cs'), /WidgetConsentStore.Revoke/);
});
test('#32 and #33 panorama is opt-in and monitor pair plus bezel stay bounded', () => {
  const source=read('src/Widgets/WallpaperWindow.cs');
  for (const token of ['PanoramaInfo','PanoramaFirstMonitor','PanoramaSecondMonitor','PanoramaBezelGapPx'])
    assert.ok(source.includes(token), token);
  assert.match(source, /Math\.Clamp\(cfg\.PanoramaBezelGapPx, 0, 400\)/);
  assert.match(read('src/Widgets/wallpaper/index.html'), /cfg\.panorama/);
  assert.match(read('src/UI/MainWindow.WallpaperMedia.cs'), /Apply panorama/);
});
test('#44/#45 joined zones need adjacent edges and a user-controlled boundary', () => {
  const zones=read('src/Widgets/ZoneLayoutStore.cs');
  assert.match(zones,/TryJoinAdjacent/);
  assert.match(zones,/TryResizeSharedBorder/);
  assert.match(zones,/!double\.IsFinite\(delta\)/);
  const tile=read('src/Services/AutoTileService.cs');
  assert.match(tile,/TrySpanWindow/);
  assert.match(tile,/RestoreSpan/);
  assert.match(tile,/ResumeAutomaticTilingAfterSpans/);
  assert.match(read('src/Widgets/ZoneLayoutEditorForm.cs'), /TryResizeSharedBorder/);
});
test('#78 microphone input is explicit and output loopback remains default', () => {
  const code=read('src/Services/SharedLoopbackCapture.cs');
  assert.match(code,/WasapiLoopbackCapture/);
  assert.match(code,/new WasapiCapture\(device\)/);
  assert.match(code,/SelectInput/);
  assert.match(code,/DataFlow\.Capture/);
  assert.match(read('src/Music/MusicOverlaySettings.cs'), /InputDeviceId/);
  assert.match(read('src/Music/MusicOverlayPage.cs'), /audioInput/);
});
test('#92 installer dependencies do not download automatically in silent none', () => {
  const code=read('installer/dependencies.iss');
  assert.match(code, /\{param:DEPS\|none\}/);
  assert.match(code, /procedure DepsInstallSelected/);
  assert.match(code, /function DepsDownloadSelected/);
  assert.match(read('tools/qa-r9-windows.ps1'), /\.NET 11 SDK/);
});
test('#93 forced classic menu avoids native DLL and defines all 5 verbs', () => {
  const build=read('installer/explorer/Build-Explorer.ps1');
  assert.match(build,/ForceClassicMenu/);
  const menu=read('src/Services/DesktopMenu.cs');
  for(const cmd of ['new-container','prev-wallpaper','next-wallpaper','toggle-wallpaper','stop-wallpaper'])
    assert.ok(menu.includes('("'+cmd+'"'),cmd);
  assert.match(read('tools/qa-r9-windows.ps1'), /classic-explorer-five-commands/);
});
test('#99 upgrade side by side and uninstall are explicit Windows QA gates',()=>{
  const qa=read('tools/qa-r9-windows.ps1');
  for(const x of ['PreviousInstaller','PreviousSha256','LegacyInstaller','LegacySha256','installer-install-upgrade-uninstall','side-by-side-legacy-folder-guard'])
    assert.ok(qa.includes(x),x);
  assert.match(qa,/NOT_VERIFIED/);
  const w=read('.github/workflows/r9-release-qa.yml');
  assert.match(w,/windows-2022/);
  assert.match(w,/windows-2025/);
  assert.match(w,/if: success\(\)/);
});
