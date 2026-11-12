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
        L('eiffel', 'Eiffel Tower, Paris', 48.8584, 2.2945, 'iron', [
            M([[0, 1.0], [0.012, 0.93], [0.03, 0.74], [0.05, 0.6], [0.1, 0.38], [0.17, 0.24], [0.27, 0.07], [0.31, 0], [0.22, 0], [0.16, 0.1], [0.1, 0.17], [0.0, 0.2], [-0.1, 0.17], [-0.16, 0.1], [-0.22, 0], [-0.31, 0], [-0.27, 0.07], [-0.17, 0.24], [-0.1, 0.38], [-0.05, 0.6], [-0.03, 0.74], [-0.012, 0.93]]),
            M(rect(-0.2, 0.235, 0.2, 0.26)), M(rect(-0.115, 0.4, 0.115, 0.415)), M(rect(-0.045, 0.66, 0.045, 0.672)),
            P('d', arc(0, 0.0, 0.14, 0.17, 0, Math.PI, 14)),
            ...[0.3, 0.44, 0.56, 0.7, 0.82].map((y, i) => P('d', [[-0.05 + i * 0.008, y], [0.05 - i * 0.008, y], [0.0, y + 0.04]])),
            P('l', [[0, 1.0], [0.012, 0.93], [0.027, 0.74], [0.04, 0.6], [0.03, 0.6], [0.015, 0.74], [0.002, 0.93]]), P('w', rect(-0.01, 0.58, 0.01, 0.6))]),
        L('bigben', 'Big Ben, London', 51.5007, -0.1246, 'stone', [
            M(rect(-0.075, 0, 0.075, 0.56)), M(rect(-0.085, 0.56, 0.085, 0.76)), M([[-0.085, 0.76], [0.085, 0.76], [0.055, 0.82], [0.05, 0.9], [0.0, 1.0], [-0.05, 0.9], [-0.055, 0.82]]),
            P('l', rect(-0.075, 0, -0.058, 0.56)), P('d', rect(-0.04, 0.05, 0.04, 0.12)),
            ...[0.16, 0.26, 0.36, 0.46].flatMap(y => [P('w', rect(-0.03, y, -0.008, y + 0.07)), P('w', rect(0.008, y, 0.03, y + 0.07))]),
            P('w', arc(0, 0.665, 0.05, 0.05, 0, Math.PI * 2, 18)), P('d', rect(-0.003, 0.665, 0.003, 0.705)), P('d', rect(-0.003, 0.662, 0.03, 0.668)),
            M(rect(-0.5, 0, -0.1, 0.22)), M(rect(-0.5, 0.22, -0.1, 0.25)), ...windows(-0.48, 0.03, -0.12, 0.21, 9, 2, 0.45, 0.55), P('l', rect(-0.5, 0.22, -0.1, 0.235)),
            M([[-0.09, 0.22], [-0.09, 0.3], [-0.04, 0.3]])]),
        L('liberty', 'Statue of Liberty, New York', 40.6892, -74.0445, 'copper', [
            M(rect(-0.34, 0, 0.34, 0.06)), M(rect(-0.28, 0.06, 0.28, 0.12)), M(rect(-0.2, 0.12, 0.2, 0.3)), P('l', rect(-0.2, 0.12, -0.17, 0.3)), M(rect(-0.23, 0.3, 0.23, 0.33)),
            ...windows(-0.17, 0.14, 0.17, 0.28, 4, 2, 0.4, 0.5),
            M([[-0.12, 0.33], [0.12, 0.33], [0.085, 0.62], [0.06, 0.7], [-0.05, 0.7], [-0.09, 0.62]]), P('l', [[-0.12, 0.33], [-0.07, 0.33], [-0.05, 0.62], [-0.07, 0.62]]),
            M([[-0.05, 0.7], [0.05, 0.7], [0.06, 0.76], [0, 0.78], [-0.06, 0.76]]), M([[0.04, 0.72], [0.1, 0.9], [0.13, 1.0], [0.16, 0.99], [0.14, 0.88], [0.08, 0.7]]), P('w', rect(0.11, 0.97, 0.15, 1.0)),
            ...[-0.07, -0.04, -0.01, 0.02, 0.05].map((x, i) => M([[x - 0.006, 0.76], [x + 0.006, 0.76], [x + (i - 2) * 0.02, 0.86]])),
            M([[-0.08, 0.62], [-0.16, 0.66], [-0.16, 0.6], [-0.1, 0.5]]), P('d', [[-0.09, 0.34], [-0.03, 0.34], [-0.04, 0.6], [-0.08, 0.6]])]),
        L('colosseum', 'Colosseum, Rome', 41.8902, 12.4922, 'stone', [
            M([[-0.98, 0], [0.98, 0], [0.98, 0.5], [0.7, 0.58], [0.1, 0.64], [-0.4, 0.6], [-0.75, 0.52], [-0.98, 0.42]]),
            ...[0, 0.17, 0.34].flatMap((y, i) => arcade(-0.94, 0.94, 0.02 + y, 0.13, 17 - i, 0.55)), P('l', rect(-0.98, 0.165, 0.98, 0.175)), P('l', rect(-0.98, 0.335, 0.98, 0.345)),
            ...windows(-0.9, 0.5, 0.78, 0.58, 30, 1, 0.45, 0.7), P('d', [[0.98, 0.5], [0.98, 0.3], [0.9, 0.34], [0.84, 0.5]])]),
        L('pisa', 'Leaning Tower, Pisa', 43.723, 10.3966, 'marble', [
            M(rect(-0.35, 0, 0.35, 0.03)), M([[-0.1, 0.03], [0.12, 0.03], [0.22, 0.92], [0.02, 0.94]]),
            ...[0.0, 0.14, 0.28, 0.42, 0.56, 0.7].flatMap(y => arcade(-0.09 + (y + 0.12) * 0.1, 0.11 + (y + 0.12) * 0.1, y + 0.05, 0.1, 6, 0.5)), ...[0.14, 0.28, 0.42, 0.56, 0.7].map(y => P('l', [[-0.1 + y * 0.109, y], [0.12 + y * 0.109, y], [0.12 + y * 0.109, y + 0.012], [-0.1 + y * 0.109, y + 0.012]])),
            M([[0.02, 0.94], [0.21, 0.92], [0.2, 0.99], [0.04, 1.0]]), P('d', [[0.07, 0.94], [0.17, 0.935], [0.165, 0.98], [0.075, 0.985]])]),
        L('sagrada', 'Sagrada Família, Barcelona', 41.4036, 2.1744, 'sand', [
            M(rect(-0.5, 0, 0.5, 0.42)), ...[-0.34, -0.17, 0, 0.17, 0.34].flatMap((x, i) => [M([[x - 0.055, 0.4], [x + 0.055, 0.4], [x + 0.04, 0.8 + (i % 2) * 0.1], [x + 0.012, 0.93 + (i % 2) * 0.05], [x, 1.0 - (i % 2) * 0.04], [x - 0.012, 0.93 + (i % 2) * 0.05], [x - 0.04, 0.8 + (i % 2) * 0.1]]), P('l', rect(x - 0.05, 0.4, x - 0.03, 0.8)), P('d', rect(x - 0.012, 0.5, x + 0.012, 0.62)), P('d', rect(x - 0.012, 0.68, x + 0.012, 0.78))]),
            ...arcade(-0.46, 0.46, 0.02, 0.22, 3, 0.6), P('d', arc(0, 0.28, 0.06, 0.06, 0, Math.PI * 2, 14)), ...windows(-0.46, 0.3, 0.46, 0.4, 14, 1, 0.4, 0.7)]),
        L('brandenburg', 'Brandenburg Gate, Berlin', 52.5163, 13.3777, 'sand', [
            M(rect(-0.78, 0, 0.78, 0.04)), P('d', rect(-0.62, 0.04, 0.62, 0.62)), ...Array.from({ length: 6 }, (_, i) => column(-0.55 + i * 0.22, 0.04, 0.62, 0.09)).flat(),
            M(rect(-0.82, 0.62, 0.82, 0.74)), P('l', rect(-0.82, 0.72, 0.82, 0.74)), ...Array.from({ length: 7 }, (_, i) => P('d', rect(-0.66 + i * 0.22, 0.65, -0.62 + i * 0.22, 0.7))), M(rect(-0.82, 0.74, 0.82, 0.82)), M(rect(-0.3, 0.82, 0.3, 0.9)),
            M([[-0.12, 0.9], [0.12, 0.9], [0.1, 0.96], [0.04, 0.99], [0.0, 1.0], [-0.04, 0.99], [-0.1, 0.96]]), P('d', rect(-0.04, 0.9, 0.04, 0.94)),
            ...[-0.18, 0.0, 0.18].map(x => M([[x - 0.012, 0.9], [x + 0.012, 0.9], [x + 0.008, 1.0], [x - 0.008, 1.0]]))]),
        L('hagia', 'Hagia Sophia, Istanbul', 41.0086, 28.98, 'stone', [
            M([[-0.62, 0], [0.62, 0], [0.62, 0.3], [0.5, 0.4], [0.42, 0.5], [0.3, 0.62], [0.2, 0.76], [0.0, 0.84], [-0.2, 0.76], [-0.3, 0.62], [-0.42, 0.5], [-0.5, 0.4], [-0.62, 0.3]]),
            P('l', arc(0, 0.6, 0.27, 0.24, Math.PI * 0.55, Math.PI * 0.95, 8)), ...arcade(-0.58, 0.58, 0.02, 0.2, 9, 0.5), ...windows(-0.2, 0.62, 0.2, 0.72, 11, 1, 0.45, 0.7),
            ...[-0.86, 0.86].flatMap(x => [M(rect(x - 0.05, 0, x + 0.05, 0.78)), M([[x - 0.06, 0.78], [x + 0.06, 0.78], [x + 0.045, 0.82], [x, 0.98], [x - 0.045, 0.82]]), P('l', rect(x - 0.05, 0, x - 0.035, 0.78)), P('d', rect(x - 0.012, 0.62, x + 0.012, 0.7)), M(rect(x - 0.075, 0.74, x + 0.075, 0.77))])]),
        L('pyramids', 'Pyramids, Giza', 29.9792, 31.1342, 'sand', [
            M([[-1.05, 0], [-0.1, 0], [-0.6, 0.6]]), P('l', [[-1.05, 0], [-0.6, 0.6], [-0.6, 0.0]]), M([[-0.34, 0], [0.92, 0], [0.3, 1.0]]), P('l', [[-0.34, 0], [0.3, 1.0], [0.3, 0.0]]), M([[0.66, 0], [1.5, 0], [1.08, 0.46]]), P('l', [[0.66, 0], [1.08, 0.46], [1.08, 0.0]]),
            ...[0.14, 0.3, 0.46, 0.62, 0.78].map(y => P('d', [[0.3 - (1 - y) * 0.62, y], [0.3 + (1 - y) * 0.62, y], [0.3 + (1 - y) * 0.62, y + 0.006], [0.3 - (1 - y) * 0.62, y + 0.006]]))]),
        L('burj', 'Burj Khalifa, Dubai', 25.1972, 55.2744, 'glass', [
            M([[-0.003, 1.0], [0.003, 1.0], [0.012, 0.82], [0.03, 0.66], [0.026, 0.62], [0.05, 0.5], [0.044, 0.46], [0.07, 0.34], [0.064, 0.3], [0.095, 0.16], [0.09, 0.12], [0.12, 0.0], [-0.12, 0.0], [-0.09, 0.12], [-0.095, 0.16], [-0.064, 0.3], [-0.07, 0.34], [-0.044, 0.46], [-0.05, 0.5], [-0.026, 0.62], [-0.03, 0.66], [-0.012, 0.82]]),
            P('l', [[0, 1.0], [0.003, 1.0], [0.012, 0.82], [0.03, 0.66], [0.0, 0.66]]), ...windows(-0.05, 0.05, 0.05, 0.5, 4, 22, 0.5, 0.45)]),
        L('taj', 'Taj Mahal, Agra', 27.1751, 78.0421, 'white', [
            M(rect(-0.55, 0, 0.55, 0.06)), M(rect(-0.5, 0.06, 0.5, 0.32)), P('d', arc(0, 0.06, 0.1, 0.22, 0, Math.PI, 12)), ...[-0.34, -0.24, 0.24, 0.34].map(x => archOpen(x, 0.1, 0.07, 0.16)),
            M(dome(0, 0.44, 0.27, 0.34)), M(rect(-0.27, 0.32, 0.27, 0.44)), P('l', arc(0, 0.44, 0.27, 0.34, Math.PI * 0.55, Math.PI * 0.95, 8)), M([[-0.012, 0.78], [0.012, 0.78], [0.0, 0.95]]),
            ...[-0.42, 0.42].map(x => M([[x - 0.05, 0.32], [x + 0.05, 0.32], [x, 0.58]])), ...[-0.45, 0.45].map(x => M(dome(x, 0.34, 0.04, 0.06))),
            ...[-0.74, 0.74].flatMap(x => [M(rect(x - 0.035, 0, x + 0.035, 0.7)), M(rect(x - 0.045, 0.34, x + 0.045, 0.36)), M(rect(x - 0.045, 0.52, x + 0.045, 0.54)), M(dome(x, 0.7, 0.045, 0.07)), P('l', rect(x - 0.035, 0, x - 0.02, 0.7)), P('d', rect(x - 0.008, 0.4, x + 0.008, 0.48))])]),
        L('tokyotower', 'Tokyo Tower', 35.6586, 139.7454, 'red', [
            M([[0, 1.0], [0.012, 0.86], [0.04, 0.64], [0.07, 0.5], [0.14, 0.26], [0.24, 0.06], [0.28, 0.0], [0.19, 0.0], [0.14, 0.1], [0.0, 0.16], [-0.14, 0.1], [-0.19, 0.0], [-0.28, 0.0], [-0.24, 0.06], [-0.14, 0.26], [-0.07, 0.5], [-0.04, 0.64], [-0.012, 0.86]]),
            M(rect(-0.16, 0.26, 0.16, 0.31)), M(rect(-0.08, 0.48, 0.08, 0.52)), P('l', rect(-0.15, 0.27, 0.15, 0.29)), P('w', rect(-0.08, 0.49, 0.08, 0.505)), P('d', arc(0, 0, 0.12, 0.14, 0, Math.PI, 12)),
            ...[0.34, 0.4, 0.56, 0.7].map((y, i) => P('d', [[-0.06 + i * 0.01, y], [0.06 - i * 0.01, y], [0, y + 0.04]])), M(rect(-0.004, 1.0, 0.004, 1.1))]),
        L('opera', 'Sydney Opera House', -33.8568, 151.2153, 'white', [
            M(rect(-1.0, 0, 1.0, 0.05)), M(rect(-0.95, 0.05, 0.95, 0.09)),
            M([[-0.92, 0.09], [-0.5, 0.09], [-0.56, 0.4], [-0.7, 0.62], [-0.82, 0.58], [-0.9, 0.4]]), M([[-0.55, 0.09], [-0.12, 0.09], [-0.2, 0.46], [-0.36, 0.76], [-0.5, 0.74], [-0.57, 0.5]]), M([[-0.1, 0.09], [0.42, 0.09], [0.32, 0.55], [0.12, 0.92], [-0.04, 0.88], [-0.1, 0.55]]), M([[0.4, 0.09], [0.9, 0.09], [0.82, 0.46], [0.66, 0.74], [0.52, 0.7], [0.44, 0.45]]),
            P('l', [[-0.7, 0.62], [-0.56, 0.4], [-0.6, 0.4]]), P('l', [[-0.36, 0.76], [-0.2, 0.46], [-0.26, 0.46]]), P('l', [[0.12, 0.92], [0.32, 0.55], [0.25, 0.55]]), P('l', [[0.66, 0.74], [0.82, 0.46], [0.76, 0.46]]),
            ...[-0.5, -0.1, 0.4].flatMap(x => windows(x - 0.18, 0.1, x + 0.18, 0.26, 4, 2, 0.5, 0.6))]),
        L('goldengate', 'Golden Gate Bridge, San Francisco', 37.8199, -122.4783, 'red', [
            M(rect(-1.2, 0.15, 1.2, 0.21)),
            ...Array.from({ length: 36 }, (_, i) => { const x = -1.18 + i * 0.0655; const y = x < -0.56 ? 0.3 + (x + 1.2) / 0.64 * 0.63 : x < 0.56 ? 0.26 + (x * x) / 0.3136 * 0.67 : 0.93 - (x - 0.56) / 0.64 * 0.63; return M([[x, 0.21], [x + 0.006, 0.21], [x + 0.006, y], [x, y]]); }),
            ...[[-1.2, 0.3, -0.56, 0.93], [-0.56, 0.93, 0.0, 0.26], [0.0, 0.26, 0.56, 0.93], [0.56, 0.93, 1.2, 0.3]].map(([x0, y0, x1, y1]) => M([[x0, y0], [x1, y1], [x1, y1 - 0.014], [x0, y0 - 0.014]])),
            ...[-0.56, 0.56].flatMap(x => [M(rect(x - 0.04, 0, x + 0.04, 0.95)), M(rect(x - 0.05, 0.9, x + 0.05, 0.96)), P('l', rect(x - 0.04, 0, x - 0.028, 0.95)), ...[0.3, 0.5, 0.7, 0.85].map(y => M(rect(x - 0.075, y, x + 0.075, y + 0.025))), P('d', rect(x - 0.02, 0.21, x + 0.02, 0.28))])]),
        L('christ', 'Christ the Redeemer, Rio de Janeiro', -22.9519, -43.2105, 'white', [
            M([[-0.36, 0], [0.36, 0], [0.26, 0.14], [0.12, 0.2], [-0.12, 0.2], [-0.26, 0.14]]), M(rect(-0.1, 0.2, 0.1, 0.34)), M([[-0.09, 0.34], [0.09, 0.34], [0.07, 0.62], [-0.07, 0.62]]), P('l', [[-0.09, 0.34], [-0.05, 0.34], [-0.04, 0.62], [-0.07, 0.62]]),
            M([[-0.46, 0.58], [0.46, 0.58], [0.46, 0.62], [0.07, 0.64], [-0.07, 0.64], [-0.46, 0.62]]), M([[-0.07, 0.62], [0.07, 0.62], [0.05, 0.86], [-0.05, 0.86]]), M(arc(0, 0.9, 0.05, 0.06, 0, Math.PI * 2, 12)), P('l', [[-0.46, 0.58], [-0.46, 0.62], [-0.38, 0.615], [-0.38, 0.58]]),
            P('d', [[0.08, 0.34], [0.1, 0.34], [0.07, 0.6], [0.05, 0.6]])]),
        L('matterhorn', 'Matterhorn', 45.9766, 7.6585, 'rock', [
            M([[-1.5, 0], [-0.8, 0.22], [-0.45, 0.4], [-0.3, 0.7], [-0.12, 0.78], [-0.05, 0.94], [0.0, 1.0], [0.1, 0.86], [0.18, 0.74], [0.32, 0.58], [0.5, 0.4], [0.9, 0.16], [1.5, 0]]),
            P('l', [[-0.3, 0.7], [-0.12, 0.78], [-0.05, 0.94], [0.0, 1.0], [0.1, 0.86], [0.03, 0.62], [-0.12, 0.62]]), P('l', [[-0.8, 0.22], [-0.45, 0.4], [-0.38, 0.5], [-0.62, 0.3]]), P('l', [[0.32, 0.58], [0.5, 0.4], [0.64, 0.3], [0.44, 0.5]]), P('d', [[0.1, 0.86], [0.18, 0.74], [0.32, 0.58], [0.22, 0.45], [0.1, 0.6]]), P('l', [[-0.04, 0.9], [0.01, 0.99], [0.06, 0.88], [0.0, 0.82]])]),
        L('fuji', 'Mount Fuji', 35.3606, 138.7274, 'rock', [
            M([[-1.6, 0], [-0.9, 0.12], [-0.5, 0.34], [-0.26, 0.68], [-0.14, 0.9], [-0.1, 0.97], [0.1, 0.97], [0.14, 0.9], [0.26, 0.68], [0.5, 0.34], [0.9, 0.12], [1.6, 0]]),
            P('l', [[-0.26, 0.68], [-0.14, 0.9], [-0.1, 0.97], [0.1, 0.97], [0.14, 0.9], [0.26, 0.68], [0.18, 0.7], [0.12, 0.8], [0.04, 0.72], [-0.02, 0.8], [-0.08, 0.7], [-0.16, 0.76]]), P('d', [[0.5, 0.34], [0.26, 0.68], [0.34, 0.4]])]),
        L('cntower', 'CN Tower, Toronto', 43.6426, -79.3871, 'glass', [
            M([[-0.006, 1.0], [0.006, 1.0], [0.012, 0.74], [0.02, 0.58], [0.026, 0.0], [-0.026, 0.0], [-0.02, 0.58], [-0.012, 0.74]]),
            M([[-0.1, 0.56], [0.1, 0.56], [0.12, 0.52], [0.1, 0.49], [0.035, 0.47], [-0.035, 0.47], [-0.1, 0.49], [-0.12, 0.52]]), ...windows(-0.1, 0.5, 0.1, 0.56, 12, 1, 0.5, 0.6), M(rect(-0.045, 0.45, 0.045, 0.52)), M(rect(-0.05, 0.0, 0.05, 0.06)), P('l', rect(-0.02, 0.1, -0.012, 0.58))]),
        L('petronas', 'Petronas Towers, Kuala Lumpur', 3.1579, 101.7116, 'glass', [
            ...[-0.2, 0.2].flatMap(x => [M([[x - 0.11, 0], [x + 0.11, 0], [x + 0.1, 0.6], [x + 0.085, 0.78], [x + 0.06, 0.88], [x + 0.03, 0.95], [x, 1.0], [x - 0.03, 0.95], [x - 0.06, 0.88], [x - 0.085, 0.78], [x - 0.1, 0.6]]), P('l', [[x - 0.11, 0], [x - 0.07, 0], [x - 0.06, 0.88], [x - 0.085, 0.78]]), ...windows(x - 0.09, 0.02, x + 0.09, 0.8, 5, 26, 0.5, 0.45)]),
            M(rect(-0.1, 0.5, 0.1, 0.56)), P('w', rect(-0.09, 0.51, 0.09, 0.545))]),
        L('lisbon', 'Belém Tower, Lisbon', 38.6916, -9.216, 'stone', [
            M(rect(-0.34, 0, 0.34, 0.5)), M(rect(-0.38, 0.46, 0.38, 0.54)), ...[-0.34, -0.17, 0, 0.17, 0.34].map(x => M(dome(x, 0.54, 0.05, 0.07))), M(rect(-0.2, 0.54, 0.2, 0.84)), M([[-0.22, 0.84], [0.22, 0.84], [0.0, 1.0]]),
            ...arcade(-0.3, 0.3, 0.04, 0.22, 3, 0.55), ...windows(-0.17, 0.58, 0.17, 0.8, 3, 2, 0.4, 0.55), P('l', rect(-0.34, 0, -0.31, 0.5)), P('l', [[-0.22, 0.84], [0, 1.0], [0, 0.97], [-0.18, 0.84]])]),
        L('cyprus', 'Kyrenia Castle, Cyprus', 35.3427, 33.3192, 'sand', [
            M(rect(-0.85, 0, 0.85, 0.42)), M(rect(-0.85, 0.42, 0.85, 0.46)), ...[-0.78, -0.62, -0.46, -0.3, -0.14, 0.02, 0.18, 0.34, 0.5, 0.66].map(x => M(rect(x, 0.46, x + 0.1, 0.52))),
            ...[-0.82, 0.82].flatMap(x => [M(dome(x, 0.42, 0.14, 0.3)), P('l', rect(x - 0.14, 0, x - 0.12, 0.45))]), ...[-0.5, 0.5].map(x => M(rect(x - 0.12, 0.46, x + 0.12, 0.8))), ...[-0.5, 0.5].map(x => M(rect(x - 0.13, 0.8, x + 0.13, 0.86))),
            ...windows(-0.1, 0.1, 0.1, 0.38, 3, 3, 0.4, 0.45), P('d', arc(0, 0, 0.09, 0.2, 0, Math.PI, 10))]),
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
    const SPRITE_IDS = ['parthenon', 'taj', 'eiffel', 'bigben', 'patras', 'rioantirrio', 'nafplio'];
    // where each monument stands: on LAND (a rock / grass base under it, the ground below the horizon instead of the sea) or in the SEA (water below, as before). plinth = height of the base as a fraction of the monument.
    const SCENE = {
        parthenon: { scene: 'land', plinth: 0.2, ground: 'rock' }, taj: { scene: 'land', plinth: 0.04, ground: 'sand' }, eiffel: { scene: 'land', plinth: 0.04, ground: 'grass' }, bigben: { scene: 'land', plinth: 0.04, ground: 'grass' },
        patras: { scene: 'sea' }, rioantirrio: { scene: 'sea' }, nafplio: { scene: 'sea' },
    };
    let photoOnly = true;
    const sprites = {};                       // id -> { img, ready, tinted: { key, canvas } }
    function sprite(id) {
        let s = sprites[id];
        if (!s) { s = sprites[id] = { img: new Image(), ready: false, tinted: { key: '', canvas: null } }; s.img.onload = () => { s.ready = true; }; s.img.src = 'landmarks/' + id + '.webp'; }
        return s;
    }

    function byId(id) { const m = LANDMARKS.find(x => x.id === id && SPRITE_IDS.includes(x.id)); return m ? { id: m.id, name: m.name, km: 0 } : null; }
    function sceneOf(id) { return SCENE[id] || { scene: 'sea' }; }
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

    root.OpsisLandmarks = { LANDMARKS, pickLandmark, byId, sceneOf, spriteSize, drawLandmark, distKm, MAT, SPRITE_IDS };
    if (typeof module !== 'undefined') module.exports = root.OpsisLandmarks;
})(typeof window !== 'undefined' ? window : globalThis);
