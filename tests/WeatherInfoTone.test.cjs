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
