const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const html = fs.readFileSync(path.join(__dirname, '..', 'src', 'Widgets', 'wallpaper', 'index.html'), 'utf8');

test('desktop time / weather / place text is always white with a fixed dark outline (no black letters, no GPU read-back)', () => {
  assert.match(html, /box\.style\.color = '#ffffff'/);
  assert.match(html, /box\.dataset\.contrast = 'white-fixed'/);
  assert.doesNotMatch(html, /box\.style\.color = useWhite \? '#fff' : '#111'/);
});

test('fixed-white policy applies to both Signature card and plain desktop labels', () => {
  const start = html.indexOf('// R20: white labels and fixed dark outline');
  assert.ok(start >= 0);
  const block = html.slice(start, html.indexOf('// fade in once', start));
  assert.match(block, /box\.style\.color = '#ffffff'/);
  assert.match(block, /box\.style\.textShadow = card \?/);
  assert.match(block, /box\.dataset\.contrast = 'white-fixed'/);
  assert.doesNotMatch(block, /getImageData|readPixels|luminosity|useWhite/);
});

const music = fs.readFileSync(path.join(__dirname, '..', 'src', 'Music', 'wallpaper-music.js'), 'utf8');
const placementSource = music.slice(music.indexOf('function placeMusicOverlay('), music.indexOf('function paint(frame)'));
const place = require('node:vm').runInNewContext(placementSource + '\nplaceMusicOverlay');
test('music keeps preferred position if the weather card does not overlap', () => {
  const p = place(1920,1080,480,230,60,780,{left:750,top:200,right:1170,bottom:480},12);
  assert.equal(p.left,60); assert.equal(p.top,780);
});
test('music moves to a safe rectangle when clock and weather collide', () => {
  const weather = {left:550,top:240,right:1370,bottom:710};
  const p = place(1920,1080,480,230,780,380,weather,12);
  assert.ok(p);
  assert.ok(p.left+480 <= weather.left-12 || p.left >= weather.right+12 ||
    p.top+230 <= weather.top-12 || p.top >= weather.bottom+12);
  assert.ok(p.left>=12 && p.top>=12 && p.left+480<=1908 && p.top+230<=1068);
});
test('music is hidden if no safe space exists, rather than covering weather', () => {
  assert.equal(place(640,480,440,280,100,100,{left:10,top:10,right:630,bottom:470},12),null);
});
test('music placement respects portrait display and changed weather bounds', () => {
  const p = place(768,1366,420,220,174,500,{left:100,top:300,right:668,bottom:690},12);
  assert.ok(p && (p.top+220<=288 || p.top>=702));
});

test('music geometry is bounded and non-overlapping across 10800 layouts', () => {
    let count = 0;
    for (const [vw,vh] of [[640,480],[768,1366],[1920,1080],[2560,1440],[3840,2160]])
    for (const w of [120,240,480,720,1200]) for (const h of [100,230,450,700])
    for (const x of [0,.5,1]) for (const y of [0,.5,.88,1])
    for (const wx of [0,.3,.8]) for (const wy of [0,.34,.8]) {
        const weather = {left:wx*vw,top:wy*vh,right:Math.min(vw,(wx+.45)*vw),bottom:Math.min(vh,(wy+.40)*vh)};
        const p = place(vw,vh,w,h,x*(vw-w),y*(vh-h),weather,12);
        count++;
        if (!p) continue;
        assert.ok(p.left>=12 && p.top>=12 && p.left+w<=vw-12 && p.top+h<=vh-12);
        assert.ok(p.left+w<=weather.left-12 || p.left>=weather.right+12 ||
            p.top+h<=weather.top-12 || p.top>=weather.bottom+12);
    }
    assert.equal(count,10800);
});
