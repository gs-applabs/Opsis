    // ---- building blocks for the silhouettes of the Greek prefecture capitals: OWN drawings from the general shape of each monument (no photo traced, no model downloaded).
    const merl = (x0, x1, y, h, n) => { const s = (x1 - x0) / n, o = []; for (let i = 0; i < n; i++) o.push(M(rect(x0 + s * (i + 0.2), y, x0 + s * (i + 0.7), y + h))); return o; };
    const slit = (x, y, h, w = 0.012) => P('d', rect(x - w, y, x + w, y + h));
    const keep = (x, w, y0, y1, n = 3) => [M(rect(x - w / 2, y0, x + w / 2, y1)), P('l', rect(x - w / 2, y0, x - w / 2 + w * 0.14, y1)), ...merl(x - w / 2, x + w / 2, y1, 0.035, n), slit(x, y1 - 0.18, 0.1)];
    const wallRun = (x0, x1, y0, y1) => [M(rect(x0, y0, x1, y1)), ...merl(x0, x1, y1, 0.03, Math.max(3, Math.round((x1 - x0) / 0.07)))];
    const plateau = (hh, x) => M([[-1, 0], [-0.93, hh * 0.4], [-x - 0.05, hh * 0.8], [-x, hh], [-x * 0.5, hh * 0.97], [0, hh * 1.02], [x * 0.5, hh * 0.97], [x, hh], [x + 0.05, hh * 0.8], [0.93, hh * 0.4], [1, 0]]);
    // castle on a rock: hh = height of the rock, x = half width of the walls, towers = [[x, width, height above the wall foot]], gate = x of the gate (null = none), keepAt = [x, width, height] of a central keep
    const fort = ({ hh = 0.3, x = 0.85, wallTop = 0.2, towers = [[-0.85, 0.2, 0.34], [0.85, 0.2, 0.34]], gate = 0, keepAt = null, extra = [] } = {}) => {
        const wy = hh * 0.95, o = [];
        if (hh > 0) o.push(plateau(hh, x));
        o.push(...wallRun(-x, x, wy, wy + wallTop));
        for (const [tx, tw, th] of towers) o.push(...keep(tx, tw, wy, wy + th));
        if (keepAt) o.push(...keep(keepAt[0], keepAt[1], wy + wallTop, wy + wallTop + keepAt[2]));
        if (gate !== null) o.push(archOpen(gate, wy, 0.1, 0.13));
        return o.concat(extra);
    };
    // bridge with n arches, deck height and hump (extra rise in the middle)
    const bridge = (n, deck, hump = 0, x = 0.95) => {
        const top = t => deck + hump * (1 - t * t), pts = [[-x, 0]];
        for (let i = 0; i <= 24; i++) { const t = -1 + 2 * i / 24; pts.push([t * x, top(t)]); }
        pts.push([x, 0]);
        const o = [M(pts)], span = 2 * x / n;
        for (let i = 0; i < n; i++) { const cx = -x + span * (i + 0.5), w = span * 0.72; o.push(archOpen(cx, 0, w, Math.max(w / 2 + 0.01, (top(cx / x) - 0.07) * 0.9))); }
        const up = [], dn = [];
        for (let i = 0; i <= 24; i++) { const t = -1 + 2 * i / 24; up.push([t * x, top(t) - 0.012]); dn.unshift([t * x, top(t) - 0.03]); }
        o.push(P('l', up.concat(dn)));
        return o;
    };
    const aqueduct = () => [M(rect(-0.95, 0, 0.95, 0.74)), ...arcade(-0.95, 0.95, 0, 0.43, 7, 0.62), ...arcade(-0.95, 0.95, 0.46, 0.22, 14, 0.6), P('l', rect(-0.95, 0.72, 0.95, 0.74)), P('l', rect(-0.95, 0.44, 0.95, 0.455))];
    const lighthouse = ({ w = 0.16, tw = 0.1, h = 0.75, base = 0, baseW = 0.9 } = {}) => {
        const o = [], y0 = base, lt = y0 + h;
        if (base > 0) o.push(M(rect(-baseW, 0, baseW, base)), ...merl(-baseW, baseW, base, 0.025, 18));
        o.push(M([[-w, y0], [w, y0], [tw, lt], [-tw, lt]]), P('l', [[-w, y0], [-w * 0.7, y0], [-tw * 0.7, lt], [-tw, lt]]));
        o.push(M(rect(-tw - 0.04, lt, tw + 0.04, lt + 0.035)), P('w', rect(-tw * 0.7, lt + 0.035, tw * 0.7, lt + 0.11)), M(rect(-tw * 0.85, lt + 0.11, tw * 0.85, lt + 0.125)), M([[-tw * 0.85, lt + 0.125], [tw * 0.85, lt + 0.125], [0, lt + 0.2]]), archOpen(0, y0, 0.07, 0.12));
        return o;
    };
    const mountain = (peaks, x = 0.97) => {
        const f = px => Math.max(...peaks.map(([c, h, w]) => h * Math.max(0, 1 - Math.abs(px - c) / w) * (0.94 + 0.06 * Math.sin(px * 37 + c * 11)))), pts = [[-x, 0]];
        for (let i = 0; i <= 60; i++) { const px = -x + 2 * x * i / 60; pts.push([px, f(px)]); }
        pts.push([x, 0]);
        const [c, h, w] = peaks.reduce((a, b) => (b[1] > a[1] ? b : a));
        return [M(pts), P('l', [[c - w * 0.24, h * 0.76], [c - w * 0.08, h * 0.9], [c, h], [c + w * 0.1, h * 0.9], [c + w * 0.26, h * 0.74], [c + w * 0.1, h * 0.8], [c, h * 0.7], [c - w * 0.1, h * 0.82]])];
    };
    const campanile = (x, w, h) => [M(rect(x - w / 2, 0, x + w / 2, h)), P('l', rect(x - w / 2, 0, x - w / 2 + w * 0.15, h)), M(rect(x - w * 0.6, h * 0.5, x + w * 0.6, h * 0.53)), archOpen(x, h * 0.6, w * 0.4, h * 0.22), archOpen(x, h * 0.18, w * 0.3, h * 0.15), M(rect(x - w * 0.6, h, x + w * 0.6, h + 0.02)), M([[x - w * 0.55, h + 0.02], [x + w * 0.55, h + 0.02], [x, h + 0.16]]), M(rect(x - 0.006, h + 0.16, x + 0.006, h + 0.22))];
    const church = ({ bell = null, domeR = 0.2, w = 0.5, h = 0.3 } = {}) => {
        const top = h + 0.2 + domeR * 0.95;
        const o = [M(rect(-w, 0, w, h)), M([[-w, h], [-w * 0.6, h + 0.06], [w * 0.6, h + 0.06], [w, h]]), M(rect(-domeR * 0.85, h + 0.02, domeR * 0.85, h + 0.2)), M(dome(0, h + 0.2, domeR, domeR * 0.95)), M(rect(-0.01, top, 0.01, top + 0.07)), M(rect(-0.035, top + 0.04, 0.035, top + 0.055)),
            M(dome(-w, h, 0.1, 0.12)), archOpen(0.22, 0, 0.1, 0.18), archOpen(-0.22, 0, 0.1, 0.18)];
        for (const wx of [-0.1, 0, 0.1]) o.push(P('w', archOpen(wx, h + 0.06, 0.04, 0.09).p));
        if (bell) o.push(...campanile(w + bell, 0.14, 0.62));
        return o;
    };
    const clockTower = (w = 0.2, h = 0.78) => [M(rect(-w * 0.7, 0, w * 0.7, 0.06)), M(rect(-w / 2, 0.06, w / 2, h)), P('l', rect(-w / 2, 0.06, -w / 2 + w * 0.12, h)), M(rect(-w * 0.62, h * 0.52, w * 0.62, h * 0.55)),
        P('l', arc(0, h * 0.7, w * 0.3, w * 0.3, 0, Math.PI * 2, 20)), P('d', rect(-0.006, h * 0.7, 0.006, h * 0.7 + w * 0.22)), P('d', rect(0, h * 0.7 - 0.006, w * 0.16, h * 0.7 + 0.006)), archOpen(0, 0.06, w * 0.34, 0.16),
        M(rect(-w * 0.62, h, w * 0.62, h + 0.02)), archOpen(0, h * 0.82, w * 0.3, h * 0.16), M([[-w * 0.6, h + 0.02], [w * 0.6, h + 0.02], [0, h + 0.2]])];
    const mosque = ({ minaretX = 0.8, domeR = 0.34 } = {}) => [M(rect(-0.62, 0, 0.62, 0.3)), ...arcade(-0.62, 0.62, 0, 0.18, 6, 0.55), M(dome(0, 0.3, domeR, domeR * 0.85)), M(dome(-0.5, 0.3, 0.13, 0.1)), M(dome(0.5, 0.3, 0.13, 0.1)),
        M(rect(minaretX - 0.03, 0, minaretX + 0.03, 0.82)), M(rect(minaretX - 0.055, 0.6, minaretX + 0.055, 0.64)), M([[minaretX - 0.035, 0.82], [minaretX + 0.035, 0.82], [minaretX, 0.98]]), M([[-0.02, 0.3 + domeR * 0.85], [0.02, 0.3 + domeR * 0.85], [0, 0.3 + domeR * 0.85 + 0.08]])];
    const neoclassic = ({ wide = 0.85, h = 0.4, cols = 6, steps = 3, turret = false } = {}) => {
        const o = [];
        for (let i = 0; i < steps; i++) o.push(M(rect(-wide * 0.6 - 0.04 * (steps - i), i * 0.02, wide * 0.6 + 0.04 * (steps - i), i * 0.02 + 0.02)));
        const y0 = steps * 0.02;
        o.push(M(rect(-wide, y0, wide, y0 + h * 0.8)), P('l', rect(-wide, y0 + h * 0.76, wide, y0 + h * 0.8)), ...windows(-wide, y0 + 0.04, -wide * 0.62, y0 + h * 0.7, 2, 2), ...windows(wide * 0.62, y0 + 0.04, wide, y0 + h * 0.7, 2, 2), P('d', rect(-wide * 0.5, y0, wide * 0.5, y0 + h * 0.66)));
        for (let i = 0; i < cols; i++) o.push(...column(-wide * 0.5 + 0.06 + (wide - 0.12) * i / (cols - 1), y0, y0 + h * 0.7, 0.045));
        o.push(M(rect(-wide * 0.58, y0 + h * 0.7, wide * 0.58, y0 + h * 0.78)), M([[-wide * 0.6, y0 + h * 0.78], [wide * 0.6, y0 + h * 0.78], [0, y0 + h * 1.12]]), P('d', [[-wide * 0.46, y0 + h * 0.8], [wide * 0.46, y0 + h * 0.8], [0, y0 + h * 1.02]]));
        if (turret) o.push(M(rect(-0.1, y0 + h * 0.8, 0.1, y0 + h * 1.3)), P('l', arc(0, y0 + h * 1.05, 0.05, 0.05, 0, Math.PI * 2, 16)), M([[-0.12, y0 + h * 1.3], [0.12, y0 + h * 1.3], [0, y0 + h * 1.6]]));
        return o;
    };
    const ruinsTemple = heights => { const o = [M(rect(-0.9, 0, 0.9, 0.04)), M(rect(-0.84, 0.04, 0.84, 0.08))], n = heights.length; heights.forEach((hh, i) => { const x = -0.7 + 1.4 * i / (n - 1); o.push(...column(x, 0.08, 0.08 + hh, 0.085)); if (hh > 0.5 && i + 1 < n && heights[i + 1] > 0.5) o.push(M(rect(x, 0.08 + hh, -0.7 + 1.4 * (i + 1) / (n - 1), 0.08 + hh + 0.07))); }); return o; };
    const lonelyColumn = () => [M(rect(-0.6, 0, 0.6, 0.03)), M(rect(-0.5, 0.03, 0.5, 0.06)), ...column(-0.02, 0.06, 0.97, 0.12), M(rect(-0.55, 0.06, -0.35, 0.14)), M(rect(0.3, 0.06, 0.5, 0.22)), M(rect(0.38, 0.22, 0.46, 0.26)), M(rect(-0.3, 0.06, -0.18, 0.1))];
    const statueLeonidas = () => [M(rect(-0.34, 0, 0.34, 0.06)), M(rect(-0.28, 0.06, 0.28, 0.12)), M(rect(-0.22, 0.12, 0.22, 0.42)), P('l', rect(-0.22, 0.38, 0.22, 0.42)), M(rect(-0.27, 0.42, 0.27, 0.46)),
        M([[-0.1, 0.46], [0.12, 0.46], [0.1, 0.74], [0.13, 0.84], [0.07, 0.88], [0.07, 0.94], [0.03, 0.96], [-0.03, 0.96], [-0.05, 0.92], [-0.05, 0.88], [-0.1, 0.82], [-0.12, 0.72]]), M([[-0.05, 0.96], [-0.01, 1.0], [0.06, 0.98], [0.04, 0.94]]),
        M(rect(0.17, 0.46, 0.185, 1.0)), M([[0.165, 1.0], [0.19, 1.0], [0.1775, 1.05]]), P('d', arc(-0.15, 0.68, 0.09, 0.09, 0, Math.PI * 2, 16))];
    const obelisk = (x = 0, h = 0.9, w = 0.07) => [M(rect(x - w * 1.8, 0, x + w * 1.8, 0.05)), M(rect(x - w * 1.4, 0.05, x + w * 1.4, 0.1)), M([[x - w, 0.1], [x + w, 0.1], [x + w * 0.55, h], [x - w * 0.55, h]]), M([[x - w * 0.55, h], [x + w * 0.55, h], [x, h + 0.07]]), P('l', [[x - w, 0.1], [x - w * 0.6, 0.1], [x - w * 0.3, h], [x - w * 0.55, h]])];
    const argo = () => [M([[-0.8, 0.2], [-0.7, 0.1], [-0.4, 0.05], [0.4, 0.05], [0.7, 0.1], [0.86, 0.3], [0.78, 0.32], [0.5, 0.22], [-0.5, 0.22], [-0.78, 0.32]]), M([[-0.8, 0.2], [-0.92, 0.4], [-0.84, 0.44], [-0.68, 0.24]]), M(rect(-0.01, 0.22, 0.015, 0.92)), M(rect(-0.28, 0.82, 0.3, 0.84)),
        P('l', [[-0.26, 0.4], [-0.26, 0.81], [0.28, 0.81], [0.28, 0.4], [0.0, 0.34]]), ...[-0.5, -0.3, -0.1, 0.1, 0.3, 0.5].map(x => P('d', arc(x, 0.16, 0.035, 0.035, 0, Math.PI * 2, 10))), ...[-0.4, -0.15, 0.1, 0.35, 0.6].map(x => M([[x, 0.1], [x + 0.02, 0.1], [x + 0.14, 0.0], [x + 0.12, 0.0]]))];
    const falls = () => [M([[-0.95, 0], [-0.95, 0.7], [-0.6, 0.78], [-0.25, 0.72], [0.25, 0.72], [0.6, 0.8], [0.95, 0.7], [0.95, 0]]), P('d', rect(-0.3, 0.1, 0.3, 0.72)), ...[-0.24, -0.14, -0.03, 0.08, 0.18].map(x => P('l', rect(x, 0.06, x + 0.07, 0.72))), P('l', rect(-0.32, 0.04, 0.32, 0.1)), M([[-0.95, 0.7], [-0.7, 0.86], [-0.4, 0.8], [-0.25, 0.72]]), M([[0.25, 0.72], [0.5, 0.88], [0.8, 0.84], [0.95, 0.7]])];
    const euripus = () => [M(rect(-0.98, 0.1, 0.98, 0.14)), M(rect(-0.98, 0.14, 0.98, 0.155)), ...[-0.8, -0.5, 0.5, 0.8].map(x => M(rect(x - 0.025, 0, x + 0.025, 0.1))), M(rect(-0.34, 0.1, -0.29, 0.72)), M(rect(0.29, 0.1, 0.34, 0.72)),
        ...[-1, 1].flatMap(s => [0.15, 0.3, 0.45].map((f, i) => M([[s * 0.315, 0.72 - i * 0.04], [s * (0.35 + f * 1.6), 0.15], [s * (0.35 + f * 1.6 + 0.012), 0.15], [s * 0.315, 0.72 - i * 0.04 - 0.01]])))];
    const argostoli = () => [...bridge(9, 0.09, 0.015, 0.95), ...obelisk(0.0, 0.62, 0.055)];
    const rhodesPalace = () => [M(rect(-0.95, 0, 0.95, 0.38)), ...merl(-0.95, 0.95, 0.38, 0.03, 20), ...keep(-0.6, 0.3, 0, 0.78), ...keep(0.6, 0.3, 0, 0.78), ...keep(0, 0.34, 0.38, 0.62, 4), archOpen(0, 0, 0.2, 0.34), ...[-0.85, -0.3, 0.3, 0.85].map(x => slit(x, 0.18, 0.1))];
    const plastiras = () => [M([[-1, 0], [-0.9, 0.5], [-0.5, 0.62], [-0.2, 0.34], [-0.2, 0], [0.2, 0], [0.2, 0.34], [0.5, 0.6], [0.9, 0.5], [1, 0]]), M(rect(-0.2, 0.22, 0.2, 0.36)), P('l', rect(-0.2, 0.34, 0.2, 0.36)), ...[-0.12, -0.04, 0.04, 0.12].map(x => P('d', rect(x - 0.012, 0.24, x + 0.012, 0.31))), M(rect(-0.03, 0.36, 0.03, 0.5))];
    const kilkis = () => [M(rect(-0.4, 0, 0.4, 0.05)), M(rect(-0.34, 0.05, 0.34, 0.1)), M(rect(-0.28, 0.1, 0.28, 0.15)), M(rect(-0.16, 0.15, 0.16, 0.9)), P('l', rect(-0.16, 0.15, -0.1, 0.9)), M(rect(-0.2, 0.9, 0.2, 0.94)), M([[-0.2, 0.94], [0.2, 0.94], [0, 1.0]]), archOpen(0, 0.15, 0.1, 0.2)];
    const mansion = () => [M(rect(-0.62, 0, 0.62, 0.3)), M(rect(-0.72, 0.3, 0.72, 0.62)), P('l', rect(-0.72, 0.3, 0.72, 0.325)), M([[-0.82, 0.62], [0.82, 0.62], [0.55, 0.8], [-0.55, 0.8]]), M(rect(0.28, 0.8, 0.36, 0.92)), ...windows(-0.66, 0.34, 0.66, 0.6, 5, 1, 0.5, 0.7), ...windows(-0.56, 0.06, 0.56, 0.28, 4, 1, 0.5, 0.7), archOpen(0, 0, 0.14, 0.24)];
    const corfuFort = () => [M([[-1, 0], [-0.95, 0.3], [-0.7, 0.56], [-0.45, 0.6], [-0.3, 0.35], [-0.12, 0.18], [0.1, 0.34], [0.3, 0.62], [0.55, 0.52], [0.8, 0.3], [1, 0]]), ...wallRun(-0.8, -0.38, 0.52, 0.66), ...keep(-0.78, 0.14, 0.52, 0.8), ...keep(-0.45, 0.12, 0.54, 0.72),
        ...wallRun(0.2, 0.62, 0.52, 0.64), ...keep(0.4, 0.14, 0.54, 0.86), M(rect(0.5, 0.62, 0.53, 0.76)), M([[0.49, 0.76], [0.54, 0.76], [0.515, 0.82]])];
    const koules = () => [M(rect(-0.95, 0, 0.95, 0.34)), ...merl(-0.95, 0.95, 0.34, 0.035, 22), ...arcade(-0.9, 0.9, 0.04, 0.2, 9, 0.5), M(rect(-0.2, 0.34, 0.25, 0.5)), ...merl(-0.2, 0.25, 0.5, 0.035, 6), M(rect(0.6, 0.34, 0.9, 0.44)), P('l', rect(-0.95, 0.31, 0.95, 0.34))];
