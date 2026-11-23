const assert = require('node:assert/strict');
const test = require('node:test');
const { readFileSync } = require('node:fs');
const { runInNewContext } = require('node:vm');
const context = { window: {} };
runInNewContext(readFileSync(require.resolve('../src/Widgets/wallpaper/weather-freshness.js'), 'utf8'), context);
const { format } = context.window.OpsisWeatherFreshness;
const now = Date.parse('2026-11-23T11:00:00Z');
test('shows source and observed age in Greek and English', () => {
    const i = { source: 'Open-Meteo', observedAt: '2026-11-23T10:51:00Z', stale: false };
    assert.equal(format(i, now, 'el'), 'Open-Meteo · 9 λ. πριν');
    assert.equal(format(i, now, 'en'), 'Open-Meteo · 9 min ago');
});
test('marks aged or explicitly stale observations rather than hiding offline state', () => {
    assert.match(format({ source: 'MET Norway', observedAt: '2026-11-23T08:51:00Z' }, now, 'el'), /Παλαιά δεδομένα/);
    assert.match(format({ source: 'wttr.in', fetchedAt: '2026-11-23T10:59:00Z', stale: true }, now, 'en'), /Outdated data/);
});
test('handles invalid and future timestamps and an empty cache', () => {
    assert.equal(format(null, now, 'en'), '');
    assert.equal(format({ source: 'wttr.in', observedAt: 'nonsense' }, now, 'en'), 'Source: wttr.in');
    assert.equal(format({ source: 'Open-Meteo', observedAt: '2026-11-24T00:00:00Z' }, now, 'en'), 'Open-Meteo · 0 min ago');
});
