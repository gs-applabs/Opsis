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
  assert.match(host,/available\.Version >= requested\.Version/);
  assert.match(host,/RequireDependencyIdentity\(requested, host\.GetName\(\)\)/);
  assert.match(host,/AssemblyName\.ReferenceMatchesDefinition\(requested, available\)/);
  assert.match(host,/token\.SequenceEqual\(available\.GetPublicKeyToken\(\)/);
});

test('host compatibility diagnostic reports both plugin IDs and registered feeds', () => {
  const d = source('src/Services/DesktopDiagnostics.cs');
  assert.match(d,/ids=\{/);
  assert.match(d,/PluginFeeds\.All\(\)/);
});

test('version metadata uses the single 3.0.0.3200 release identity', () => {
  assert.match(source('Opsis.csproj'), /<Import Project="Version\.props"/);
  assert.match(source('Version.props'), /<OpsisAppVersion>3\.0\.0\.3200<\/OpsisAppVersion>/);
  assert.match(source('Version.props'), /<InformationalVersion>\$\(OpsisAppVersion\)<\/InformationalVersion>/);
  assert.doesNotMatch(source('Version.props'), /source-preview/);
  assert.match(source('installer.iss'), /GetFileVersion\(PublishDir/);
});


test('new DLL plugins use 1.0.0.0 while shared host ABI remains versioned independently', () => {
  const workflow = source('.github/workflows/plugin-compatibility.yml');
  const gate = source('tools/check-plugin-dll-versions.ps1');
  assert.match(workflow, /check-plugin-dll-versions\.ps1 -CrawlerDir plugin-crawler -ChronikologioDir plugin-chronikologio -HostDir publish/);
  assert.match(gate, /File='OpsisNewsCrawler\.OpsisPlugin\.dll'; Assembly='OpsisNewsCrawler\.OpsisPlugin'; Version='1\.0\.0\.0'/);
  assert.match(gate, /File='Chronikologio\.OpsisPlugin\.dll'; Assembly='Chronikologio\.OpsisPlugin'; Version='1\.0\.0\.0'/);
  assert.match(gate, /\$identity\.Version\.ToString\(\) -ne \$spec\.Version/);
  assert.match(gate, /File='OpsisNewsCrawler\.Plugin\.Abstractions\.dll'; Assembly='OpsisNewsCrawler\.Plugin\.Abstractions'; Version='1\.0\.0\.0'/);
  assert.match(gate, /File='Opsis\.UI\.dll'; Assembly='Opsis\.UI'; Version='0\.1\.0\.0'/);
  assert.match(workflow, /Get-FileHash -LiteralPath \$group\.host -Algorithm SHA256/);
  assert.match(workflow, /Incompatible shared DLL/);
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

test('compatibility workflow pins reproducible provider commits and exact host source', () => {
  const workflow = source('.github/workflows/plugin-compatibility.yml');
  for (const provider of ['crawler', 'chronikologio']) {
    const input = workflow.match(new RegExp("inputs\\." + provider + "_ref \\|\\| '([a-f0-9]{40})'"));
    assert.ok(input, provider + ' must pin a full SHA');
    assert.ok(workflow.includes("default: '" + input[1] + "'"), provider + ' dispatch default must match PR pin');
  }
  assert.match(workflow, /repository: gs-applabs\/OpsisMediaSuite\s+ref: '[a-f0-9]{40}'/);
  assert.match(workflow, /ref: \$\{\{ github\.event\.pull_request\.head\.sha \|\| github\.sha \}\}/);
  assert.match(workflow, /compat-results\/source-identities\.json/);
});

test('PowerShell ABI gate uses terminating errors rather than stale native exit status', () => {
  const workflow = source('.github/workflows/plugin-compatibility.yml');
  const start = workflow.indexOf('name: Verify plugin 1.0.0.0 and host ABI identities');
  const end = workflow.indexOf('name: Verify exact shared assembly identities', start);
  assert.ok(start >= 0 && end > start);
  const step = workflow.slice(start,end);
  assert.match(step, /\$ErrorActionPreference = 'Stop'/);
  assert.match(step, /& \.\/tools\/check-plugin-dll-versions\.ps1/);
  assert.doesNotMatch(step, /if\s*\(\s*\$LASTEXITCODE/);
  const gate = source('tools/check-plugin-dll-versions.ps1');
  assert.match(gate, /throw .*incompatible assembly identity/);
  assert.match(gate, /throw .*FileVersion=/);
});
