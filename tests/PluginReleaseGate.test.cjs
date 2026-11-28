'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
test('plugin ABI version gate covers both plugins and both host shared libraries', () => {
  const s=read('tools/check-plugin-dll-versions.ps1');
  for(const name of ['OpsisNewsCrawler.OpsisPlugin.dll','Chronikologio.OpsisPlugin.dll','Opsis.UI.dll','OpsisNewsCrawler.Plugin.Abstractions.dll']) assert.ok(s.includes(name),name);
  assert.ok(s.includes("Version='1.0.0.0'"));
  assert.match(s, /File='OpsisNewsCrawler\.Plugin\.Abstractions\.dll'; Assembly='OpsisNewsCrawler\.Plugin\.Abstractions'; Version='1\.0\.0\.0'/);
  assert.ok(s.includes("Version='0.1.0.0'"));
  assert.match(read('.github/workflows/plugin-compatibility.yml'), /check-plugin-dll-versions\.ps1/);
});
test('Windows installer artifact is gated by full UI audit',()=>{
 const w=read('.github/workflows/windows-audit.yml');
 const audit = w.indexOf('Audit the application and taskbar registry');
 const artifact = w.indexOf('UNRELEASED-audited-build');
 assert.ok(audit >= 0 && artifact > audit);
 assert.match(w, /if: success\(\)\s+with:\s+name: Opsis-\$\{\{ github\.run_id \}\}-UNRELEASED-audited-build\s+path: Output\//);
});
