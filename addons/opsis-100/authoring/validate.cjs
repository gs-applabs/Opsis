'use strict';
// Authoring checks are intentionally NOT equivalent to an add-on Release gate.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const base = __dirname;
const europa = JSON.parse(fs.readFileSync(path.join(base,'europe-capitals.json'),'utf8'));
const rome = JSON.parse(fs.readFileSync(path.join(base,'ancient-rome.theme.json'),'utf8'));
assert.equal(europa.count,50);
assert.equal(europa.scenes.length,50);
assert.equal(rome.id,'ancient-rome-320');
assert.equal(rome.distribution,'opsis-100-add-on');
assert.equal(rome.builtIn,false);
assert.deepEqual(europa.coreBuiltInThemesExcluded,['Ancient Athens']);
const ids = new Set();
const paths = new Set();
for (const scene of europa.scenes) {
  assert.match(scene.id,/^eu-[a-z][a-z0-9-]+$/);
  assert.match(scene.file,/^scenes\/europe\/[a-z][a-z0-9-]+\.webp$/);
  assert.match(scene.poster,/^posters\/europe\/[a-z][a-z0-9-]+\.webp$/);
  assert.ok(scene.name && scene.nameEn && scene.country && scene.landmark);
  assert.ok(Number.isFinite(scene.lat) && scene.lat>=-90 && scene.lat<=90);
  assert.ok(Number.isFinite(scene.lon) && scene.lon>=-180 && scene.lon<=180);
  assert.ok(!ids.has(scene.id),'duplicate ID '+scene.id);ids.add(scene.id);
  assert.ok(!paths.has(scene.file),'duplicate photo '+scene.file);paths.add(scene.file);
  assert.equal(scene.assetStatus,'required-not-yet-delivered');
}
assert.ok(ids.has('eu-athens') && ids.has('eu-rome'));
assert.ok(!ids.has('ancient-athens'));
const requiredNames = new Set(['dawn.webp','day.webp','golden.webp','blue.webp','night.webp']);
assert.ok(requiredNames.isSubset(new Set(rome.textureRequirements.static)));
const required = process.argv.includes('--release');
if (required) {
  // Optional-addon contents are not in the host. Always check the *installed*
  // packaging root, never an unrelated source directory.
  const pack=process.env.OPSIS_WALLPAPER_PACK_ROOT ||
    path.resolve(base,'../../../wallpapers/packs/opsis-100');
  const checksum=path.join(pack,'sha256.json');
  assert.ok(fs.existsSync(checksum),'Pack is missing sha256.json: not releasable');
  const hashes=JSON.parse(fs.readFileSync(checksum,'utf8'));
  const readWebP = relative => {
    const full=path.resolve(pack,relative);
    assert.ok(full.startsWith(pack+path.sep),'Path escapes pack root');
    assert.ok(fs.existsSync(full),'Missing WebP: '+relative);
    const data=fs.readFileSync(full);
    assert.ok(data.length>15000,'WebP asset unexpectedly tiny: '+relative);
    assert.equal(data.toString('ascii',0,4),'RIFF');
    assert.equal(data.toString('ascii',8,12),'WEBP');
    const hash=crypto.createHash('sha256').update(data).digest('hex');
    assert.equal(hashes[relative],hash,'Hash mismatch: '+relative);
  };
  for(const scene of europa.scenes){readWebP(scene.file);readWebP(scene.poster);}
  for(const variant of [...rome.textureRequirements.static,
      ...rome.textureRequirements.ultraHighResolution,
      ...rome.textureRequirements.masks,rome.textureRequirements.poster]){
    readWebP('ancient-rome-320/'+variant);
  }
  for(const scene of [...europa.scenes.map(c=>c.id),rome.id]){
    const entry=path.join(pack,scene,'index.html');
    assert.ok(fs.existsSync(entry),'Missing installable wallpaper: '+scene);
  }
}
console.log(required ?
  'PASS: 51 expansion entries have real WebP assets with matching SHA-256' :
  'PASS: 50 Europe specs + Ancient Rome; assets/installer NOT validated (authoring only)');
