'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const html = fs.readFileSync(require.resolve('../src/Widgets/wallpaper/index.html'), 'utf8');
const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];
function fixture() {
    const pending = new Map();
    let id = 0, now = 0;
    function schedule(fn) { pending.set(++id, fn); return id; }
    const clear = id => pending.delete(id);
    const element = { style: {}, getContext: () => ({ setTransform() {} }), addEventListener() {}, pause() {} };
    const context = vm.createContext({
        document: { getElementById: () => element }, OpsisStudioScenes: { has: () => false },
        performance: { now: () => now }, spend: ms => { now += ms; }, console, addEventListener() {},
        setInterval: schedule, setTimeout: schedule, requestAnimationFrame: schedule,
        clearInterval: clear, clearTimeout: clear, cancelAnimationFrame: clear, innerWidth: 640, innerHeight: 480
    });
    context.window = context;
    const run = s => vm.runInContext(s, context);
    run(script.replace(/window\.motionDeskRefresh\(\);\s*$/, ''));
    return { run, pending, setNow: value => { now = value; } };
}
test('pausing cancels weather, audio, retry and frame callbacks; repeated resume is idempotent', () => {
    const { run, pending } = fixture();
    run("cfg.mode = 'Weather'; updateWeatherInfo = () => {}; startPolling(); weatherPollHandles.push(setTimeout(pollWeatherOnce, 10)); waveLoopHandle = requestAnimationFrame(waveLoop)");
    assert.equal(pending.size, 4);
    run('motionDeskSetPaused(true)');
    assert.equal(pending.size, 0);
    run('motionDeskSetPaused(false); motionDeskSetPaused(false)');
    assert.equal(pending.size, 3);
    run('motionDeskSetPaused(true)');
    assert.equal(pending.size, 0);
});
test('late weather response cannot update a paused scene or create retries', async () => {
    const { run, pending } = fixture();
    const refresh = run(`cfg.mode = 'Weather'; window.WeatherWallpaper = { setWeather() { throw Error('stale'); } };
      let finish, writes = 0; WeatherWallpaper.setWeather = () => writes++;
      bridge = () => ({ RequestWeatherRefresh() {}, GetWeatherJson: () => new Promise(r => finish = r) });
      refreshWeather()`);
    run('motionDeskSetPaused(true); finish(\'{"ok":true}\')');
    await refresh;
    assert.equal(run('writes'), 0);
    assert.equal(pending.size, 0);
});
test('slow frames lower automatic scale while a fixed profile retains its resolution', () => {
    for (const adaptive of [false, true]) {
        const { run, setNow } = fixture();
        run(`cfg.perf = {targetFps: 30, renderScale: 1, adaptive: ${adaptive}}; renderFrame = () => spend(32); resizeCanvas = () => {}`);
        for (let i = 1; i <= 360; i++) {
            setNow(i * 50 + 32);
            run(`waveLoop(${i * 50})`);
        }
        const stats = run('opsisFrameStats()');
        assert.equal(stats.samples, 300);
        assert.equal(stats.p99Ms, 50);
        assert.equal(stats.adaptiveScale, adaptive ? .625 : 1);
    }
});
test('settings bursts merge into one refresh and one trailing refresh while busy', async () => {
    const { run } = fixture();
    await run('refreshChain');
    const work = run('let calls = 0, finish; doRefresh = async () => { if (++calls === 1) await new Promise(r => finish = r) }; motionDeskRefresh(); motionDeskRefresh()');
    await Promise.resolve();
    run('motionDeskRefresh(); motionDeskRefresh(); finish()');
    await work;
    assert.equal(run('calls'), 2);
});
