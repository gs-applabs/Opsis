// Node built-in test runner; DOM and GPU context are simulated safely.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const src = fs.readFileSync(path.join(__dirname,'../../src/Widgets/wallpaper/weather-spatial.js'),'utf8');

test('Off and 2.5D modes never request a GPU context', () => {
  let createCount = 0;
  const el = { style:{}, setAttribute(){}, remove() {} };
  const waves = { parentNode:{ insertBefore(){createCount++;} }, nextSibling:null };
  const sandbox = {window:{},document:{
    getElementById(){return waves;},
    createElement(){return el;}
  },Math,Float32Array};
  vm.runInNewContext(src,sandbox);
  const spatial=sandbox.window.OpsisWeatherSpatial;
  spatial.setMode('Off'); spatial.update(.016,{rain:1,snow:0,seconds:2},'High',false);
  spatial.setMode('2.5D'); spatial.update(.016,{rain:1,snow:0,seconds:2},'High',false);
  assert.equal(createCount,0);
  assert.equal(spatial.mode(),'2.5D');
});

test('GPU unavailable safely falls back without creating an active overlay', () => {
  const el = {style:{},setAttribute(){},remove(){this.removed=true;},getContext(){return null;}};
  const waves = { parentNode:{ insertBefore(){ } }, nextSibling:null };
  const sandbox={window:{innerWidth:1920,innerHeight:1080},document:{
    getElementById(){return waves;},createElement(){return el;}
  },Math,Float32Array};
  vm.runInNewContext(src,sandbox);
  const spatial=sandbox.window.OpsisWeatherSpatial;
  spatial.setMode('3D');
  assert.doesNotThrow(()=>spatial.update(.016,{rain:1,snow:0,seconds:2},'Balanced',false));
  assert.equal(spatial.supported(),false);
  assert.equal(el.removed,true);
  spatial.dispose();
  assert.equal(spatial.supported(),true);
});

test('reduced motion and low-power profiles skip GPU allocation', () => {
  let created=0;
  const sandbox={window:{},document:{createElement(){created++;return {};},getElementById(){return {};}},Math,Float32Array};
  vm.runInNewContext(src,sandbox);
  const spatial=sandbox.window.OpsisWeatherSpatial;
  spatial.setMode('3D');
  spatial.update(.016,{rain:1},'High',true);
  spatial.update(.016,{rain:1},'Low Power',false);
  spatial.update(.016,{rain:1},'Battery',false);
  assert.equal(created,0);
});

test('both quality tiers build complete finite triangles for every Greek scene and manual landmark', () => {
  const folder=path.join(__dirname,'../../src/Widgets/wallpaper');
  const context={window:{}};
  vm.runInNewContext(fs.readFileSync(path.join(folder,'weather-landscape.js'),'utf8'),context);
  const {build}=context.window.OpsisWeatherLandscape;
  const cities=['capitals','islands'].flatMap(name=>JSON.parse(fs.readFileSync(path.join(folder,'scenes',name+'.json'))));
  const manual=['rioantirrio','patras-lighthouse','nafplio-bourtzi','mountain-lake'];
  for (const city of [...cities,...manual.map(id=>({id}))]) {
    const balanced=build(city.id,city,false), high=build(city.id,city,true);
    assert.ok(balanced.length>0 && high.length>balanced.length,city.id);
    for(const data of [balanced,high]) {
      assert.equal(data.length%18,0,city.id);
      assert.ok(data.every(Number.isFinite),city.id);
    }
  }
});

test('photo grade SDR tone mapping is bounded and monotonic', () => {
  const sandbox={window:{}};
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../../src/Widgets/wallpaper/weather-atmosphere.js'),'utf8'),sandbox);
  const a=sandbox.window.OpsisWeatherAtmosphere;
  assert.equal(a.toneMap(0),0); assert.equal(a.toneMap(1),1);
  let previous=0;
  for(let n=0;n<=100;n++){const mapped=a.toneMap(n/100); assert.ok(mapped>=previous && mapped<=1); previous=mapped;}
  assert.ok(a.photoGrade(.8,0,{storm:0,rain:0,fog:0}).exposure > a.photoGrade(.1,0,{storm:0,rain:0,fog:0}).exposure);
});
