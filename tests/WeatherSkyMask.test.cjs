const assert = require('node:assert/strict');
const test = require('node:test');
const { skyline } = require('../src/Widgets/wallpaper/sky-mask.js');

const width = 80;
const height = 64;
const sky = [113, 170, 225, 255];
const ground = [62, 69, 51, 255];

function analyze(pixel) {
    const pixels = new Uint8ClampedArray(width * height * 4);
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) pixels.set(pixel(x, y), (y * width + x) * 4);
    }
    return skyline(pixels, width, height);
}

test('stops at a mountain ridge and a narrow building even with sky-blue windows below', () => {
    const boundary = (x) => x === 30 ? 10 : 30 - Math.round(10 * (1 - Math.abs(x - 55) / 55));
    const rows = analyze((x, y) => y < boundary(x) || y > 33 ? sky : ground);
    assert.equal(rows[30], 7);
    assert.equal(rows[31], 7);
    assert.equal(rows[55], 17);
    assert.ok(rows.every((row, x) => row > 0 && row < boundary(x)));
});

test('preserves the side of a photo where foliage reaches the top', () => {
    const rows = analyze((x, y) => x < 15 || y >= 27 ? ground : sky);
    assert.ok(rows.slice(0, 16).every(row => row === 0));
    assert.equal(rows[25], 24);
});

test('rejects a ground-only photo and an unbroken sky-blue surface', () => {
    assert.ok(analyze(() => ground).every(row => row === 0));
    assert.ok(analyze(() => sky).every(row => row === 0));
});

test('uses the bundled Athens profile without reading local photo pixels', () => {
    const { readFileSync } = require('node:fs');
    const { runInNewContext } = require('node:vm');
    let result;
    const context = {
        window: {},
        document: { createElement: () => ({
            getContext: () => ({
                drawImage: () => assert.fail('the bundled photo must not require pixel readback'),
                createImageData: (w, h) => ({ width: w, height: h, data: new Uint8ClampedArray(w * h * 4) }),
                putImageData: data => { result = data; }
            })
        }) }
    };
    for (const script of ['scene-sky-profiles.js', 'sky-mask.js']) {
        runInNewContext(readFileSync(require.resolve('../src/Widgets/wallpaper/' + script), 'utf8'), context);
    }
    const mask = context.window.OpsisSkyMask.detect({}, 'scenes/greece/parthenon.webp');
    assert.ok(mask);
    const center = result.width >> 1;
    assert.equal(result.data[(15 * result.width + center) * 4 + 3], 255);
    assert.equal(result.data[(Math.floor(result.height * .55) * result.width + center) * 4 + 3], 0);
});

test('follows a brightening sky to the terrain instead of stopping at a horizontal colour band', () => {
    const boundary = (x) => x < width / 2 ? 32 : 39;
    const rows = analyze((x, y) => y < boundary(x)
        ? [70 + 2 * y, 120 + 2 * y, 205 + y, 255]
        : ground);
    assert.equal(rows[12], 29);
    assert.equal(rows[65], 36);
});

test('ships a valid sky profile or explicit fallback for every catalogue photo', () => {
    const { existsSync, readFileSync } = require('node:fs');
    const { join } = require('node:path');
    const { runInNewContext } = require('node:vm');
    const wallpaper = join(__dirname, '../src/Widgets/wallpaper');
    const context = { window: {} };
    runInNewContext(readFileSync(join(wallpaper, 'scene-sky-profiles.js'), 'utf8'), context);
    const profiles = context.window.OpsisSceneSkyProfiles;
    const files = new Set(['scenes/acropolis.webp', 'scenes/mountain-lake.webp']);
    for (const catalogue of ['capitals.json', 'islands.json']) {
        for (const scene of JSON.parse(readFileSync(join(wallpaper, 'scenes', catalogue), 'utf8'))) {
            if (scene.file) files.add(scene.file);
        }
    }
    assert.equal(Object.keys(profiles).length, files.size);
    for (const file of files) {
        assert.ok(existsSync(join(wallpaper, file)), file);
        assert.ok(Object.hasOwn(profiles, file), file);
        if (profiles[file] === null) continue;
        const [height, rows] = profiles[file];
        assert.ok(height >= 24 && height <= 512, file);
        assert.equal(rows.length, 512, file);
        assert.ok(rows.every(y => Number.isInteger(y) && y >= 0 && y < height * .65), file);
        assert.ok(rows.filter(Boolean).length >= rows.length / 4, file);
    }
});
