const assert = require('node:assert/strict');
const test = require('node:test');
const { readFileSync } = require('node:fs');
const { runInNewContext } = require('node:vm');

function scene() {
    const context = { window: {}, Date, Math };
    const source = readFileSync(require.resolve('../src/Widgets/wallpaper/weather.js'), 'utf8');
    runInNewContext(source, context, { filename: 'weather.js' });
    return context.window.WeatherWallpaper;
}

test('offline or pending refresh retains the last valid observation and visual target', () => {
    const wallpaper = scene();
    wallpaper.setWeather({ ok: true, temp: 17.4, wind: 31, windDir: 270, code: 63,
        source: 'Open-Meteo', observedAt: '2026-11-23T10:00:00Z', stale: false });
    assert.equal(wallpaper.info().temp, 17.4);
    const targetBefore = wallpaper._state().TGT.rain;
    assert.ok(targetBefore > 0);
    wallpaper.setWeather({ ok: false, pending: true });
    assert.equal(wallpaper.info().temp, 17.4);
    assert.equal(wallpaper._state().TGT.rain, targetBefore);
    wallpaper.setWeather({ ok: false });
    assert.equal(wallpaper.info().source, 'Open-Meteo');
    wallpaper.setWeather({ ok: true, temp: 17.4, wind: 31, windDir: 270, code: 63,
        source: 'Open-Meteo', stale: true });
    assert.equal(wallpaper.info().stale, true);
});

test('changing weather coordinates clears the previous city observation', () => {
    const wallpaper = scene();
    wallpaper.setWeather({ ok: true, temp: 17.4, code: 2, source: 'Open-Meteo' });
    assert.equal(wallpaper.info().temp, 17.4);
    wallpaper.setConfig({ lat: 38.2466, lon: 21.7346 });
    assert.equal(wallpaper.info().temp, null);
    assert.equal(wallpaper.info().source, null);
});
