'use strict';

const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');
const assert = require('node:assert/strict');

const root = path.join(__dirname, '../src/Widgets/wallpaper');
function fixture() {
    let created = 0, missing = false, invalid = false;
    const calls = { buffers: 0, programs: 0, deleted: 0, draws: 0, photos: 0 };
    const images = [];
    const handlers = new Map();
    const gl = new Proxy({
        createBuffer() { calls.buffers++; return {}; },
        createProgram() { calls.programs++; return {}; },
        deleteProgram() { calls.deleted++; },
        createShader: () => ({}),
        createTexture: () => ({}),
        getShaderParameter: () => true,
        getProgramParameter: () => !invalid,
        getProgramInfoLog: () => 'mock shader link error',
        getUniformLocation: (_, name) => name,
        isContextLost: () => missing,
        drawArrays() { calls.draws++; },
        texImage2D(...args) { if (args.length === 6) calls.photos++; }
    }, { get: (object, key) => object[key] ?? (() => {}) });

    const canvas = {
        width: 0, height: 0, getContext: () => gl,
        addEventListener(name, handler) { handlers.set(name, handler); }
    };
    const sandbox = vm.createContext({
        document: { createElement() { created++; return canvas; } },
        Image: class {
            constructor() { images.push(this); this.naturalWidth = 1200; this.naturalHeight = 675; this.complete = true; }
        },
        console: { warn() {} }
    });
    for (const file of ['studio-landscapes.js', 'studio-scenes.js'])
        vm.runInContext(fs.readFileSync(path.join(root, file), 'utf8'), sandbox, { filename: file });

    let image;
    const context = {
        save() {}, restore() {}, setTransform() {}, fillRect() {},
        drawImage(value) { image = value; }
    };
    return {
        calls, images, created: () => created,
        draw(id, w = 800, h = 450) { sandbox.OpsisStudioScenes.draw(context, id, w, h, 1.5); return image; },
        lose() {
            missing = true;
            let recoveryEnabled = false;
            handlers.get('webglcontextlost')?.({ preventDefault() { recoveryEnabled = true; } });
            assert.ok(recoveryEnabled);
        },
        restore() { missing = false; handlers.get('webglcontextrestored')?.(); },
        failLink(flag) { invalid = flag; }
    };
}

test('fall back during context loss, then restore the same scene and photo', () => {
    const f = fixture();
    f.draw('Studio Forest', 0, 0);
    assert.equal(f.created(), 0);
    f.draw('Studio Forest');
    f.images[0].onload();
    f.draw('Studio Forest');
    assert.equal(f.calls.photos, 1);
    assert.equal(f.calls.draws, 2);

    f.lose();
    assert.equal(f.draw('Studio Forest').src, 'gallery/forest.jpg');
    assert.equal(f.calls.draws, 2);
    f.restore();
    f.draw('Studio Forest');
    const reloaded = f.images.findLast(image => image.src === 'gallery/plates/forest.webp');
    assert.notEqual(reloaded, f.images[0]);
    reloaded.onload();
    f.draw('Studio Forest');

    assert.equal(f.created(), 1);
    assert.deepEqual([f.calls.buffers, f.calls.programs, f.calls.draws, f.calls.photos], [2, 2, 4, 2]);
});

test('release a failed shader program and retry after a context restore', () => {
    const f = fixture();
    f.failLink(true);
    assert.equal(f.draw('Studio Aurora').src, 'gallery/aurora.jpg');
    assert.equal(f.calls.deleted, 1);
    f.failLink(false);
    assert.equal(f.draw('Studio Aurora').src, 'gallery/aurora.jpg');
    assert.equal(f.calls.programs, 1);
    f.lose();
    f.restore();
    f.draw('Studio Aurora');
    assert.equal(f.calls.draws, 1);
});
