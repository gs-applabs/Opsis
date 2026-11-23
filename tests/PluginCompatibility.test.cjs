'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname,'..');
const source = p => fs.readFileSync(path.join(root,p),'utf8');
const catalog = JSON.parse(source('suite/apps.json'));

test('family catalogue includes each independent companion application exactly once', () => {
  const ids = catalog.apps.map(a => a.id);
  assert.equal(new Set(ids).size, ids.length, 'duplicate family app identifiers');
  for (const [id,repo] of [['opsis','Opsis'],['newscrawler','OpsisNewsCrawler'],['chronikologio','san-shmera'],['mediasuite','OpsisMediaSuite']]) {
    const app = catalog.apps.find(a => a.id === id);
    assert.ok(app, `missing ${id}`);
    assert.equal(app.repo, repo);
    assert.ok(app.paths.length, `${id} has no executable candidate`);
  }
});

test('Chronikologio installer and plugin contract are mapped correctly', () => {
  const c = catalog.apps.find(a => a.id === 'chronikologio');
  assert.equal(c.installerAsset, '^ChronikologioSetup\\.exe$');
  assert.ok(c.paths.some(p => p.endsWith('Chronikologio\\Chronikologio.exe')));
  assert.ok(c.capabilities.el.some(p => p.includes('Σαν σήμερα')));
});

test('standalone Media Suite is treated as an external program, not a .NET plugin', () => {
  const m = catalog.apps.find(a => a.id === 'mediasuite');
  assert.ok(m.paths.some(p => p.includes('Opsis Media Suite.exe')));
  assert.doesNotMatch(m.paths.join(' '), /Plugin\.dll/i);
});

test('plugin host shares both contract and Opsis.UI singleton assemblies', () => {
  const host = source('src/Services/PluginHost.cs');
  assert.match(host,/typeof\(IOpsisNewsCrawlerPlugin\)\.Assembly/);
  assert.match(host,/typeof\(Opsis\.UI\.FlatButton\)\.Assembly/);
  assert.match(host,/CompatibleHostAssembly\(name, sharedUiAssembly\)/);
  assert.match(host,/requested\.Version > available\.Version/);
});

test('host compatibility diagnostic reports both plugin IDs and registered feeds', () => {
  const d = source('src/Services/DesktopDiagnostics.cs');
  assert.match(d,/ids=\{/);
  assert.match(d,/PluginFeeds\.All\(\)/);
});

test('version metadata is ready for 2.8.0.2500 staging and retains preview designation', () => {
  assert.match(source('Opsis.Studio.csproj'), /<Import Project="Version\.props"/);
  assert.match(source('Version.props'), /<OpsisAppVersion>2\.8\.0\.2500<\/OpsisAppVersion>/);
  assert.match(source('Version.props'), /<InformationalVersion>\$\(OpsisAppVersion\)-source-preview<\/InformationalVersion>/);
  assert.match(source('installer.iss'), /GetFileVersion\(PublishDir/);
});


test('new DLL plugins use 1.0.0.0 while shared host ABI remains versioned independently', () => {
  const workflow = source('.github/workflows/plugin-compatibility.yml');
  assert.match(workflow, /plugin-crawler\/OpsisNewsCrawler\.OpsisPlugin\.dll/);
  assert.match(workflow, /plugin-chronikologio\/Chronikologio\.OpsisPlugin\.dll/);
  assert.match(workflow, /\$asm\.Version\.ToString\(\) -ne '1\.0\.0\.0'/);
  assert.match(workflow, /contract unexpectedly changed; ABI migration is required/);
  assert.match(workflow, /0\.8\.0\.0/);
});

test('runtime plugin diagnostics surface the full four-part DLL version', () => {
  const diag = source('src/Services/DesktopDiagnostics.cs');
  assert.match(diag, /versions=\{/);
  assert.match(diag, /p\.Id \+ "@" \+ p\.Version/);
  assert.match(diag, /pluginDlls=\{/);
  const workflow = source('.github/workflows/plugin-compatibility.yml');
  for (const id of ['giorstergiopoulos', 'chronikologio']) assert.ok(workflow.includes(id));
  assert.match(workflow, /@1\\\.0\\\.0\\\.0/);
});


test('published plugin style is idempotent to avoid unnecessary SSD writes and watcher wakeups', () => {
  const code = source('src/Services/PluginUiStyle.cs');
  assert.match(code, /File\.Exists\(FilePath\)/);
  assert.match(code, /style\.GetString\(\) == Current/);
  assert.match(code, /JsonDocument\.Parse/);
});


test('Media Suite family card reflects the actual converter/player/photo capabilities', () => {
  const media = catalog.apps.find(a => a.id === 'mediasuite');
  assert.match(media.tagline.en, /converter and photo editor/);
  assert.ok(media.capabilities.en.some(v => v.includes('FFmpeg')));
  assert.ok(media.capabilities.el.some(v => v.includes('υπότιτλους')));
  assert.ok(media.capabilities.en.some(v => v.includes('20 languages')));
});
