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
