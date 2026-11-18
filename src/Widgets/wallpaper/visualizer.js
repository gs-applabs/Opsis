// Music visualizer wallpaper element: a ray spectrum (radial or linear) plus clock/date text, drawn over any wallpaper mode.
// Written for Opsis: the spectrum comes from the app's own AudioSpectrumService (WASAPI loopback) through bridge().GetAudioBandsJson().
// Configured by window.opsisApplyViz(cfg) (called from elements.js on every settings refresh); cfg.viz is built by VisualizerConfig.ToPageObject.
//
// Performance rules: one requestAnimationFrame loop that exists ONLY while the element is enabled, visible, not paused and not in the
// Battery performance mode; the audio is requested at most ~30x/s (15x/s on low tiers) with one request in flight; the per-frame path
// reuses typed arrays and pre-built colour strings (no allocations); when the loop stops the loopback capture is released (StopAudioBands).
(function () {
    'use strict';
    const root = document.createElement('div');
    root.id = 'opsisViz';
    root.style.cssText = 'position:fixed;inset:0;z-index:4;pointer-events:none;overflow:hidden;display:none';
    const bgDiv = document.createElement('div'); bgDiv.style.cssText = 'position:absolute;inset:0';
    const cv = document.createElement('canvas'); cv.style.cssText = 'position:absolute;inset:0;width:100%;height:100%';
    const clockEl = document.createElement('div'), dateEl = document.createElement('div');
    for (const t of [clockEl, dateEl]) t.style.cssText = 'position:absolute;transform:translate(-50%,-50%);white-space:nowrap;line-height:1;font-variant-numeric:tabular-nums';
    root.append(bgDiv, cv, clockEl, dateEl);
    const attach = () => { if (!root.parentNode) document.body.appendChild(root); };
    if (document.body) attach(); else document.addEventListener('DOMContentLoaded', attach);
    const ctx = cv.getContext('2d', { alpha: true });

    const MAX_RAYS = 160, BANDS = 24;
    const bands = new Float32Array(BANDS);        // last spectrum from the app (0..1 per band)
    const smooth = new Float32Array(MAX_RAYS);    // smoothed level per ray
    const idle = new Float32Array(MAX_RAYS);      // the pattern shown while nothing plays (seeded)
    let colors = [];                               // per-ray colour strings (rebuilt only when the config / ray count changes)
    let conf = null, perf = null, perfMode = '', lang = 'en';
    let paused = false, raf = 0, lastFrame = 0, lastPull = 0, pending = false, capturing = false;
    let silentSince = 0, alpha = 1, avgLevel = 0, idleT = 0;
    let rays = 60, W = 0, H = 0, dpr = 1, cx = 0, cy = 0, R = 0;
    let glowGrad = null, clockTimer = 0, fmtTime = null, fmtDate = null, fmtWeek = null;
    let vid = null, lastBgKey = '';

    const bridge = () => (typeof window.bridge === 'function' ? window.bridge() : null);
    const batteryMode = () => perfMode === 'Battery';
    const wantsRays = () => !!(conf && conf.enabled && conf.showRays && !paused && !document.hidden && !batteryMode());

    // ---- helpers -------------------------------------------------------------------------------------------------------------------------
    function hex(c) { c = c.length === 4 ? '#' + c[1] + c[1] + c[2] + c[2] + c[3] + c[3] : c; return [parseInt(c.slice(1, 3), 16), parseInt(c.slice(3, 5), 16), parseInt(c.slice(5, 7), 16)]; }
    function lerpHex(a, b, t) { const A = hex(a), B = hex(b); const m = i => Math.round(A[i] + (B[i] - A[i]) * t).toString(16).padStart(2, '0'); return '#' + m(0) + m(1) + m(2); }
    function rng(seed) { let s = (seed | 0) + 0x6D2B79F5; return () => { s = (s + 0x6D2B79F5) | 0; let t = Math.imul(s ^ (s >>> 15), 1 | s); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }

    function rebuild() {
        if (!conf) return;
        const scale = (perf && perf.renderScale) || 1;
        dpr = Math.max(0.5, (window.devicePixelRatio || 1) * Math.min(1, scale));
        W = Math.max(1, Math.round(window.innerWidth * dpr)); H = Math.max(1, Math.round(window.innerHeight * dpr));
        if (cv.width !== W) cv.width = W;
        if (cv.height !== H) cv.height = H;
        rays = conf.interpolate ? 120 : 60;
        if (conf.layout === 'Linear') rays = conf.interpolate ? 96 : 48;
        if (conf.shadow && perf && perf.glow === false) rays = Math.min(rays, 60);
        cx = W * conf.x / 100; cy = H * (1 - conf.y / 100); R = Math.min(W, H) * 0.17;
        // colours: rainbow around the circle (or along the line), or the user's two colours (symmetric, so the seam is invisible)
        colors = new Array(rays);
        for (let i = 0; i < rays; i++) {
            const u = conf.layout === 'Linear' ? i / (rays - 1) : (i / rays < 0.5 ? i / rays * 2 : (1 - i / rays) * 2);
            colors[i] = conf.gradient ? lerpHex(conf.c1, conf.c2, u) : 'hsl(' + Math.round(u * 300) + ',100%,55%)';
        }
        const r = rng(conf.seed);
        for (let i = 0; i < MAX_RAYS; i++) idle[i] = 0.12 + r() * 0.3;
        glowGrad = null;
        if (conf.whiteInside) {
            if (conf.layout === 'Linear') { glowGrad = ctx.createLinearGradient(0, cy - R * 0.5, 0, cy + R * 0.2); glowGrad.addColorStop(0, 'rgba(255,255,255,0)'); glowGrad.addColorStop(1, 'rgba(255,255,255,.55)'); }
            else { glowGrad = ctx.createRadialGradient(cx, cy, R * 0.2, cx, cy, R * 1.5); glowGrad.addColorStop(0, 'rgba(255,255,255,.95)'); glowGrad.addColorStop(0.55, 'rgba(255,255,255,.35)'); glowGrad.addColorStop(1, 'rgba(255,255,255,0)'); }
        }
    }

    // ---- audio ---------------------------------------------------------------------------------------------------------------------------
    function pull(now) {
        const every = (perf && perf.targetFps && perf.targetFps < 40) ? 66 : 33;
        if (pending || now - lastPull < every) return;
        const b = bridge(); if (!b) return;
        lastPull = now; pending = true; capturing = true;
        Promise.resolve(b.GetAudioBandsJson()).then(s => {
            pending = false;
            const a = JSON.parse(s);
            for (let i = 0; i < BANDS; i++) bands[i] = i < a.length ? a[i] : 0;
        }).catch(() => { pending = false; });
    }
    function releaseCapture() {
        if (!capturing) return;
        capturing = false; pending = false; bands.fill(0);
        try { const b = bridge(); if (b && b.StopAudioBands) b.StopAudioBands(); } catch (x) { /* ignore */ }
    }

    // ---- drawing -------------------------------------------------------------------------------------------------------------------------
    function sample(u) {                      // u 0..1 across the spectrum, linear interpolation between the bands
        const p = u * (BANDS - 1), i = p | 0, f = p - i;
        return bands[i] * (1 - f) + bands[Math.min(BANDS - 1, i + 1)] * f;
    }

    function frame(ts) {
        raf = 0;
        if (!wantsRays()) { stopLoop(); return; }
        raf = requestAnimationFrame(frame);
        const fps = Math.min(60, (perf && perf.targetFps) || 60);
        if (ts - lastFrame < 1000 / fps - 2) return;
        const dt = Math.min(0.1, (ts - lastFrame) / 1000); lastFrame = ts; idleT += dt;
        pull(ts);

        let mx = 0; for (let i = 0; i < BANDS; i++) if (bands[i] > mx) mx = bands[i];
        const silent = mx < 0.03;
        if (!silent) silentSince = 0; else if (!silentSince) silentSince = ts;
        const quiet = silent && ts - silentSince > 1200;
        const targetAlpha = quiet && conf.hideSilent ? 0 : 1;
        alpha += (targetAlpha - alpha) * Math.min(1, dt * 4);

        ctx.clearRect(0, 0, W, H);
        if (alpha < 0.01) return;

        const gain = 0.4 + conf.intensity / 99 * 1.6, add = conf.addLevel / 500 * 0.5, min = conf.minLevel / 1000, tb = conf.treble / 100 * 2;
        const linear = conf.layout === 'Linear';
        let sum = 0;
        for (let i = 0; i < rays; i++) {
            let u = linear ? i / (rays - 1) : (i / rays < 0.5 ? i / rays * 2 : (1 - i / rays) * 2);
            if (conf.reverse) u = 1 - u;
            let t;
            if (quiet) t = idle[i] * (0.7 + 0.3 * Math.sin(idleT * 0.8 + i * 0.55));
            else t = (sample(u) * (1 + tb * u)) * gain + add;
            if (t < min) t = min; if (t > 1.5) t = 1.5;
            smooth[i] += (t - smooth[i]) * (t > smooth[i] ? 0.5 : 0.14);
            sum += smooth[i];
        }
        avgLevel = sum / rays;

        ctx.globalAlpha = alpha;
        if (glowGrad) { ctx.fillStyle = glowGrad; ctx.beginPath(); if (linear) ctx.rect(cx - R * 2, cy - R * 0.5, R * 4, R * 0.7); else ctx.arc(cx, cy, R * 1.5, 0, 6.2832); ctx.fill(); }

        const clean = conf.cleanEnd, round = conf.rounded && !clean;
        const useShadow = conf.shadow && !clean && conf.shadowIntensity > 0 && !(perf && perf.glow === false);
        const blur = useShadow ? conf.shadowIntensity / 500 * 60 * dpr * (conf.shadowMusic ? 0.3 + Math.min(1, avgLevel * 1.6) : 1) : 0;
        ctx.lineCap = round ? 'round' : 'butt';
        const lwFrac = 0.15 + 0.85 * conf.lineWidth / 100;
        const span = linear ? (W * 0.7) / rays : (6.2832 * R) / rays;
        const lw = round ? Math.max(1.5 * dpr, span * lwFrac * 0.9) : Math.max(1.5 * dpr, span * 0.35);
        ctx.lineWidth = lw;
        const tilt = round ? conf.rayAngle / 100 * 0.6 : 0;
        const x0 = cx - (W * 0.7) / 2, step = (W * 0.7) / (rays - 1);
        ctx.shadowBlur = blur;
        for (let i = 0; i < rays; i++) {
            const len = (round ? lw * 0.5 : 0) + smooth[i] * R * 1.3 + 2 * dpr;
            ctx.strokeStyle = colors[i];
            if (blur) ctx.shadowColor = colors[i];
            ctx.beginPath();
            if (linear) { const x = x0 + step * i; ctx.moveTo(x, cy); ctx.lineTo(x + tilt * smooth[i] * R * 0.3, cy - len); }
            else {
                const a = (i / rays) * 6.2832 - 1.5708, a2 = a + tilt * smooth[i], r0 = R;
                ctx.moveTo(cx + Math.cos(a) * r0, cy + Math.sin(a) * r0);
                ctx.lineTo(cx + Math.cos(a2) * (r0 + len), cy + Math.sin(a2) * (r0 + len));
            }
            ctx.stroke();
        }
        ctx.shadowBlur = 0; ctx.globalAlpha = 1;
    }

    function startLoop() { if (raf) return; lastFrame = 0; silentSince = 0; raf = requestAnimationFrame(frame); }
    function stopLoop() {
        if (raf) { cancelAnimationFrame(raf); raf = 0; }
        ctx.clearRect(0, 0, W, H);
        releaseCapture();
    }

    // ---- clock / date --------------------------------------------------------------------------------------------------------------------
    function buildFormats() {
        const l = lang || undefined;
        try {
            fmtTime = new Intl.DateTimeFormat(l, { hour: conf.h12 ? 'numeric' : '2-digit', minute: '2-digit', second: conf.seconds ? '2-digit' : undefined, hour12: !!conf.h12 });
            fmtWeek = new Intl.DateTimeFormat(l, { weekday: 'long' });
            fmtDate = conf.numericDate
                ? new Intl.DateTimeFormat('en-US', { month: '2-digit', day: conf.dayNumber ? '2-digit' : undefined, year: conf.year ? 'numeric' : undefined })
                : new Intl.DateTimeFormat(l, { day: conf.dayNumber ? 'numeric' : undefined, month: 'long', year: conf.year ? 'numeric' : undefined });
        } catch (x) { fmtTime = fmtDate = fmtWeek = null; }
    }
    function tickClock() {
        if (!conf || !fmtTime) return;
        const d = new Date();
        if (conf.showClock) { const t = fmtTime.format(d); if (clockEl.textContent !== t) clockEl.textContent = t; }
        if (conf.showDate) {
            const body = fmtDate.format(d); const t = conf.dayName ? fmtWeek.format(d) + ' ' + body : body;
            if (dateEl.textContent !== t) dateEl.textContent = t;
        }
    }
    function styleText() {
        const fam = '"' + conf.font.replace(/["\\]/g, '') + '", "Segoe UI", sans-serif';
        const sh = conf.shadow ? '0 0 ' + Math.round(conf.shadowIntensity / 500 * 30) + 'px rgba(0,0,0,.65)' : 'none';
        const set = (el, show, color, size, x, y) => {
            el.style.display = show ? '' : 'none'; el.style.color = color; el.style.fontSize = size + 'vh'; el.style.left = x + '%'; el.style.top = (100 - y) + '%';
            el.style.fontFamily = fam; el.style.fontWeight = conf.bold ? '700' : '400'; el.style.textShadow = sh;
        };
        set(clockEl, conf.showClock && conf.clockSize > 0, conf.clockColor, conf.clockSize, conf.clockX, conf.clockY);
        set(dateEl, conf.showDate && conf.dateSize > 0, conf.dateColor, conf.dateSize, conf.dateX, conf.dateY);
    }
    function clockRunning() { return !!(conf && conf.enabled && !paused && (conf.showClock || conf.showDate)); }
    function syncClock() {
        clearInterval(clockTimer); clockTimer = 0;
        if (clockRunning()) { tickClock(); clockTimer = setInterval(tickClock, 1000); }
    }

    // ---- background (colour / image / video picked by the user) ------------------------------------------------------------------------------
    function applyBackground() {
        const key = conf.useBg + '|' + conf.bg + '|' + conf.useBgImage + '|' + conf.bgImage;
        if (key === lastBgKey) { if (vid) { if (paused || !conf.enabled) vid.pause(); else vid.play().catch(() => {}); } return; }
        lastBgKey = key;
        bgDiv.style.background = conf.useBg ? conf.bg : 'none';
        if (vid) { vid.pause(); vid.removeAttribute('src'); vid.load(); vid.remove(); vid = null; }
        if (conf.useBgImage && conf.bgImage) {
            if (/\.(webm|mp4|m4v)$/i.test(conf.bgImage)) {
                vid = document.createElement('video'); vid.muted = true; vid.loop = true; vid.playsInline = true; vid.autoplay = !paused;
                vid.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;object-fit:cover'; vid.src = conf.bgImage; bgDiv.appendChild(vid);
            } else {
                bgDiv.style.background = (conf.useBg ? conf.bg + ' ' : '') + 'url("' + conf.bgImage.replace(/["\\]/g, '') + '") center/cover no-repeat';
            }
        }
    }

    // ---- public hooks ----------------------------------------------------------------------------------------------------------------------
    window.opsisApplyViz = function (c) {
        perf = c.perf || null; perfMode = c.performanceMode || ''; lang = c.lang || 'en';
        const next = c.viz || null;
        if (!next || !next.enabled) {
            conf = next; root.style.display = 'none'; stopLoop(); syncClock();
            if (vid) vid.pause();
            return;
        }
        conf = next; root.style.display = '';
        buildFormats(); styleText(); applyBackground(); rebuild(); syncClock();
        if (wantsRays()) startLoop(); else stopLoop();
    };
    window.opsisPauseViz = function (p) {
        paused = !!p;
        if (!conf) return;
        if (paused) { stopLoop(); if (vid) vid.pause(); } else if (wantsRays()) startLoop();
        syncClock();
    };
    window.addEventListener('resize', () => { if (conf && conf.enabled) { rebuild(); } });
    document.addEventListener('visibilitychange', () => { if (!conf) return; if (wantsRays()) startLoop(); else stopLoop(); });
    // the dynamic performance scaler pushes a new profile live (index.html opsisSetPerf): follow it without a settings refresh
    window.addEventListener('load', () => {
        const orig = window.opsisSetPerf;
        if (typeof orig === 'function' && !orig._viz) {
            window.opsisSetPerf = function (p) { orig(p); perf = p; if (conf && conf.enabled) rebuild(); };
            window.opsisSetPerf._viz = true;
        }
    });
})();
