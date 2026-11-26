const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const html = fs.readFileSync(path.join(__dirname, '..', 'src', 'Widgets', 'wallpaper', 'index.html'), 'utf8');

test('desktop time / weather / place text honours the automatic text colour switch', () => {
  assert.match(html, /card \|\| cfg\.elAuto === false/);          // switch off (or card layout): always light text, never black letters
  assert.match(html, /box\.style\.color = '#ffffff'/);
});

test('dark text only on a clearly bright backdrop, with hysteresis', () => {
  assert.match(html, /wasDarkText \? luminosity < 0\.32 : luminosity < 0\.45/);
  assert.doesNotMatch(html, /const useWhite = whiteRatio >= blackRatio;/);
});

test('the backdrop is not read back every second', () => {
  assert.match(html, />= 15000/);
  assert.match(html, /box\.dataset\.sample !== sampleKey/);
});
