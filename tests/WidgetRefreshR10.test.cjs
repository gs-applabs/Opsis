const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

function runWidget(relativePath) {
  const source = fs.readFileSync(path.join(__dirname, '..', relativePath), 'utf8');
  let onMessage;
  const timers = [];
  const sandbox = {
    Date, Intl, Math, Number,
    navigator: { language: 'en-US' },
    document: {
      querySelector: () => ({ value: '', textContent: '' }),
      documentElement: { dataset: {}, style: { setProperty: () => {} } }
    },
    window: { chrome: { webview: {
      addEventListener: (kind, callback) => { if (kind === 'message') onMessage = callback; },
      postMessage: () => {}
    } } },
    clearInterval: () => {},
    setInterval: (_fn, ms) => { timers.push(ms); return timers.length; }
  };
  vm.runInNewContext(source, sandbox, { filename: relativePath });
  assert.equal(typeof onMessage, 'function');
  return { timers, onMessage };
}

test('Clock widget follows saved host refresh scaling and clamps excessive values', () => {
  const { timers, onMessage } = runWidget('widget-sdk/catalog/local-time/widget.js');
  assert.equal(timers.at(-1), 1000);
  onMessage({ data: { type: 'opsis:state', api: 1, language: 'el', dark: true, refreshPercent: 200 } });
  assert.equal(timers.at(-1), 2000);
  onMessage({ data: { type: 'opsis:refresh', refreshPercent: 25 } });
  assert.equal(timers.at(-1), 250);
  onMessage({ data: { type: 'opsis:refresh', refreshPercent: 90000 } });
  assert.equal(timers.at(-1), 4000);
});

test('Day progress widget follows individual refresh percentage, with bounds', () => {
  const { timers, onMessage } = runWidget('widget-sdk/catalog/day-progress/widget.js');
  assert.equal(timers.at(-1), 15000);
  onMessage({ data: { type: 'opsis:refresh', refreshPercent: 50 } });
  assert.equal(timers.at(-1), 7500);
  onMessage({ data: { type: 'opsis:state', api: 1, language: 'en', dark: false, accent: '#aabbcc', refreshPercent: 400 } });
  assert.equal(timers.at(-1), 60000);
});
