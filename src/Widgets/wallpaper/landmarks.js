// Landmarks for the weather wallpaper (plan 2.1.5 / 2.1.5b): the best-known monument of the place where the computer is (the weather location) stands on the horizon.
// Own drawing, no downloaded models: every monument is built from parts (body, openings, highlights, windows) with a MATERIAL (marble, stone, iron, glass...), lit by the sun / moon
// from the correct side, faded by the haze of the scene, floodlit and with lit windows at night. pickLandmark(lat, lon) chooses the nearest within maxKm.
// Units: x in landmark heights around 0, y up from the horizon (0..1). Part kinds: m = body, d = dark opening (arch, door, gap), l = light edge / fluting, w = window.
(function (root) {
    const P = (kind, pts) => ({ k: kind, p: pts });
    const M = pts => P('m', pts);
    const rect = (x0, y0, x1, y1) => [[x0, y0], [x1, y0], [x1, y1], [x0, y1]];
    const arc = (cx, cy, rx, ry, a0, a1, n = 14) => { const o = []; for (let i = 0; i <= n; i++) { const a = a0 + (a1 - a0) * i / n; o.push([cx + Math.cos(a) * rx, cy + Math.sin(a) * ry]); } return o; };
    const dome = (cx, y, rx, ry) => arc(cx, y, rx, ry, 0, Math.PI, 16);
    // arched opening: rectangle with a round top
    const archOpen = (cx, y0, w, h) => P('d', [[cx - w / 2, y0], [cx + w / 2, y0], ...arc(cx, y0 + h - w / 2, w / 2, w / 2, 0, Math.PI, 8)]);
    const arcade = (x0, x1, y0, h, n, fill = 0.55) => { const o = [], step = (x1 - x0) / n; for (let i = 0; i < n; i++) o.push(archOpen(x0 + step * (i + 0.5), y0, step * fill, h)); return o; };
    const windows = (x0, y0, x1, y1, cols, rows, fw = 0.5, fh = 0.55) => { const o = [], sx = (x1 - x0) / cols, sy = (y1 - y0) / rows; for (let c = 0; c < cols; c++) for (let r = 0; r < rows; r++) o.push(P('w', rect(x0 + sx * (c + (1 - fw) / 2), y0 + sy * (r + (1 - fh) / 2), x0 + sx * (c + (1 + fw) / 2), y0 + sy * (r + (1 + fh) / 2)))); return o; };
    // a column: base, shaft with a light flute, capital
    const column = (x, y0, y1, w) => { const t = y1 - y0; return [M(rect(x - w * 0.75, y0, x + w * 0.75, y0 + t * 0.06)), M(rect(x - w / 2, y0 + t * 0.06, x + w / 2, y1 - t * 0.07)), P('l', rect(x - w * 0.12, y0 + t * 0.08, x + w * 0.04, y1 - t * 0.09)), M(rect(x - w * 0.8, y1 - t * 0.07, x + w * 0.8, y1))]; };
    const L = (id, name, lat, lon, mat, parts) => ({ id, name, lat, lon, mat, parts });

    // materials: rgb of the surface in neutral light
    const MAT = { marble: [226, 214, 192], stone: [196, 170, 136], iron: [112, 100, 94], brick: [176, 112, 88], white: [238, 238, 242], copper: [120, 168, 150], glass: [150, 176, 204], sand: [214, 184, 132], rock: [128, 122, 130], red: [214, 76, 58] };

    const LANDMARKS = [
        L('parthenon', 'Parthenon, Athens', 37.9715, 23.7267, 'marble', [
            M(rect(-0.78, 0, 0.78, 0.04)), M(rect(-0.72, 0.04, 0.72, 0.08)), M(rect(-0.66, 0.08, 0.66, 0.12)), P('d', rect(-0.6, 0.12, 0.6, 0.62)),
            ...Array.from({ length: 8 }, (_, i) => column(-0.56 + i * 0.16, 0.12, 0.62, 0.075)).flat(),
            M(rect(-0.68, 0.62, 0.68, 0.69)), P('l', rect(-0.68, 0.665, 0.68, 0.69)), M([[-0.7, 0.69], [0.7, 0.69], [0.0, 0.96]]), P('d', [[-0.52, 0.715], [0.52, 0.715], [0.0, 0.9]]), P('l', [[-0.7, 0.69], [0.0, 0.96], [0.0, 0.945], [-0.66, 0.69]])]),
        L('whitetower', 'White Tower, Thessaloniki', 40.6264, 22.9484, 'white', [
            M(rect(-0.34, 0, 0.34, 0.42)), M(rect(-0.3, 0.42, 0.3, 0.84)), M(rect(-0.34, 0.8, 0.34, 0.9)), P('l', rect(-0.34, 0.86, 0.34, 0.9)),
            ...[-0.3, -0.18, -0.06, 0.06, 0.18, 0.3].map(x => P('d', rect(x - 0.025, 0.9, x + 0.025, 0.96))), M(rect(-0.34, 0.9, 0.34, 0.92)), M([[-0.34, 0.92], [0.34, 0.92], [0.28, 0.99], [-0.28, 0.99]]),
            ...arcade(-0.28, 0.28, 0.06, 0.2, 3, 0.6), ...windows(-0.24, 0.5, 0.24, 0.78, 3, 2, 0.4, 0.5), P('d', arc(0, 0.04, 0.07, 0.1, 0, Math.PI))]),
        // photo-only landmarks (no vector fallback): place and name only
        L('patras', 'Lighthouse of Patras', 38.2467, 21.7295, 'stone', []),
        L('rioantirrio', 'Rio-Antirrio Bridge', 38.3200, 21.7740, 'white', []),
        L('nafplio', 'Bourtzi, Nafplio', 37.5640, 22.8000, 'stone', []),
    ];

    function distKm(lat1, lon1, lat2, lon2) {
        const r = Math.PI / 180, dLat = (lat2 - lat1) * r, dLon = (lon2 - lon1) * r;
        const a = Math.sin(dLat / 2) ** 2 + Math.cos(lat1 * r) * Math.cos(lat2 * r) * Math.sin(dLon / 2) ** 2;
        return 6371 * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    }

    // Photographic sprites (landmarks/<id>.webp, cut-outs of Wikimedia Commons photos with a clear sky, see landmarks/ATTRIBUTION.md). Only landmarks WITH a photo are offered: the hand-built
    // vector shapes above look like drawings, so they stay as a fallback for the future (photoOnly = false) and are not shown by default.
    const SPRITE_IDS = ['parthenon', 'patras', 'rioantirrio', 'nafplio'];     // Greek landmarks only (photos: public domain / CC0 / CC BY, see landmarks/manifest.json)
    // where each monument stands: on LAND (a rock / grass base under it, the ground below the horizon instead of the sea) or in the SEA (water below, as before). plinth = height of the base as a fraction of the monument.
    const SCENE = {
        parthenon: { scene: 'photo' },                                    // a real aerial photograph of the Acropolis and Athens replaces sprite + landscape (scenes/acropolis.webp)
        patras: { scene: 'sea' }, rioantirrio: { scene: 'sea' }, nafplio: { scene: 'sea' },
    };
    const PHOTO_SCENES = { parthenon: { file: 'scenes/acropolis.webp', lights: 'scenes/acropolis-lights.webp' } };
    let photoOnly = true;
    const sprites = {};                       // id -> { img, ready, tinted: { key, canvas } }
    function sprite(id) {
        let s = sprites[id];
        if (!s) { s = sprites[id] = { img: new Image(), ready: false, tinted: { key: '', canvas: null } }; s.img.onload = () => { s.ready = true; }; s.img.src = 'landmarks/' + id + '.webp'; }
        return s;
    }

    function byId(id) { const m = LANDMARKS.find(x => x.id === id && SPRITE_IDS.includes(x.id)); return m ? { id: m.id, name: m.name, km: 0 } : null; }
    function sceneOf(id) { return SCENE[id] || { scene: 'sea' }; }
    function photoScene(id) { return PHOTO_SCENES[id] || null; }
    // on-screen size of the sprite once loaded (null while loading), so the caller can build the ground under it
    function spriteSize(id, maxH, maxW) {
        const s = sprite(id); if (!s.ready) return null;
        const sc = Math.min(maxH / s.img.naturalHeight, maxW / s.img.naturalWidth);
        return { w: Math.round(s.img.naturalWidth * sc), h: Math.round(s.img.naturalHeight * sc) };
    }

    // nearest landmark within maxKm of the position; null when nothing is near (the plain hills stay)
    function pickLandmark(lat, lon, maxKm = 250) {
        if (typeof lat !== 'number' || typeof lon !== 'number' || isNaN(lat) || isNaN(lon)) return null;
        let best = null, bd = Infinity;
        for (const m of LANDMARKS) { if (photoOnly && !SPRITE_IDS.includes(m.id)) continue; const d = distKm(lat, lon, m.lat, m.lon); if (d < bd) { bd = d; best = m; } }
        return best && bd <= maxKm ? { id: best.id, name: best.name, km: Math.round(bd) } : null;
    }

    const clamp01 = v => Math.max(0, Math.min(1, v));
    const mixc = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
    const css = (c, a = 1) => `rgba(${Math.round(c[0])},${Math.round(c[1])},${Math.round(c[2])},${a})`;

    // pal: { amb 0..1 (daylight), haze [r,g,b] (colour of the distant air), fog 0..1, sunSide -1 (light from the left) .. 1 (right), warm [r,g,b] (sun / sky tint), night 0..1, snow 0..1, maxW px }
    // h = maximum height in px. Photographic sprites are tinted by the light of the scene (multiply by the ambient colour, warm floodlight at night, haze of the distance) in an
    // offscreen canvas that is rebuilt only when the quantised light changes (a few times per day), so a frame costs one drawImage.
    function drawLandmark(g, id, cx, baseY, h, pal) {
        if (SPRITE_IDS.includes(id)) return drawSprite(g, id, cx, baseY, h, pal);
        return drawProcedural(g, id, cx, baseY, h, pal);
    }

    function drawSprite(g, id, cx, baseY, maxH, pal) {
        const s = sprite(id); if (!s.ready) return false;
        const iw = s.img.naturalWidth, ih = s.img.naturalHeight;
        const scale = Math.min(maxH / ih, (pal.maxW || maxH * 1.5) / iw);
        const w = Math.max(8, Math.round(iw * scale)), h = Math.max(8, Math.round(ih * scale));
        const amb = clamp01(pal.amb), night = clamp01(pal.night || 0), fog = clamp01(pal.fog || 0), snow = clamp01(pal.snow || 0);
        const key = [w, h, Math.round(amb * 14), Math.round(night * 8), Math.round(fog * 10), Math.round(snow * 5), Math.round((pal.glow || 0) * 4)].join('/');
        const t = s.tinted;
        if (t.key !== key) {
            const c = t.canvas || (t.canvas = document.createElement('canvas')); c.width = w; c.height = h;
            const o = c.getContext('2d'); o.clearRect(0, 0, w, h); o.imageSmoothingQuality = 'high'; o.drawImage(s.img, 0, 0, w, h);
            // daylight: the photo is a bright afternoon shot, so full ambient = unchanged; dusk and night multiply it down and towards the warm / cool light of the sky
            const warm = pal.warm || [255, 236, 205], dusk = clamp01((pal.glow || 0) * (1 - amb) * 1.6);
            const dayMul = mixc([62, 74, 110], [255, 255, 255], Math.pow(amb, 0.8));
            const mul = mixc(dayMul, [dayMul[0] * warm[0] / 255, dayMul[1] * warm[1] / 255, dayMul[2] * warm[2] / 255], dusk * 0.6);
            o.globalCompositeOperation = 'multiply'; o.fillStyle = css(mul); o.fillRect(0, 0, w, h);
            o.globalCompositeOperation = 'destination-in'; o.drawImage(s.img, 0, 0, w, h);            // restore the alpha the multiply fill covered
            o.globalCompositeOperation = 'source-atop';
            if (night > 0.05) { o.fillStyle = css([255, 196, 120], 0.2 * night); o.fillRect(0, 0, w, h); }  // warm floodlight on the stone at night
            if (snow > 0.05) { o.fillStyle = css([232, 238, 250], 0.18 * snow); o.fillRect(0, 0, w, h); }
            const haze = pal.haze || [200, 210, 225];
            o.fillStyle = css(haze, 0.1 + fog * 0.65); o.fillRect(0, 0, w, h);                         // the air between us and the monument, thicker in fog
            o.globalCompositeOperation = 'source-over';
            t.key = key;
        }
        g.save(); g.imageSmoothingQuality = 'high';
        g.drawImage(t.canvas, Math.round(cx - w / 2), Math.round(baseY - h + h * 0.02));
        g.restore(); return true;
    }

    function drawProcedural(g, id, cx, baseY, h, pal) {
        const m = LANDMARKS.find(x => x.id === id); if (!m) return false;
        const amb = clamp01(pal.amb), night = clamp01(pal.night || 0), fog = clamp01(pal.fog || 0), side = pal.sunSide >= 0 ? 1 : -1;
        const haze = pal.haze || [200, 210, 225], warm = pal.warm || [255, 236, 205];
        // surface colour: material lit by the sky (warm at low sun), dim at night but FLOODLIT (warm light on the monument), then faded into the haze of the distance
        const base = MAT[m.mat] || MAT.stone;
        const day = mixc([base[0] * 0.38, base[1] * 0.4, base[2] * 0.5], mixc(base, warm, 0.18), amb);
        const flood = mixc(day, mixc(base, [255, 214, 150], 0.45), night * (m.mat === 'glass' || m.mat === 'iron' ? 0.6 : 0.85));
        let lit = mixc(flood, haze, 0.08 + fog * 0.75);
        if (pal.snow) lit = mixc(lit, [232, 238, 250], clamp01(pal.snow) * 0.35);
        const shade = mixc(lit, [18, 24, 44], 0.42);
        const light = mixc(lit, [255, 250, 238], 0.34 + 0.16 * amb);
        const x0 = cx - h * 1.1, x1 = cx + h * 1.1;
        const body = g.createLinearGradient(side > 0 ? x0 : x1, 0, side > 0 ? x1 : x0, 0);
        body.addColorStop(0, css(shade)); body.addColorStop(0.55, css(lit)); body.addColorStop(1, css(light));
        const winDay = mixc(shade, [120, 150, 190], 0.4), winNight = [255, 206, 110];
        g.save();
        // ground contact: a soft dark band under the monument
        const gr = g.createLinearGradient(0, baseY - h * 0.02, 0, baseY + h * 0.05); gr.addColorStop(0, css(shade, 0.5 * (1 - fog))); gr.addColorStop(1, css(shade, 0));
        g.fillStyle = gr; g.fillRect(cx - h * 1.2, baseY - h * 0.02, h * 2.4, h * 0.07);
        for (const part of m.parts) {
            g.beginPath();
            part.p.forEach(([x, y], i) => { const px = cx + x * h, py = baseY - y * h; if (i === 0) g.moveTo(px, py); else g.lineTo(px, py); });
            g.closePath();
            if (part.k === 'm') g.fillStyle = body;
            else if (part.k === 'd') g.fillStyle = css(mixc(shade, [8, 10, 22], 0.5), 0.92);
            else if (part.k === 'l') g.fillStyle = css(light, 0.55);
            else g.fillStyle = night > 0.25 ? css(mixc(winDay, winNight, clamp01((night - 0.25) / 0.5)), 0.95) : css(winDay, 0.8);
            g.fill();
        }
        g.restore(); return true;
    }

    root.OpsisLandmarks = { LANDMARKS, pickLandmark, byId, sceneOf, photoScene, spriteSize, drawLandmark, distKm, MAT, SPRITE_IDS };
    if (typeof module !== 'undefined') module.exports = root.OpsisLandmarks;
})(typeof window !== 'undefined' ? window : globalThis);
