'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const root = path.resolve(__dirname, '../../src/Widgets/wallpaper');
function fakeContext(log) {
    const shade = () => ({ addColorStop() {} });
    const obj = {
        save(){},restore(){},setTransform(){},beginPath(){},clip(){},moveTo(){},
        lineTo(){},closePath(){},fill(){},stroke(){},clearRect(){},fillRect(){},
        arc(){},ellipse(){},quadraticCurveTo(){},drawImage(){log.paints++;},
        translate(){},scale(){},createRadialGradient:shade,createLinearGradient:shade
    };
    return obj;
}
function harness() {
    const log = { paints:0, urls:[], canvases:0 };
    const window = {};
    const document = {
        createElement(type) {
            assert.equal(type, 'canvas');
            log.canvases++;
            return { width:0,height:0,getContext(){return fakeContext(log);} };
        }
    };
    class Image {
        constructor() { this.naturalWidth=1920;this.naturalHeight=1080; }
        set src(url) {this._src=url;log.urls.push(url);}
        get src(){return this._src;}
        decode(){return Promise.resolve();}
    }
    const sandbox = { window, document, Image, Date, Intl, Math, Promise, console, Object, Map, Set };
    vm.runInNewContext(fs.readFileSync(path.join(root,'ancient-athens-effects.js'),'utf8'), sandbox,
        { filename:'ancient-athens-effects.js' });
    vm.runInNewContext(fs.readFileSync(path.join(root,'ancient-athens.js'),'utf8'), sandbox,
        { filename:'ancient-athens.js' });
    return { fx:window.OpsisAncientAthensFx, athens:window.OpsisAncientAthens, log, ctx:fakeContext(log) };
}
test('Athens remains a built-in WebView2 canvas renderer; day-to-night stages are consistent', () => {
    const {athens}=harness();
    const noon=athens.phaseAt(new Date('2026-06-21T09:00:00Z'));
    const midnight=athens.phaseAt(new Date('2026-06-21T00:00:00Z'));
    assert.equal(noon.from,'day');
    assert.equal(midnight.from,'night');
    assert.equal(athens.phaseAt(new Date(),'Night').from,'night');
    assert.equal(athens.phaseAt(new Date(),'Day').from,'day');
});
test('Athens uses cloud/wind, rain, snow, storms and preserves cached state on network failures', () => {
    const {athens,fx}=harness();
    assert.equal(athens.setWeather({ok:true,code:95,cloud:100,wind:35,source:'cache'}),true);
    assert.equal(fx._state().target.storm,.75);
    assert.equal(fx._state().target.wind,35);
    assert.equal(fx._state().target.cloud,1);
    assert.equal(athens.setWeather({ok:false,code:0}),false);
    assert.equal(fx._state().target.storm,.75);
    assert.equal(athens.setWeather({ok:true,code:73,wind:9}),true);
    assert.equal(fx._state().target.snow,.68);
});
test('Night Acropolis lamps are suppressed by a masked, graded day texture; legacy JPEG fallback is retained', async () => {
    const {athens,log,ctx}=harness();
    await athens.prepare(1920,1080,'Night');
    athens.draw(ctx,1920,1080,'Night',true,{now:new Date('2026-11-24T22:00:00Z')});
    assert.ok(log.urls.some(u=>u.includes('night-4k.webp')));
    assert.ok(log.urls.some(u=>u.includes('day-4k.webp')));
    assert.ok(log.canvases>=3,'blended composite and Acropolis color/alpha masks');
    assert.ok(log.paints>3);
    athens.draw(ctx,1920,1080,'Night',false,{now:new Date('2026-11-24T22:00:00Z'),reducedMotion:true,lowPower:true});
    athens.release();
});
test('Sun, moon, clouds, birds and precipitation do not require a network or third party library', () => {
    const {fx,ctx,log}=harness();
    assert.doesNotThrow(()=>fx.render(ctx,1280,720,{date:new Date(),minutes:780,sr:390,ss:1090,delta:1}));
    fx.setWeather({ok:true,code:65,wind:20,cloud:100});
    for(let i=0;i<20;i++) fx.render(ctx,1280,720,{date:new Date(),minutes:780,sr:390,ss:1090,delta:1,lowPower:true});
    assert.ok(fx._state().current.rain>.1);
    fx.setWeather({ok:true,code:0});
    assert.doesNotThrow(()=>fx.render(ctx,1280,720,{date:new Date(),minutes:30,sr:390,ss:1090,delta:.4,reducedMotion:true}));
    assert.equal(typeof fx.clear,'function');
});
test('Ancient Athens weather is fixed to Athens coordinates, not the user selected city', () => {
    const bridge = fs.readFileSync(path.resolve(__dirname,'../../src/Widgets/WallpaperWindow.cs'),'utf8');
    const index = fs.readFileSync(path.join(root,'index.html'),'utf8');
    assert.match(bridge,/AncientAthensLat = 37\.9715/);
    assert.match(bridge,/AncientAthensLon = 23\.7267/);
    assert.match(index,/GetAncientAthensWeatherJson/);
    assert.match(index,/RequestAncientAthensWeatherRefresh/);
    assert.match(index,/usesAtmosphere\(mode\)/);
    assert.match(index,/ancient-athens-effects\.js/);
});

test('MacLike uses one Weather engine with cached masked foreground and isolated city state', async () => {
    const log = { foreground:0, legacy:0, paints:0, masks:0 };
    const shade=()=>({addColorStop(){}});
    const context=()=>new Proxy({
        createRadialGradient:shade, createLinearGradient:shade,
        createImageData:(w,h)=>({data:new Uint8ClampedArray(w*h*4)}),
        getImageData:(x,y,w,h)=>({data:new Uint8ClampedArray(w*h*4)}),
        drawImage(){log.paints++;}, measureText(){return {width:12};},
        createPattern(){return {};}, globalAlpha:1
    },{get:(t,k)=>k in t?t[k]:(()=>{})});
    const document={createElement(){return {width:0,height:0,getContext:context};}};
    const window={OpsisSkyMask:{
        detect(){log.masks++;return {width:512,height:288};},
        refinePhotoSky(image,mask){return mask;}
    }};
    class Image { constructor(){this.naturalWidth=1920;this.naturalHeight=1080;} decode(){return Promise.resolve();} }
    const sandbox={window,document,Image,Date,Intl,Math,Promise,performance:{now:()=>100}};
    for(const file of ['ancient-athens.js','weather.js'])
        vm.runInNewContext(fs.readFileSync(path.join(root,file),'utf8'),sandbox,{filename:file});
    const athens=window.OpsisAncientAthens,original=athens.draw;
    athens.draw=(...args)=>{
        if(args[5]?.sharedWeather)log.foreground++;
        else log.legacy++;
        return original(...args);
    };
    await athens.prepare(640,360,'Day');
    const wx=window.WeatherWallpaper;
    wx.resize(640,360);
    wx.setConfig({historicalAthens:true,athensPhase:'Day',timeSim:'Day',lat:37.9715,lon:23.7267,
        weatherSim:'Rain',realistic:true,glass:false,spatialMode:'Off'});
    for(let i=0;i<3;i++)wx.draw(context(),.1,{id:'Balanced'});
    assert.equal(log.foreground,3);
    assert.equal(log.legacy,0);
    assert.equal(log.masks,1);
    assert.ok(wx._state().TGT.rain>0);
    wx.setConfig({historicalAthens:false,lat:51.5,lon:-.12,weatherSim:'Clear',timeSim:'Auto'});
    assert.equal(wx._state().TGT.rain,0);
    assert.equal(wx.info().temp,null);
});
