const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const html = fs.readFileSync(path.join(__dirname, '..', 'src', 'Widgets', 'wallpaper', 'index.html'), 'utf8');

test('desktop time / weather / place text honours the automatic text colour switch', () => {
  assert.match(html, /cfg\.elAuto === false/);                       // switch off: always light text, never black letters
  assert.match(html, /box\.style\.color = '#fff'/);
});

test('dark text only on a clearly bright backdrop, with hysteresis', () => {
  assert.match(html, /wasDarkText \? luminosity < 0\.32 : luminosity < 0\.45/);
  assert.doesNotMatch(html, /const useWhite = whiteRatio >= blackRatio;/);
});

test('outside the sampled weather modes the inline colour is cleared (no stale black text)', () => {
  assert.match(html, /else \{ box\.style\.color = ''; box\.style\.textShadow = ''; delete box\.dataset\.tone; \}/);
});
