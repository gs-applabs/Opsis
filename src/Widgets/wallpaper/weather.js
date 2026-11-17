// Opsis β€” "Weather" wallpaper: a procedural scene driven by the real time of day (sun/moon/stars,
// sunrise & sunset computed from the location) and the live weather (clouds, rain, snow, fog, thunderstorms).
// Everything is drawn with Canvas 2D β€” no external assets, no network access from this file (the C# bridge supplies the weather).
// Public API (used by index.html): WeatherWallpaper.setConfig(cfg), .setWeather(data), .resize(w, h, dpr), .draw(dtSeconds, profile).
(function () {
'use strict';

const TAU = Math.PI * 2;
const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
const lerp = (a, b, t) => a + (b - a) * t;
const smooth = (t) => { t = clamp(t, 0, 1); return t * t * (3 - 2 * t); };

function mulberry32(seed) {
    return function () {
        seed |= 0; seed = (seed + 0x6D2B79F5) | 0;
        let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

function hex(h) {
    const s = h.replace('#', '');
    const n = parseInt(s, 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}
const mix = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
const rgba = (c, a) => `rgba(${c[0] | 0},${c[1] | 0},${c[2] | 0},${a})`;
const lum = (c) => 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2];
const scaleCol = (c, k) => [c[0] * k, c[1] * k, c[2] * k];

// Beaufort scale (continuous 0..12) from km/h β€” the thresholds are the standard upper limits of Bf 0..11
const BF_LIMITS = [1, 6, 12, 20, 29, 39, 50, 62, 75, 89, 103, 118];
function beaufort(kmh) {
    if (kmh < 1) return Math.max(0, kmh);
    let i = 0;
    while (i < BF_LIMITS.length && kmh >= BF_LIMITS[i]) i++;
    if (i >= BF_LIMITS.length) return 12;
    const lo = BF_LIMITS[i - 1], hi = BF_LIMITS[i];
    return i + (kmh - lo) / (hi - lo);
}
let BFN = 2;           // current (eased) Beaufort number, updated every frame

// ---------------------------------------------------------------- state
let W = 0, H = 0, DPR = 1, HORIZON = 0, K = 1;
let cfg = { weatherSim: 'Auto', timeSim: 'Auto', glass: true, lat: 37.9838, lon: 23.7275 };
let data = null;                       // last weather payload from the host
let animTime = 0;
const S = { cloud: 0.15, rain: 0, snow: 0, fog: 0, storm: 0, windX: 0, wind: 8, dir: 1, snowCover: 0, wet: 0 };   // eased, current values
const TGT = { cloud: 0.15, rain: 0, snow: 0, fog: 0, storm: 0, windX: 0, wind: 8, dir: 1 };                           // targets from the weather

// offscreen canvases
let skyCv = null, skyCtx = null;       // sky + celestial + clouds + hills  (W x HORIZON)
let cloudCv = null, cloudCtx = null;   // half-res cloud layer
let sceneCv = null, sceneCtx = null;   // full composite (only when glass drops are drawn)
let lightX = 0.3, moonSprite = null;
let cloudSprites = [], clouds = [], hills = [], stars = [], flakeSprite = null, fogSprites = [];
let rain = { far: [], mid: [], near: [] }, ripples = [], splashes = [], glass = [], bolt = null;
let nextBoltIn = 4, flash = 0, shoot = null, nextShootIn = 8;
let lastDensity = -1;
// quality budget per performance profile (the density already follows the profile): 2 = full (High), 1 = medium (default / balanced), 0 = simple (Low Power / Battery): fewer clouds, no Milky Way, foam, trees or rock texture
function tierLevel() { return lastDensity >= 0.95 ? 2 : lastDensity >= 0.5 ? 1 : 0; }

function makeCanvas(w, h) {
    const c = document.createElement('canvas');
    c.width = Math.max(1, Math.round(w)); c.height = Math.max(1, Math.round(h));
    return c;
}

// ---------------------------------------------------------------- sun / moon astronomy
function dayOfYear(d) {
    const start = Date.UTC(d.getUTCFullYear(), 0, 0);
    return Math.floor((d.getTime() - start) / 86400000);
}

let sunCache = { key: '', sr: 390, ss: 1170 };
function sunTimes(offMin) {
    const d = new Date();
    const key = `${d.getUTCFullYear()}-${dayOfYear(d)}-${cfg.lat}-${cfg.lon}-${offMin}`;
    if (key === sunCache.key) return sunCache;
    const rad = Math.PI / 180, lat = cfg.lat * rad;
    const gamma = TAU / 365 * (dayOfYear(d) - 1 + 0.5);
    const eqtime = 229.18 * (0.000075 + 0.001868 * Math.cos(gamma) - 0.032077 * Math.sin(gamma) - 0.014615 * Math.cos(2 * gamma) - 0.040849 * Math.sin(2 * gamma));
    const decl = 0.006918 - 0.399912 * Math.cos(gamma) + 0.070257 * Math.sin(gamma) - 0.006758 * Math.cos(2 * gamma) + 0.000907 * Math.sin(2 * gamma) - 0.002697 * Math.cos(3 * gamma) + 0.00148 * Math.sin(3 * gamma);
    const c = Math.cos(90.833 * rad) / (Math.cos(lat) * Math.cos(decl)) - Math.tan(lat) * Math.tan(decl);
    let sr = 390, ss = 1170;                                   // fallback 06:30 / 19:30 (also for polar day/night)
    if (c > -1 && c < 1) {
        const ha = Math.acos(c) / rad;
        const norm = (m) => ((m % 1440) + 1440) % 1440;
        sr = norm(720 - 4 * (cfg.lon + ha) - eqtime + offMin);
        ss = norm(720 - 4 * (cfg.lon - ha) - eqtime + offMin);
        if (ss < sr) ss += 1440;                               // sunset after midnight (very high offsets)
    }
    sunCache = { key, sr, ss };
    return sunCache;
}

function offsetMinutes() {
    if (data && typeof data.utcOffsetSec === 'number') return data.utcOffsetSec / 60;
    return -new Date().getTimezoneOffset();
}

function localMinutes() {
    const sim = cfg.timeSim;
    const { sr, ss } = sunTimes(offsetMinutes());
    if (sim === 'Dawn') return sr + 8;
    if (sim === 'Day') return (sr + ss) / 2;
    if (sim === 'Dusk') return ss - 4;
    if (sim === 'Night') return 60;
    const d = new Date();
    const utc = d.getUTCHours() * 60 + d.getUTCMinutes() + d.getUTCSeconds() / 60;
    return (utc + offsetMinutes() + 1440) % 1440;
}

// 0 = new moon, 0.5 = full moon (synodic month from a known new moon, 2000-01-06 18:14 UTC)
function moonPhase() {
    const days = (Date.now() - Date.UTC(2000, 0, 6, 18, 14)) / 86400000;
    return (((days / 29.530588853) % 1) + 1) % 1;
}

// ---------------------------------------------------------------- sky palette
const NIGHT = { top: hex('#04070f'), mid: hex('#070d1f'), hor: hex('#0e1a34'), amb: 0.05, glow: 0 };
function skyKeys(sr, ss) {
    const noon = (sr + ss) / 2;
    return [
        { t: sr - 100, ...NIGHT },
        { t: sr - 45, top: hex('#101a3c'), mid: hex('#2c3869'), hor: hex('#6f5079'), amb: 0.14, glow: 0.25 },
        { t: sr - 8,  top: hex('#27427e'), mid: hex('#9a7aa6'), hor: hex('#ff9d6b'), amb: 0.4, glow: 0.9 },
        { t: sr + 35, top: hex('#3a78c4'), mid: hex('#8fb6d9'), hor: hex('#ffd9a8'), amb: 0.72, glow: 0.6 },
        { t: sr + 120, top: hex('#2b79d0'), mid: hex('#5da7ec'), hor: hex('#c9e4f7'), amb: 0.93, glow: 0.2 },
        { t: noon,    top: hex('#1f6dce'), mid: hex('#4fa0ee'), hor: hex('#bfe0f8'), amb: 1, glow: 0.1 },
        { t: ss - 120, top: hex('#2b79d0'), mid: hex('#5da7ec'), hor: hex('#c9e4f7'), amb: 0.93, glow: 0.2 },
        { t: ss - 35, top: hex('#3a6bb8'), mid: hex('#d79a7e'), hor: hex('#ffc185'), amb: 0.72, glow: 0.7 },
        { t: ss + 2,  top: hex('#2a3f80'), mid: hex('#a65f7c'), hor: hex('#ff7f55'), amb: 0.4, glow: 1 },
        { t: ss + 40, top: hex('#141e4a'), mid: hex('#4a3a70'), hor: hex('#b0556c'), amb: 0.15, glow: 0.4 },
        { t: ss + 95, ...NIGHT },
    ];
}

function skyAt(t, sr, ss) {
    const keys = skyKeys(sr, ss);
    let tt = t;
    for (const cand of [t, t + 1440, t - 1440]) {
        if (cand >= keys[0].t && cand <= keys[keys.length - 1].t) { tt = cand; break; }
    }
    if (tt < keys[0].t || tt > keys[keys.length - 1].t) return { ...NIGHT };
    for (let i = 0; i < keys.length - 1; i++) {
        const a = keys[i], b = keys[i + 1];
        if (tt >= a.t && tt <= b.t) {
            const f = smooth((tt - a.t) / Math.max(1, b.t - a.t));
            return { top: mix(a.top, b.top, f), mid: mix(a.mid, b.mid, f), hor: mix(a.hor, b.hor, f), amb: lerp(a.amb, b.amb, f), glow: lerp(a.glow, b.glow, f) };
        }
    }
    return { ...NIGHT };
}

// ---------------------------------------------------------------- weather -> scene targets
function sceneFromCode(code) {
    switch (code) {
        case 0: return { cloud: 0.04, rain: 0, snow: 0, fog: 0, storm: 0 };
        case 1: return { cloud: 0.2, rain: 0, snow: 0, fog: 0, storm: 0 };
        case 2: return { cloud: 0.48, rain: 0, snow: 0, fog: 0, storm: 0 };
        case 3: return { cloud: 0.92, rain: 0, snow: 0, fog: 0, storm: 0 };
        case 45: case 48: return { cloud: 0.6, rain: 0, snow: 0, fog: 0.85, storm: 0 };
        case 51: case 53: case 55: case 56: case 57: return { cloud: 0.82, rain: code === 51 ? 0.18 : code === 53 ? 0.3 : 0.42, snow: 0, fog: 0.12, storm: 0 };
        case 61: return { cloud: 0.85, rain: 0.42, snow: 0, fog: 0.08, storm: 0 };
        case 63: return { cloud: 0.9, rain: 0.72, snow: 0, fog: 0.1, storm: 0 };
        case 65: return { cloud: 1, rain: 1, snow: 0, fog: 0.15, storm: 0 };
        case 66: case 67: return { cloud: 0.92, rain: 0.6, snow: 0, fog: 0.1, storm: 0 };
        case 71: return { cloud: 0.8, rain: 0, snow: 0.35, fog: 0.1, storm: 0 };
        case 73: return { cloud: 0.88, rain: 0, snow: 0.65, fog: 0.15, storm: 0 };
        case 75: return { cloud: 1, rain: 0, snow: 1, fog: 0.25, storm: 0 };
        case 77: return { cloud: 0.8, rain: 0, snow: 0.45, fog: 0.1, storm: 0 };
        case 80: return { cloud: 0.7, rain: 0.5, snow: 0, fog: 0, storm: 0 };
        case 81: return { cloud: 0.85, rain: 0.78, snow: 0, fog: 0.05, storm: 0 };
        case 82: return { cloud: 1, rain: 1, snow: 0, fog: 0.1, storm: 0 };
        case 85: return { cloud: 0.8, rain: 0, snow: 0.55, fog: 0.1, storm: 0 };
        case 86: return { cloud: 0.95, rain: 0, snow: 0.95, fog: 0.2, storm: 0 };
        case 95: return { cloud: 1, rain: 0.85, snow: 0, fog: 0.1, storm: 1 };
        case 96: case 99: return { cloud: 1, rain: 1, snow: 0, fog: 0.12, storm: 1 };
        default: return { cloud: 0.3, rain: 0, snow: 0, fog: 0, storm: 0 };
    }
}

const SIM_CODES = { Clear: 0, PartlyCloudy: 2, Cloudy: 3, Drizzle: 51, Rain: 63, HeavyRain: 65, Thunderstorm: 95, Snow: 73, Fog: 45 };

const WIND_SIM_KMH = { Calm: 3, LightBreeze: 15, FreshBreeze: 30, Strong: 52, Gale: 82 };
const SIM_WIND_KMH = { Clear: 8, PartlyCloudy: 12, Cloudy: 14, Drizzle: 12, Rain: 22, HeavyRain: 38, Thunderstorm: 55, Snow: 16, Fog: 3 };

function computeTargets() {
    let sc, wind = 8, windDir = 270, cloudPct = -1;
    const sim = cfg.weatherSim;
    if (sim && sim !== 'Auto' && SIM_CODES[sim] !== undefined) {
        sc = sceneFromCode(SIM_CODES[sim]);
        wind = SIM_WIND_KMH[sim] ?? 10;
    } else if (data && data.ok) {
        sc = sceneFromCode(data.code | 0);
        wind = +data.wind || 0; windDir = +data.windDir || 270;
        cloudPct = typeof data.cloud === 'number' ? data.cloud : -1;
        if (cloudPct >= 0) sc.cloud = Math.max(Math.min(1, cloudPct / 100 * 1.05), sc.rain > 0 || sc.snow > 0 ? 0.7 : 0);
    } else {
        sc = sceneFromCode(0);                                  // no data: clear sky, the time of day still works
    }
    // the user can force a wind strength (Beaufort preview) independently of the weather
    if (cfg.windSim && cfg.windSim !== 'Auto' && WIND_SIM_KMH[cfg.windSim] !== undefined) wind = WIND_SIM_KMH[cfg.windSim];
    TGT.cloud = sc.cloud; TGT.rain = sc.rain; TGT.snow = sc.snow; TGT.fog = sc.fog; TGT.storm = sc.storm;
    // wind in km/h -> px/s lateral drift for rain/snow (direction: the wind blows TOWARD windDir+180)
    const dirSign = -Math.sin(windDir * Math.PI / 180);
    const dir = Math.abs(dirSign) < 0.15 ? (dirSign < 0 ? -0.15 : 0.15) : dirSign;
    TGT.wind = clamp(wind, 0, 120);
    TGT.dir = dir < 0 ? -1 : 1;
    TGT.windX = clamp(wind, 0, 90) * 2.6 * dir;
}

// ---------------------------------------------------------------- generated assets
// ---- value noise + fbm (tileless, small lattice): the base of the cloud and moon textures ----
function noiseTable(seed) { const r = mulberry32(seed), t = new Float32Array(65536); for (let i = 0; i < 65536; i++) t[i] = r(); return t; }
function vnoise(t, x, y) {
    const xi = Math.floor(x), yi = Math.floor(y), xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
    const x0 = xi & 255, x1 = (xi + 1) & 255, y0 = (yi & 255) << 8, y1 = ((yi + 1) & 255) << 8;
    const a = t[y0 | x0], b = t[y0 | x1], c = t[y1 | x0], d = t[y1 | x1];
    return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
}
function fbm(t, x, y, oct) { let s = 0, a = 0.5, f = 1, n = 0; for (let i = 0; i < oct; i++) { s += a * vnoise(t, x * f, y * f); n += a; a *= 0.5; f *= 2.03; } return s / n; }

// Cloud sprites made from a density field (fbm, domain-warped) and lit by marching toward the light: thick parts shade themselves, thin edges glow (silver lining).
// Kinds: cumulus (flat base, cauliflower top), stratus (wide, layered), cirrus (thin streaks). Baked with the light on the LEFT; the draw loop mirrors a sprite to face the sun.
const CLOUD_KINDS = ['cumulus', 'cumulus', 'cumulus', 'stratus', 'stratus', 'cirrus'];
function buildCloudSprites() {
    cloudSprites = [];
    const cw = 480, ch = 200;
    for (let v = 0; v < CLOUD_KINDS.length; v++) {
        const kind = CLOUD_KINDS[v], tab = noiseTable(311 + v * 59), tab2 = noiseTable(977 + v * 13);
        const D = new Float32Array(cw * ch);
        for (let y = 0; y < ch; y++) for (let x = 0; x < cw; x++) {
            const u = x / cw, w = y / ch;
            const wx = (fbm(tab2, u * 3 + 5, w * 3, 3) - 0.5) * 0.35, wy = (fbm(tab2, u * 3, w * 3 + 9, 3) - 0.5) * 0.25;     // domain warp: billows, not blobs
            let d = 0;
            if (kind === 'cumulus') {
                const bump = fbm(tab, u * 2.6, 4.1, 3), qx = (u - 0.5) / 0.47, qy = (0.82 - w) / (0.66 * (0.7 + 0.6 * bump));
                const q = qx * qx + qy * qy, m = smooth((1 - q) / 0.55) * smooth((0.84 - w) / 0.06);       // a rounded dome with a flat base, lumpy outline
                const n = fbm(tab, u * 9 + wx * 6, w * 9 + wy * 6, 6);                                  // billows
                d = m * 0.9 + (n - 0.5) * 1.5 * smooth(m * 2.5) - 0.12;
                if (m <= 0.001) d = 0;
                D[y * cw + x] = clamp((d - 0.1) / 0.7, 0, 1); continue;
            } else if (kind === 'stratus') {
                const m = smooth(1 - Math.abs(w - 0.55) / 0.24) * smooth(1 - Math.abs(u - 0.5) * 1.9);
                d = m * 0.95 + (fbm(tab, u * 4 + wx * 3, w * 14 + wy * 3, 5) - 0.5) * 1.1 * smooth(m * 2);
                if (m <= 0.001) d = 0;
            } else {
                const m = smooth(1 - Math.abs(w - 0.5) / 0.4) * smooth(1 - Math.abs(u - 0.5) * 1.8);
                const streak = fbm(tab, u * 2.2 + wx * 2, w * 22 + wy * 6, 5);
                d = (m * 0.55 + (streak - 0.5) * 1.25) * 0.8;
                if (m <= 0.001) d = 0;
            }
            D[y * cw + x] = clamp((d - 0.16) / 0.5, 0, 1);
        }
        const c = makeCanvas(cw, ch), g = c.getContext('2d'), img = g.createImageData(cw, ch), px = img.data;
        const lx = -1.2, ly = -1.0;                                                                 // toward the light (up-left), in pixels per march step
        const shade = [104, 118, 152], lit = [255, 251, 244];
        for (let y = 0; y < ch; y++) for (let x = 0; x < cw; x++) {
            const i = y * cw + x, dens = D[i];
            if (dens <= 0.004) continue;
            let od = 0;
            for (let k = 1; k <= 7; k++) { const sx = Math.round(x + lx * k * 2.2), sy = Math.round(y + ly * k * 2.2); if (sx >= 0 && sx < cw && sy >= 0 && sy < ch) od += D[sy * cw + sx]; }
            let br = Math.exp(-od * 0.55);                                                          // light reaching this point through the cloud
            br = 0.26 + 0.74 * br + 0.1 * (1 - dens);                                              // thin edges glow a little (silver lining)
            br = clamp(br, 0, 1);
            const a = smooth(dens * 1.7);
            const o = i * 4;
            px[o] = shade[0] + (lit[0] - shade[0]) * br; px[o + 1] = shade[1] + (lit[1] - shade[1]) * br; px[o + 2] = shade[2] + (lit[2] - shade[2]) * br;
            px[o + 3] = (kind === 'cirrus' ? 0.55 : 0.96) * 255 * a;
        }
        g.putImageData(img, 0, 0);
        cloudSprites.push(c);
    }
}

function buildFlake() {
    flakeSprite = makeCanvas(32, 32);
    const g = flakeSprite.getContext('2d');
    const gr = g.createRadialGradient(16, 16, 0, 16, 16, 16);
    gr.addColorStop(0, 'rgba(255,255,255,1)'); gr.addColorStop(0.35, 'rgba(255,255,255,0.75)'); gr.addColorStop(1, 'rgba(255,255,255,0)');
    g.fillStyle = gr; g.fillRect(0, 0, 32, 32);
}

function buildFogSprites() {
    fogSprites = [];
    for (let v = 0; v < 3; v++) {
        const c = makeCanvas(1024, 256), g = c.getContext('2d'), rnd = mulberry32(900 + v * 31);
        for (let i = 0; i < 40; i++) {
            const x = rnd() * 1024, y = 100 + rnd() * 56, r = 40 + rnd() * 60;
            for (const off of [-1024, 0, 1024]) {                 // wrap horizontally so the sprite tiles without a seam
                const gr = g.createRadialGradient(x + off, y, 0, x + off, y, r * 1.7);
                gr.addColorStop(0, 'rgba(255,255,255,0.15)'); gr.addColorStop(1, 'rgba(255,255,255,0)');
                g.fillStyle = gr; g.save(); g.translate(x + off, y); g.scale(1, 0.4); g.translate(-(x + off), -y);
                g.beginPath(); g.arc(x + off, y, r * 1.7, 0, TAU); g.fill(); g.restore();
            }
        }
        fogSprites.push(c);
    }
}

function buildStars() {
    const rnd = mulberry32(42);
    stars = [];
    const n = 260;
    for (let i = 0; i < n; i++) {
        stars.push({ x: rnd(), y: Math.pow(rnd(), 1.25), r: rnd() < 0.08 ? 1.7 : 0.5 + rnd() * 0.9, ph: rnd() * TAU, sp: 0.6 + rnd() * 2.2, big: rnd() < 0.06 });
    }
}

function buildHills() {
    // ridged fbm ridges: five ranges from far (pale, low) to near (dark, jagged, wooded); the middle of the picture stays open for the sun and the moon
    hills = [];
    const specs = [
        { depth: -0.6, amp: 0.34, base: -0.004, freq: 1.3, seed: 3, snow: 0.55, trees: false },
        { depth: -0.2, amp: 0.3, base: 0.0,   freq: 1.8, seed: 5, snow: 0.62, trees: false },
        { depth: 0.2,  amp: 0.22, base: 0.006, freq: 2.6, seed: 11, snow: 0.7, trees: false },
        { depth: 0.6,  amp: 0.14,  base: 0.012, freq: 3.7, seed: 23, snow: 2, trees: true },
        { depth: 1.0,  amp: 0.09, base: 0.02, freq: 5.5, seed: 41, snow: 2, trees: true },
    ];
    const samples = 260;
    for (const sp of specs) {
        const tab = noiseTable(sp.seed * 131);
        const pts = [];
        for (let i = 0; i <= samples; i++) {
            const x = i / samples;
            let r = 0, a = 0.55, f = 1, n = 0;
            for (let o = 0; o < 5; o++) { const v = 1 - Math.abs(2 * vnoise(tab, x * sp.freq * f + 7.3, 11.1 + o * 3.7) - 1); r += a * v * v; n += a; a *= 0.5; f *= 2.1; }
            r /= n;
            const edge = Math.pow(Math.abs(x - 0.5) * 2, 1.35);
            pts.push(sp.base + sp.amp * (0.12 + 0.88 * r) * (0.28 + 0.72 * edge));
        }
        const rnd = mulberry32(sp.seed * 7);
        const trees = [];
        if (sp.trees) for (let i = 0; i < 70; i++) { const idx = Math.floor(rnd() * samples); trees.push({ idx, h: 0.012 + rnd() * 0.02 * (0.5 + sp.depth * 0.5), ph: rnd() * TAU, w: 0.55 + rnd() * 0.35, broad: rnd() < 0.4 }); }
        hills.push({ ...sp, pts, trees });
    }
}

function buildClouds() {
    clouds = [];
    const rnd = mulberry32(7);
    for (let i = 0; i < 30; i++) {
        const layer = i % 3;                                    // 0 high/small/slow, 2 low/big/fast
        clouds.push({
            sprite: layer === 0 ? (i % 2 ? 5 : 3) : (i % 3),
            x: rnd(), y: 0.06 + rnd() * 0.5 + layer * 0.04,
            scale: 0.55 + layer * 0.35 + rnd() * 0.3,
            speed: 3 + layer * 5 + rnd() * 3,
            alpha: 0.55 + rnd() * 0.4,
            layer, rank: ((i * 11) % 30) / 30,
        });
    }
    clouds.sort((a, b) => a.layer - b.layer || a.y - b.y);
}

function buildDrops(density) {
    const area = (W * H) / (1920 * 1080);
    const make = (n, f) => Array.from({ length: Math.round(n * area * density) }, f);
    const rnd = Math.random;
    const groundSpan = () => HORIZON + rnd() * (H - HORIZON);
    rain.far = make(720, () => ({ x: rnd() * (W + 200), y: rnd() * H, v: 760 + rnd() * 160, l: 0.9, g: HORIZON + rnd() * (H - HORIZON) * 0.18, a: 0.5 + rnd() * 0.5 }));
    rain.mid = make(420, () => ({ x: rnd() * (W + 200), y: rnd() * H, v: 1150 + rnd() * 250, l: 1.2, g: HORIZON + (H - HORIZON) * (0.15 + rnd() * 0.35), a: 0.5 + rnd() * 0.5 }));
    rain.near = make(170, () => ({ x: rnd() * (W + 200), y: rnd() * H, v: 1750 + rnd() * 350, l: 1.7, g: HORIZON + (H - HORIZON) * (0.4 + rnd() * 0.6), a: 0.5 + rnd() * 0.5 }));
    void groundSpan;
}

function densityFor(profile) {
    switch (profile && profile.id) {
        case 'High': return 1.0;
        case 'Low Power': return 0.35;
        case 'Battery': return 0.15;
        default: return 0.65;
    }
}

// ---------------------------------------------------------------- public API
function resize(w, h, dpr) {
    W = Math.max(2, Math.round(w)); H = Math.max(2, Math.round(h)); DPR = dpr || 1;
    HORIZON = Math.round(H * 0.64);
    K = Math.max(0.7, W / 1920);
    skyCv = makeCanvas(W, HORIZON); skyCtx = skyCv.getContext('2d');
    cloudCv = makeCanvas(W / 2, HORIZON / 2); cloudCtx = cloudCv.getContext('2d');
    sceneCv = null; sceneCtx = null;
    lastDensity = -1;
    if (!cloudSprites.length) { buildCloudSprites(); buildMoonSprite(); buildFlake(); buildFogSprites(); buildStars(); buildClouds(); buildWaterAssets(); }
    reflCv = null;
    buildHills();
}

function setConfig(c) {
    const old = cfg;
    cfg = { ...cfg, ...c };
    if (typeof cfg.lat !== 'number' || isNaN(cfg.lat)) cfg.lat = 37.9838;
    if (typeof cfg.lon !== 'number' || isNaN(cfg.lon)) cfg.lon = 23.7275;
    if (old.weatherSim !== cfg.weatherSim || old.windSim !== cfg.windSim) computeTargets();
    sunCache.key = '';
}

function setWeather(d) { data = d; computeTargets(); }

// ---------------------------------------------------------------- drawing
function approach(cur, tgt, dt, rate) { return cur + (tgt - cur) * Math.min(1, dt * rate); }

function drawSkyLayer(sky, t, sr, ss) {
    const g = skyCtx;
    g.globalCompositeOperation = 'source-over';
    g.globalAlpha = 1;
    // weather grading: overcast skies are grey and darker, storms darker still
    const grey = clamp(S.cloud * 0.8 + S.storm * 0.15, 0, 0.92);
    const dark = clamp(S.cloud * 0.22 + S.storm * 0.34 + S.rain * 0.1 + S.snow * 0.05, 0, 0.7);
    const grade = (c) => { const L = lum(c); return scaleCol(mix(c, [L, L, L * 1.02], grey), 1 - dark); };
    const top = grade(sky.top), mid = grade(sky.mid), hor = grade(sky.hor);
    const hz = HORIZON;
    const gr = g.createLinearGradient(0, 0, 0, hz);
    gr.addColorStop(0, rgba(top, 1)); gr.addColorStop(0.55, rgba(mid, 1)); gr.addColorStop(1, rgba(hor, 1));
    g.fillStyle = gr; g.fillRect(0, 0, W, hz);
    return { top, mid, hor };
}

function drawStarsLayer(sky, vis) {
    const a = vis * (1 - sky.amb * 2.4);
    if (a <= 0.02) return;
    const g = skyCtx;
    for (const s of stars) {
        const tw = 0.6 + 0.4 * Math.sin(animTime * s.sp + s.ph);
        const x = s.x * W, y = s.y * HORIZON * 0.95;
        g.fillStyle = `rgba(235,242,255,${clamp(a * tw, 0, 1)})`;
        g.beginPath(); g.arc(x, y, s.r, 0, TAU); g.fill();
        if (s.big && a * tw > 0.4) {
            g.strokeStyle = `rgba(235,242,255,${a * tw * 0.4})`; g.lineWidth = 0.7;
            g.beginPath(); g.moveTo(x - 5, y); g.lineTo(x + 5, y); g.moveTo(x, y - 5); g.lineTo(x, y + 5); g.stroke();
        }
    }
    // shooting star on clear nights
    if (shoot) {
        const p = shoot.life / shoot.dur, hx = shoot.x + shoot.dx * p, hy = shoot.y + shoot.dy * p;
        const tx = hx - shoot.dx * 0.18, ty = hy - shoot.dy * 0.18;
        const lg = g.createLinearGradient(tx, ty, hx, hy);
        lg.addColorStop(0, 'rgba(255,255,255,0)'); lg.addColorStop(1, `rgba(255,255,255,${0.9 * (1 - p) * a})`);
        g.strokeStyle = lg; g.lineWidth = 1.6; g.beginPath(); g.moveTo(tx, ty); g.lineTo(hx, hy); g.stroke();
    }
}

// A textured moon: fbm surface, dark maria, craters, limb darkening (all procedural; no outside image, no licence to carry).
function buildMoonSprite() {
    const n = 256, c = makeCanvas(n, n), g = c.getContext('2d'), img = g.createImageData(n, n), px = img.data, t1 = noiseTable(4242), t2 = noiseTable(777);
    const craters = []; const rnd = mulberry32(99);
    for (let i = 0; i < 70; i++) { const a = rnd() * TAU, d = Math.sqrt(rnd()) * 0.92; craters.push([Math.cos(a) * d, Math.sin(a) * d, 0.02 + Math.pow(rnd(), 2.2) * 0.1]); }
    for (let y = 0; y < n; y++) for (let x = 0; x < n; x++) {
        const nx = (x / n) * 2 - 1, ny = (y / n) * 2 - 1, rr = Math.sqrt(nx * nx + ny * ny);
        if (rr > 1) continue;
        let b = 0.8 + 0.2 * (fbm(t1, nx * 6 + 7, ny * 6 + 3, 5) - 0.5) * 2;
        const maria = smooth((fbm(t2, nx * 2.3 + 2, ny * 2.3 + 8, 3) - 0.5) / 0.1 + 0.2);       // large dark plains
        b *= 1 - 0.42 * maria * smooth(1.05 - rr);
        for (const [cx, cy, cr] of craters) {
            const dx = nx - cx, dy = ny - cy, d = Math.sqrt(dx * dx + dy * dy) / cr;
            if (d < 1.25) { const rim = Math.exp(-Math.pow((d - 1) / 0.16, 2)), bowl = smooth(1 - d); b += 0.14 * rim * (dx + dy < 0 ? 1 : 0.35) - 0.12 * bowl * (maria < 0.5 ? 1 : 0.5); }
        }
        b *= 0.74 + 0.26 * Math.sqrt(Math.max(0, 1 - rr * rr));                                  // limb darkening
        b = clamp(b, 0, 1.1);
        const o = (y * n + x) * 4, edge = smooth((1 - rr) / 0.02);
        px[o] = Math.min(255, 238 * b); px[o + 1] = Math.min(255, 233 * b); px[o + 2] = Math.min(255, 220 * b); px[o + 3] = 255 * edge;
    }
    g.putImageData(img, 0, 0);
    moonSprite = c;
}

function drawMoonLayer(t, sr, ss, vis) {
    if (vis <= 0.02) return null;
    const g = skyCtx;
    const night = 1440 - (ss - sr);
    let v = ((t - ss) % 1440 + 1440) % 1440 / night;
    if (v > 1) return null;
    const e = Math.sin(Math.PI * clamp(v, 0, 1));
    const x = W * (0.12 + 0.76 * v), y = HORIZON - e * HORIZON * 0.72 + 6;
    const r = Math.max(18, Math.min(W, H) * 0.042);
    lightX = x / W;
    const alpha = vis * (1 - S.cloud * 0.9) * smooth((e + 0.06) / 0.14);
    if (alpha <= 0.02) return { x, y, r, alpha: 0 };
    // halo
    g.globalCompositeOperation = 'lighter';
    const halo = g.createRadialGradient(x, y, r * 0.8, x, y, r * 7);
    halo.addColorStop(0, `rgba(190,205,240,${0.30 * alpha})`); halo.addColorStop(0.12, `rgba(190,205,240,${0.16 * alpha})`);
    halo.addColorStop(0.35, `rgba(190,205,240,${0.06 * alpha})`); halo.addColorStop(0.7, `rgba(190,205,240,${0.015 * alpha})`); halo.addColorStop(1, 'rgba(190,205,240,0)');
    g.fillStyle = halo; g.beginPath(); g.arc(x, y, r * 7, 0, TAU); g.fill();
    g.globalCompositeOperation = 'source-over';
    // disc: earthshine + lit part with the real phase
    g.save(); g.translate(x, y);
    g.globalAlpha = alpha;
    g.fillStyle = 'rgba(60,70,95,0.9)'; g.beginPath(); g.arc(0, 0, r, 0, TAU); g.fill();
    const p = moonPhase();
    const waning = p > 0.5, ph = waning ? (1 - p) * TAU : p * TAU;     // 0..PI
    const c = Math.cos(ph);
    g.save(); if (waning) g.scale(-1, 1);
    g.beginPath();
    const N = 36;
    for (let i = 0; i <= N; i++) { const th = -Math.PI / 2 + Math.PI * i / N; const px = r * Math.cos(th), py = r * Math.sin(th); i ? g.lineTo(px, py) : g.moveTo(px, py); }
    for (let i = N; i >= 0; i--) { const th = -Math.PI / 2 + Math.PI * i / N; g.lineTo(r * c * Math.cos(th), r * Math.sin(th)); }
    g.closePath();
    g.fillStyle = 'rgba(214,220,232,1)';
    g.fill();
    g.clip();
    if (moonSprite) g.drawImage(moonSprite, -r, -r, 2 * r, 2 * r);
    g.restore();
    g.restore();
    return { x, y, r, alpha };
}

function drawSunLayer(sky, t, sr, ss) {
    const u = (t - sr) / Math.max(1, ss - sr);
    if (u < -0.12 || u > 1.12) return null;
    const e = Math.sin(Math.PI * clamp(u, -0.12, 1.12));
    const x = W * (0.1 + 0.8 * clamp(u, -0.1, 1.1)), y = HORIZON - e * HORIZON * 0.78;
    const r = Math.max(18, Math.min(W, H) * 0.04);
    const vis = smooth((e + 0.1) / 0.16) * (1 - S.cloud * 0.82) * (1 - S.fog * 0.3);
    if (e > 0.02) lightX = x / W;
    const g = skyCtx;
    // wide horizon glow follows the sun even behind clouds (dimmer)
    const warm = mix(hex('#ffffff'), hex('#ff9a5a'), clamp(1 - e * 2.2, 0, 1));
    g.globalCompositeOperation = 'lighter';
    const glowA = (0.18 + 0.5 * sky.glow) * (0.35 + 0.65 * (1 - S.cloud * 0.7));
    const gg = g.createRadialGradient(x, y, 0, x, y, Math.max(W, HORIZON) * 0.55);
    gg.addColorStop(0, rgba(warm, glowA * 0.75)); gg.addColorStop(0.25, rgba(warm, glowA * 0.28)); gg.addColorStop(1, rgba(warm, 0));
    g.fillStyle = gg; g.fillRect(0, 0, W, HORIZON);
    if (vis > 0.02) {
        const cg = g.createRadialGradient(x, y, r * 0.2, x, y, r * 3.2);
        cg.addColorStop(0, rgba(warm, 0.95 * vis)); cg.addColorStop(0.3, rgba(warm, 0.35 * vis)); cg.addColorStop(1, rgba(warm, 0));
        g.fillStyle = cg; g.beginPath(); g.arc(x, y, r * 3.2, 0, TAU); g.fill();
        const dg = g.createRadialGradient(x, y, 0, x, y, r);
        dg.addColorStop(0, rgba([255, 255, 250], vis)); dg.addColorStop(0.8, rgba(mix(warm, [255, 255, 255], 0.7), vis)); dg.addColorStop(1, rgba(mix(warm, [255, 236, 200], 0.5), vis * 0.85));
        g.fillStyle = dg; g.beginPath(); g.arc(x, y, r, 0, TAU); g.fill();
        // lens bloom: a faint horizontal streak and a wide soft halo
        const bl = g.createLinearGradient(x - r * 9, y, x + r * 9, y);
        bl.addColorStop(0, rgba(warm, 0)); bl.addColorStop(0.5, rgba(warm, 0.22 * vis)); bl.addColorStop(1, rgba(warm, 0));
        g.fillStyle = bl; g.fillRect(x - r * 9, y - r * 0.18, r * 18, r * 0.36);
        // crepuscular rays through broken cloud: thin fans, slowly breathing
        const rayA = 0.06 * vis * clamp(S.cloud * 1.6, 0, 1) * (1 - S.rain);
        if (rayA > 0.004) {
            const tt = performance.now() / 9000; g.save(); g.translate(x, y);
            for (let i = 0; i < 14; i++) {
                const ang = Math.PI * (0.08 + 0.84 * (i / 13)) + Math.sin(tt + i * 1.7) * 0.025, len = Math.max(W, HORIZON) * (0.5 + 0.25 * Math.sin(i * 2.3 + tt));
                const rg = g.createLinearGradient(0, 0, Math.cos(ang) * len, Math.sin(ang) * len);
                rg.addColorStop(0, rgba(warm, rayA * (0.6 + 0.4 * Math.sin(i * 3.1 + tt * 2)))); rg.addColorStop(1, rgba(warm, 0));
                g.fillStyle = rg; g.beginPath(); g.moveTo(0, 0); g.lineTo(Math.cos(ang - 0.018) * len, Math.sin(ang - 0.018) * len); g.lineTo(Math.cos(ang + 0.018) * len, Math.sin(ang + 0.018) * len); g.closePath(); g.fill();
            }
            g.restore();
        }
    }
    g.globalCompositeOperation = 'source-over';
    return { x, y, r, vis, e };
}

function cloudTint(sky) {
    const dayCol = [255, 255, 255], nightCol = [34, 42, 68];
    let c = mix(nightCol, dayCol, clamp(sky.amb * 1.15, 0, 1));
    c = mix(c, scaleCol(sky.hor, 1.05), clamp(sky.glow * 0.55, 0, 0.6));
    const heavy = clamp(S.storm * 0.95 + S.rain * 0.55 + S.cloud * 0.18, 0, 0.92);
    c = mix(c, scaleCol([86, 92, 106], 0.25 + 0.75 * clamp(sky.amb * 1.2, 0, 1)), heavy);
    c = scaleCol(c, 1 - flash * 0.0);
    if (flash > 0) c = mix(c, [235, 240, 255], clamp(flash, 0, 1));
    return c;
}

function drawCloudsLayer(sky, dt) {
    // rain/storm make the sky denser and lower: effective cover grows, clouds get bigger and sit lower
    const cover = clamp(S.cloud + S.rain * 0.35 + S.storm * 0.2, 0, 1);
    if (cover < 0.04) return;
    const g = cloudCtx, cw = cloudCv.width, ch = cloudCv.height;
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.globalCompositeOperation = 'source-over';
    g.clearRect(0, 0, cw, ch);
    const count = Math.min(clouds.length, Math.round((2 + 16 * cover + 8 * S.rain + 4 * S.storm) * [0.4, 0.75, 1][tierLevel()]));
    // clouds travel with the wind: ~3 px/s in calm air up to >100 px/s in a gale (per layer parallax: low clouds move faster)
    const base = (3 + S.wind * 1.5) * K;
    const dir = S.dir < 0 ? -1 : 1;
    const lower = 0.07 * (S.rain + S.storm * 0.5);
    const grow = 1 + 0.3 * S.rain + 0.15 * S.storm;
    for (let i = 0; i < clouds.length; i++) {
        const c = clouds[i];
        c.x += (dir * base * (0.45 + 0.4 * c.layer) * (0.85 + (c.speed % 3) * 0.08) * dt) / W;
        if (c.x > 1.3) c.x -= 1.6; else if (c.x < -0.3) c.x += 1.6;
        if (c.rank >= count / clouds.length) continue;                                           // which clouds are out: a spread over all layers and kinds, not only the highest ones
        const sp = cloudSprites[c.sprite];
        const kindK = CLOUD_KINDS[c.sprite % CLOUD_KINDS.length] === 'cirrus' ? 1.5 : 1;
        const w = sp.width * c.scale * grow * (W / 1920) * 0.78 * kindK, h = sp.height * c.scale * grow * (W / 1920) * 0.78 * kindK;
        g.globalAlpha = clamp(c.alpha * (0.35 + cover * 0.75) * (1 - S.fog * 0.45) + S.rain * 0.12, 0, 1);
        const flip = lightX >= c.x ? -1 : 1;                                                       // the lit side of the cloud faces the sun / moon
        g.save(); g.translate(c.x * cw, (c.y + lower) * ch); g.scale(flip, 1); g.drawImage(sp, -w / 2, -h / 2, w, h); g.restore();
    }
    g.globalAlpha = 1;
    g.globalCompositeOperation = 'source-atop';
    const tint = cloudTint(sky);
    g.fillStyle = rgba(tint, 0.08 + 0.5 * clamp(S.storm + S.rain * 0.55, 0, 1) + 0.2 * clamp(1 - sky.amb * 1.3, 0, 1));       // lighter tint than before: the baked self-shadowing stays visible
    g.fillRect(0, 0, cw, ch);
    g.globalCompositeOperation = 'source-over';
    skyCtx.globalAlpha = 1;
    skyCtx.drawImage(cloudCv, 0, 0, W, HORIZON);
    // heavy rain: a dark, dense overcast deck across the upper sky
    const deck = clamp(S.rain * 0.7 + S.storm * 0.35 - 0.1, 0, 0.75);
    if (deck > 0.02) {
        const dg = skyCtx.createLinearGradient(0, 0, 0, HORIZON * 0.62);
        const dc = scaleCol([70, 78, 92], 0.3 + 0.7 * clamp(sky.amb * 1.2, 0, 1));
        dg.addColorStop(0, rgba(dc, deck)); dg.addColorStop(1, rgba(dc, 0));
        skyCtx.fillStyle = dg; skyCtx.fillRect(0, 0, W, HORIZON * 0.62);
    }
}

// ---- Milky Way: a soft band of unresolved stars with dust lanes, visible on clear moonless-ish nights; one pre-rendered texture, drawn with a slow drift ----
let milkyCv = null;
function buildMilkyWay() {
    const w = 1024, h = 512, c = makeCanvas(w, h), g = c.getContext('2d'), img = g.createImageData(w, h), px = img.data, t1 = noiseTable(5150), t2 = noiseTable(8181);
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
        const u = x / w, v = y / h, band = (v - 0.5) + 0.12 * Math.sin(u * 5.2) + 0.05 * (fbm(t1, u * 3, 1.3, 3) - 0.5);
        const prof = Math.exp(-Math.pow(band / 0.09, 2)), core = Math.exp(-Math.pow((u - 0.42) / 0.22, 2));
        const cloud = fbm(t1, u * 9, v * 9, 5), dust = smooth((fbm(t2, u * 14, v * 22, 4) - 0.52) / 0.18);
        let d = prof * (0.35 + 0.75 * cloud) * (1 - 0.8 * dust * prof) * (0.55 + 0.7 * core);
        const sparkle = vnoise(t2, u * 220, v * 110) > 0.93 ? 0.6 : 0;
        d = clamp(d + sparkle * prof * 0.5, 0, 1);
        const o = (y * w + x) * 4, warm = core * 0.5;
        px[o] = 190 + 50 * warm; px[o + 1] = 205 + 20 * warm; px[o + 2] = 235 - 30 * warm; px[o + 3] = 255 * d * 0.85;
    }
    g.putImageData(img, 0, 0); milkyCv = c;
}
function drawMilkyWay(sky) {
    const vis = clamp(1 - sky.amb * 4, 0, 1) * (1 - S.cloud * 1.1) * (1 - S.fog) * (1 - S.rain) * (1 - 0.6 * (moonBright || 0));
    if (vis < 0.03 || tierLevel() < 1) return;
    if (!milkyCv) buildMilkyWay();
    const g = skyCtx; g.save(); g.globalCompositeOperation = 'lighter'; g.globalAlpha = 0.4 * vis;
    g.translate(W * 0.5, HORIZON * 0.45); g.rotate(-0.55 + Math.sin(animTime * 0.004) * 0.01);
    const sw = Math.max(W, HORIZON) * 1.5; g.drawImage(milkyCv, -sw / 2, -sw * 0.25, sw, sw * 0.5);
    g.restore();
}
let moonBright = 0;

// ---- rainbow: when the sun shines through light rain, opposite the sun ----
function drawRainbow(sky, sun) {
    if (!sun || sun.e < 0.04 || sun.e > 0.6) return;
    const f = smooth((S.rain - 0.04) / 0.1) * smooth((0.62 - S.rain) / 0.2) * (1 - S.storm) * clamp(sky.amb * 1.4, 0, 1) * (1 - S.cloud * 0.5) * (S.snow > 0.1 ? 0 : 1);
    if (f < 0.03) return;
    const g = skyCtx, R = Math.min(W, H) * 0.62, cx = W - sun.x, cy = HORIZON + R * (0.25 + sun.e * 0.7), lw = R * 0.014;
    const bands = [[255, 60, 60], [255, 150, 40], [255, 230, 70], [80, 210, 100], [70, 150, 255], [90, 80, 220], [160, 80, 200]];
    g.save(); g.globalCompositeOperation = 'lighter'; g.lineWidth = lw;
    for (let i = 0; i < bands.length; i++) { g.strokeStyle = rgba(bands[i], 0.2 * f); g.beginPath(); g.arc(cx, cy, R + i * lw, Math.PI * 1.08, Math.PI * 1.92); g.stroke(); }
    g.strokeStyle = rgba([255, 255, 255], 0.05 * f); g.lineWidth = lw * 8; g.beginPath(); g.arc(cx, cy, R + lw * 3, Math.PI * 1.08, Math.PI * 1.92); g.stroke();
    g.restore();
}

function hazeColor(hor) { return mix(hor, [190, 196, 206], 0.4); }

function drawHillsLayer(sky, graded) {
    if (onPhoto()) return;                                               // the aerial photograph has its own horizon
    const g = skyCtx;
    const amb = clamp(sky.amb, 0, 1);
    const shade = scaleCol([22, 32, 46], 0.28 + 0.72 * amb);
    const haze = hazeColor(graded.hor);
    const lowTier = tierLevel() < 1, richTier = tierLevel() >= 2;
    for (const h of hills) {
        // atmospheric perspective: far ranges are pale and blue (air between), near ranges dark and saturated
        const air = clamp(0.5 - h.depth * 0.5, 0, 1);
        let col = mix(graded.hor, shade, 0.2 + 0.62 * Math.max(0, h.depth + 0.2) * Math.max(0, h.depth + 0.2) + 0.16 * h.depth);
        col = mix(col, haze, clamp(air * 0.55 + S.fog * (0.9 - 0.35 * h.depth), 0, 0.9));
        col = mix(col, [212, 222, 238].map(v => v * (0.3 + 0.7 * amb)), clamp(S.snowCover * 0.75, 0, 0.8));
        if (flash > 0) col = mix(col, [200, 210, 235], clamp(flash * 0.5, 0, 0.6));
        const n = h.pts.length - 1;
        // slow parallax drift: the nearer the range, the more it moves (a few pixels, driven by time and wind)
        const dx = Math.sin(animTime * 0.05 + h.seed) * (2 + h.depth * 5) * K + S.windX * 0.01 * h.depth;
        g.save(); g.translate(dx, 0);
        const key = W + 'x' + H + 'x' + HORIZON;
        if (h.pathKey !== key) {                                          // the outline is rebuilt only when the size changes (no per-frame allocation)
            h.path = new Path2D(); h.path.moveTo(-20, HORIZON + 2);
            for (let i = 0; i <= n; i++) h.path.lineTo((i / n) * W, HORIZON - h.pts[i] * H);
            h.path.lineTo(W + 20, HORIZON + 2); h.path.closePath(); h.pathKey = key;
        }
        const path = h.path;
        // body: lighter at the ridge, deeper (and hazier) toward the foot
        const top = HORIZON - h.amp * H, lg = g.createLinearGradient(0, top, 0, HORIZON);
        lg.addColorStop(0, rgba(mix(col, [255, 245, 230], 0.1 * amb), 1)); lg.addColorStop(1, rgba(mix(col, haze, 0.35 + 0.3 * air), 1));
        g.fillStyle = lg; g.fill(path);
        if (!lowTier) {
            g.save(); g.clip(path);
            // rock / forest texture: dark gullies running down from the ridge
            if (richTier) {
            g.strokeStyle = rgba(mix(col, [8, 12, 18], 0.6), 0.16 + 0.1 * h.depth); g.lineWidth = Math.max(1, (0.6 + h.depth * 0.8) * K);
            g.beginPath();
            for (let i = 4; i < n - 4; i += 5 + (h.seed % 3)) {
                const x = (i / n) * W, y = HORIZON - h.pts[i] * H, len = (h.pts[i] * H) * (0.35 + 0.25 * ((i * 7 + h.seed) % 5) / 5);
                g.moveTo(x, y); g.lineTo(x + (((i * 13) % 7) - 3) * 1.6 * K, y + len);
            }
            g.stroke();
            }
            // snow caps on the tall peaks (and lower when it is snowing)
            const limit = h.snow - S.snowCover * 0.5;
            if (limit < 1.2) {
                g.fillStyle = rgba(mix([236, 240, 250], col, 0.1), 0.9 * (0.35 + 0.65 * amb));
                g.beginPath(); let open = false;
                for (let i = 0; i <= n; i++) {
                    const v = h.pts[i] / h.amp, x = (i / n) * W, y = HORIZON - h.pts[i] * H;
                    if (v > limit) { if (!open) { g.moveTo(x, y + 3 * K); open = true; } g.lineTo(x, y); }
                    else if (open) { g.lineTo(x, y + (4 + (v - limit) * -30) * K); g.closePath(); open = false; }
                }
                if (open) g.closePath();
                g.fill();
            }
            g.restore();
            // rim light along the ridge, on the side of the light
            g.lineWidth = Math.max(1, 1.1 * K); g.strokeStyle = rgba(mix(col, [255, 240, 215], 0.7), 0.28 * amb * (1 - S.cloud * 0.6));
            g.beginPath();
            for (let i = 0; i <= n; i++) { const x = (i / n) * W, y = HORIZON - h.pts[i] * H; if (i) g.lineTo(x, y); else g.moveTo(x, y); }
            g.stroke();
        }
        // trees on the near ranges: conifer silhouettes swaying with the wind
        if (h.trees.length && !lowTier) {
            const sway = (0.4 + S.wind * 0.05) * K, tc = mix(col, [10, 22, 16], 0.55);
            g.fillStyle = rgba(tc, 0.95); g.beginPath();
            for (const t of h.trees) {
                if (t.broad || (!richTier && (t.idx & 1))) continue;
                const x = (t.idx / n) * W, y = HORIZON - h.pts[t.idx] * H + 2, th = t.h * H, tw = th * t.w * 0.5, sw = Math.sin(animTime * (1.1 + S.wind * 0.04) + t.ph) * sway;
                g.moveTo(x - tw, y); g.lineTo(x + sw * 0.5, y - th * 0.55 + sw * 0.3); g.lineTo(x + tw, y);
                g.moveTo(x - tw * 0.75, y - th * 0.35); g.lineTo(x + sw, y - th); g.lineTo(x + tw * 0.75, y - th * 0.35);
            }
            g.fill();
            // deciduous trees: a thin trunk and a round, swaying crown (bare and pale under snow)
            const leaf = mix(mix(col, [40, 92, 48], 0.5 * (1 - S.snowCover)), [225, 232, 242], clamp(S.snowCover * 0.8, 0, 0.8));
            g.fillStyle = rgba(mix(leaf, [8, 14, 10], 0.25), 0.95); g.beginPath();
            for (const t of h.trees) {
                if (!t.broad || (!richTier && (t.idx & 1))) continue;
                const x = (t.idx / n) * W, y = HORIZON - h.pts[t.idx] * H + 2, th = t.h * H, sw = Math.sin(animTime * (0.9 + S.wind * 0.05) + t.ph) * sway * 1.4, r = th * 0.34;
                g.rect(x - r * 0.12, y - th * 0.5, r * 0.24, th * 0.5);
                g.moveTo(x + sw + r, y - th * 0.72); g.arc(x + sw, y - th * 0.72, r, 0, TAU);
                g.moveTo(x + sw - r * 0.5 + r * 0.7, y - th * 0.95); g.arc(x + sw - r * 0.5, y - th * 0.95, r * 0.7, 0, TAU);
            }
            g.fill();
        }
        g.restore();
    }
    // snow settles: a white bank along the shore and a thin sheet of ice on the water close to it
    if (S.snowCover > 0.04 && !onLand()) {
        const sc = clamp(S.snowCover, 0, 1), tab = snowTab || (snowTab = noiseTable(606)), bw = 6 * K * (0.6 + sc);
        g.fillStyle = rgba([232, 238, 248].map(v => v * (0.45 + 0.55 * amb)), 0.9 * sc);
        g.beginPath(); g.moveTo(0, HORIZON + 4);
        for (let x = 0; x <= W; x += 10) g.lineTo(x, HORIZON - bw * (0.4 + 1.4 * vnoise(tab, x * 0.02, 3.3)));
        g.lineTo(W, HORIZON + 4); g.closePath(); g.fill();
    }
    drawLandmarkOnHorizon(sky, graded, shade, haze);
}
let snowTab = null;

// The best-known landmark of the place where the computer is (the weather location, plan 2.1.5), as a distant silhouette in the colour of the hills: it follows the time of day, fog,
// snow and lightning of the scene. Chosen once per location; off with the "landmarks" setting or when nothing known is within 250 km.
let landmarkKey = '', landmarkPick = null, landmarkScene = { scene: 'sea' }, landmarkSide = -1, landmarkSideAt = 0;
function landmarkSideNow() { const n = Date.now(); if (n - landmarkSideAt > 60000) { landmarkSideAt = n; landmarkSide = new Date().getHours() < 13 ? -1 : 1; } return landmarkSide; }

// Chosen once per setting / location: the user's own choice (any landmark, independent of where the computer is), "Auto" = the nearest one to the weather location, "Off" = none.
function resolveLandmark() {
    const key = (cfg.landmarkId || 'Auto') + '|' + cfg.lat + ',' + cfg.lon + '|' + (cfg.landmarks === false);
    if (key === landmarkKey) return landmarkPick;
    landmarkKey = key; landmarkPick = null; landmarkScene = { scene: 'sea' };
    const L = window.OpsisLandmarks;
    if (cfg.landmarks !== false && L && cfg.landmarkId !== 'Off') {
        landmarkPick = cfg.landmarkId && cfg.landmarkId !== 'Auto' ? L.byId(cfg.landmarkId) : L.pickLandmark(cfg.lat, cfg.lon);
        if (landmarkPick) landmarkScene = L.sceneOf(landmarkPick.id);
    }
    return landmarkPick;
}
const onLand = () => !!(resolveLandmark() && landmarkScene.scene === 'land');
const onPhoto = () => !!(resolveLandmark() && landmarkScene.scene === 'photo');

// ---- aerial photograph as the landscape (Athens: the Acropolis and the city from Lycabettus, CC0, scenes/manifest.json) ----
// The picture fades to transparent at the top, so the animated sky (clouds, sun, moon, stars) shows through the haze of the gulf. It is tinted by the light of the scene in an
// offscreen canvas rebuilt only when the quantised light changes; at night a second image with warm window lights is added on top.
const photoState = { img: null, lights: null, ready: false, lightsReady: false, id: '', canvas: null, key: '' };
function drawScenePhoto(c2, sky, graded) {
    const L = window.OpsisLandmarks, ps = L.photoScene(landmarkPick.id); if (!ps) return;
    if (photoState.id !== landmarkPick.id) {
        photoState.id = landmarkPick.id; photoState.ready = photoState.lightsReady = false; photoState.key = '';
        photoState.img = new Image(); photoState.img.onload = () => { photoState.ready = true; }; photoState.img.src = ps.file;
        photoState.lights = new Image(); photoState.lights.onload = () => { photoState.lightsReady = true; }; photoState.lights.src = ps.lights;
    }
    if (!photoState.ready) return;
    const iw = photoState.img.naturalWidth, ih = photoState.img.naturalHeight, sc = Math.max(W / iw, 0.1), w = W, h = Math.round(ih * sc);
    const amb = clamp(sky.amb, 0, 1), night = clamp(1 - amb / 0.35, 0, 1), fog = clamp(S.fog, 0, 1), glow = clamp(sky.glow || 0, 0, 1);
    const key = [w, h, Math.round(amb * 14), Math.round(night * 8), Math.round(fog * 10), Math.round(glow * 4), Math.round(S.snowCover * 4)].join('/');
    if (photoState.key !== key) {
        const c = photoState.canvas || (photoState.canvas = makeCanvas(w, h)); if (c.width !== w || c.height !== h) { c.width = w; c.height = h; }
        const o = c.getContext('2d'); o.clearRect(0, 0, w, h); o.imageSmoothingQuality = 'high'; o.drawImage(photoState.img, 0, 0, w, h);
        const warm = mix([255, 236, 205], graded.hor, glow * 0.6), dusk = clamp(glow * (1 - amb) * 1.6, 0, 1);
        const dm = mix([62, 74, 110], [255, 255, 255], Math.pow(amb, 0.8)), mul = [0, 1, 2].map(i => dm[i] * (1 - dusk * 0.6 + dusk * 0.6 * warm[i] / 255));
        o.globalCompositeOperation = 'multiply'; o.fillStyle = `rgb(${mul[0] | 0},${mul[1] | 0},${mul[2] | 0})`; o.fillRect(0, 0, w, h);
        o.globalCompositeOperation = 'destination-in'; o.drawImage(photoState.img, 0, 0, w, h);                    // keep the alpha of the photo
        o.globalCompositeOperation = 'source-atop';
        if (fog > 0.02) { const hz = mix(graded.hor, [200, 205, 212], 0.4); o.fillStyle = rgba(hz, fog * 0.7); o.fillRect(0, 0, w, h); }
        if (S.snowCover > 0.05) { o.fillStyle = `rgba(232,238,250,${(0.16 * S.snowCover).toFixed(2)})`; o.fillRect(0, 0, w, h); }
        o.globalCompositeOperation = 'source-over'; photoState.key = key;
    }
    const y = H - h;                                                       // the photo stands on the bottom edge; if it is taller than the screen its hazy top is cut
    c2.drawImage(photoState.canvas, 0, y);
    if (night > 0.05 && photoState.lightsReady) { c2.save(); c2.globalAlpha = Math.min(1, night * 0.95); c2.globalCompositeOperation = 'lighter'; c2.drawImage(photoState.lights, 0, y, w, h); c2.restore(); }
}

// ---- birds: flocks of gulls / swifts crossing the sky on clear days, wings beating, each bird a little different ----
let birds = [], birdWait = 4;
function drawBirds(g, dt, sky) {
    const day = clamp(sky.amb * 1.5 - 0.15, 0, 1) * (1 - clamp(S.rain * 1.2, 0, 1)) * (1 - S.fog * 0.8) * (1 - S.snowCover * 0.6) * (1 - clamp(BFN / 9, 0, 1) * 0.6);
    if (day < 0.1 && birds.length === 0) return;
    birdWait -= dt;
    if (birdWait <= 0 && day > 0.35 && birds.length < 16) {
        const n = 3 + Math.floor(Math.random() * 6), dir = Math.random() < 0.5 ? 1 : -1, y0 = HORIZON * (0.12 + Math.random() * 0.5), sp = W * (0.035 + Math.random() * 0.03);
        for (let i = 0; i < n; i++) birds.push({ x: dir > 0 ? -30 - i * 26 : W + 30 + i * 26, y: y0 + (i % 2 ? 1 : -1) * i * 9 + (Math.random() - 0.5) * 14, vx: dir * sp * (0.9 + Math.random() * 0.2), ph: Math.random() * 6, f: 5 + Math.random() * 3, s: (8 + Math.random() * 6) * Math.max(0.8, H / 1080) });
        birdWait = 12 + Math.random() * 30;
    }
    if (birds.length === 0) return;
    g.save();
    const col = sky.amb < 0.35 ? '10,12,20' : '20,24,32';
    for (let i = birds.length - 1; i >= 0; i--) {
        const b = birds[i]; b.x += b.vx * dt; b.ph += b.f * dt; b.y += Math.sin(b.ph * 0.35) * 6 * dt;
        if (b.x < -80 || b.x > W + 80) { birds.splice(i, 1); continue; }
        const glide = 0.5 + 0.5 * Math.sin(b.ph * 0.09 + b.f);                                     // flap / glide cycle: sometimes the wings hold
        const flap = Math.sin(b.ph) * (0.35 + 0.65 * (glide > 0.35 ? 1 : glide / 0.35)) + (glide < 0.35 ? 0.25 : 0);
        const a = 0.82 * day * clamp(1 - Math.abs(b.y - HORIZON * 0.4) / (HORIZON * 0.9), 0.4, 1);
        const sc = b.s * (0.7 + 0.7 * clamp(b.y / HORIZON, 0, 1)), dir = b.vx < 0 ? -1 : 1;           // perspective: birds lower in the sky are nearer, so larger
        g.save(); g.translate(b.x, b.y); g.scale(dir, 1); g.rotate(Math.sin(b.ph * 0.35) * 0.05);
        g.fillStyle = `rgba(${col},${a.toFixed(3)})`;
        for (const side of [-1, 1]) {                                                            // two wings: leading edge bows up, tip follows the flap, trailing edge scallops back
            const tipX = side * sc * 1.25, tipY = -flap * sc * 0.95, midY = -flap * sc * 0.42 - sc * 0.12;
            g.beginPath(); g.moveTo(side * sc * 0.08, -sc * 0.06);
            g.quadraticCurveTo(side * sc * 0.55, midY - sc * 0.22, tipX, tipY);
            g.quadraticCurveTo(side * sc * 0.78, midY + sc * 0.16, side * sc * 0.42, sc * 0.1 - flap * sc * 0.1);
            g.quadraticCurveTo(side * sc * 0.22, sc * 0.2, side * sc * 0.06, sc * 0.1); g.closePath(); g.fill();
        }
        g.beginPath(); g.ellipse(0, sc * 0.03, sc * 0.2, sc * 0.1, 0, 0, TAU); g.fill();            // body
        g.beginPath(); g.ellipse(sc * 0.2, -sc * 0.02, sc * 0.07, sc * 0.06, 0, 0, TAU); g.fill();   // head
        g.beginPath(); g.moveTo(-sc * 0.15, sc * 0.04); g.lineTo(-sc * 0.42, sc * 0.09); g.lineTo(-sc * 0.15, sc * 0.1); g.closePath(); g.fill();   // tail
        g.restore();
    }
    g.restore();
}

// The landmark as a distant monument in the colour of the air between it and the viewer. On LAND it stands on a base of rock / grass that rises from the plain (the sprite's cut edge is hidden
// behind the base); in the SEA its foot stands at the waterline.
function drawLandmarkOnHorizon(sky, graded, shade, haze) {
    if (!resolveLandmark()) return;
    const amb = clamp(sky.amb, 0, 1);
    const pal = {
        amb, haze, fog: clamp(S.fog, 0, 1), snow: clamp(S.snowCover, 0, 1), night: clamp(1 - amb / 0.35, 0, 1),
        sunSide: landmarkSideNow(),                                       // the sun rises on the left of the picture and sets on the right
        warm: mix([255, 236, 205], graded.hor, clamp(sky.glow || 0, 0, 1) * 0.6),
        glow: clamp(sky.glow || 0, 0, 1), maxW: W * 0.24,
    };
    if (flash > 0) pal.amb = clamp(pal.amb + flash * 0.5, 0, 1);
    const maxH = Math.max(90, H * 0.34), L = window.OpsisLandmarks;
    if (landmarkScene.scene === 'photo') return;
    const size = L.spriteSize(landmarkPick.id, maxH, pal.maxW); if (!size) return;
    if (landmarkScene.scene === 'photo') return;                          // the photo scene replaces the hills, the monument and the ground (drawScenePhoto)
    if (landmarkScene.scene !== 'land') { L.drawLandmark(skyCtx, landmarkPick.id, W * 0.72, HORIZON + 2, maxH, pal); return; }
    L.drawLandmark(skyCtx, landmarkPick.id, W * 0.72, HORIZON - Math.round(size.h * 0.01), maxH, pal);
}

// Ground under a land monument instead of the sea: haze at the horizon fading to the dark plain, with the lights of the city at night. No reflections, no ripples.
let cityDots = null;
function drawLand(ctx2, sky, graded) {
    const hz = HORIZON, wh = H - hz, amb = clamp(sky.amb, 0, 1), gc = landmarkScene.ground;
    const base = gc === 'sand' ? [150, 128, 96] : gc === 'grass' ? [64, 82, 52] : [102, 92, 80];
    const far = mix(graded.hor, scaleCol(base, 0.4 + 0.6 * amb), 0.55);
    const near = scaleCol(base, 0.18 + 0.55 * amb);
    const wg = ctx2.createLinearGradient(0, hz, 0, H);
    wg.addColorStop(0, rgba(far, 1)); wg.addColorStop(0.35, rgba(mix(far, near, 0.6), 1)); wg.addColorStop(1, rgba(near, 1));
    ctx2.fillStyle = wg; ctx2.fillRect(0, hz, W, wh);
    // fields / terraces: a few soft horizontal bands that get wider towards the viewer
    ctx2.fillStyle = rgba(scaleCol(base, 0.3 * (0.4 + 0.6 * amb)), 0.18);
    for (let i = 1; i < 6; i++) { const y = hz + wh * (i * i / 36); ctx2.fillRect(0, y, W, Math.max(1, wh * 0.012 * i)); }
    const night = clamp(1 - amb / 0.35, 0, 1);
    if (night > 0.05) {
        if (!cityDots || cityDots.w !== W || cityDots.h !== H) {
            const pts = []; let seed = 7; const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
            for (let i = 0; i < 420; i++) { const d = Math.pow(rnd(), 1.8); pts.push([rnd() * W, hz + 3 + d * wh * 0.9, 1 + d * 1.6, 0.35 + rnd() * 0.65]); }
            cityDots = { w: W, h: H, pts };
        }
        for (const [x, y, r, a] of cityDots.pts) { ctx2.fillStyle = `rgba(255,${200 + (a * 40) | 0},${120 + (a * 60) | 0},${(a * night * 0.8).toFixed(3)})`; ctx2.fillRect(x, y, r, r); }
    }
}

function drawLightning(dt, sky) {
    if (S.storm < 0.3) { bolt = null; flash = 0; return; }
    nextBoltIn -= dt;
    if (nextBoltIn <= 0 && !bolt) {
        nextBoltIn = 3.5 + Math.random() * 9;
        const x0 = W * (0.15 + Math.random() * 0.7);
        const pts = [[x0, HORIZON * 0.12]];
        let x = x0, y = HORIZON * 0.12;
        const segs = 14;
        for (let i = 0; i < segs; i++) {
            y += (HORIZON * 0.9 - HORIZON * 0.12) / segs * (0.7 + Math.random() * 0.6);
            x += (Math.random() - 0.5) * 70 + S.windX * 0.05;
            pts.push([x, Math.min(y, HORIZON)]);
        }
        const branches = [];
        for (let i = 3; i < pts.length - 2; i += 2) if (Math.random() < 0.55) {
            let bx = pts[i][0], by = pts[i][1]; const b = [[bx, by]];
            const dir = Math.random() < 0.5 ? -1 : 1;
            for (let k = 0; k < 4; k++) { bx += dir * (14 + Math.random() * 26); by += 16 + Math.random() * 28; b.push([bx, by]); }
            branches.push(b);
        }
        bolt = { pts, branches, life: 0, dur: 0.55 };
    }
    if (bolt) {
        bolt.life += dt;
        const p = bolt.life / bolt.dur;
        // double flicker: strong, dip, stronger, fade
        const f = p < 0.12 ? p / 0.12 : p < 0.25 ? 0.35 : p < 0.38 ? 1 : Math.max(0, 1 - (p - 0.38) / 0.62);
        flash = clamp(f * 0.55 * S.storm, 0, 0.6);
        if (p >= 1) { bolt = null; flash = 0; }
    } else flash = Math.max(0, flash - dt * 2);
}

function drawBolt(ctx2) {
    if (!bolt) return;
    const p = bolt.life / bolt.dur;
    const a = p < 0.5 ? 1 : Math.max(0, 1 - (p - 0.5) / 0.5);
    ctx2.save();
    ctx2.globalCompositeOperation = 'lighter';
    ctx2.lineCap = 'round'; ctx2.lineJoin = 'round';
    const stroke = (pts, w, col) => { ctx2.strokeStyle = col; ctx2.lineWidth = w; ctx2.beginPath(); pts.forEach((q, i) => i ? ctx2.lineTo(q[0], q[1]) : ctx2.moveTo(q[0], q[1])); ctx2.stroke(); };
    ctx2.shadowColor = 'rgba(170,190,255,0.9)'; ctx2.shadowBlur = 24;
    stroke(bolt.pts, 5, `rgba(150,170,255,${0.45 * a})`);
    for (const b of bolt.branches) stroke(b, 3, `rgba(150,170,255,${0.3 * a})`);
    ctx2.shadowBlur = 8;
    stroke(bolt.pts, 2, `rgba(255,255,255,${0.95 * a})`);
    for (const b of bolt.branches) stroke(b, 1.1, `rgba(255,255,255,${0.8 * a})`);
    ctx2.restore();
}

// Position/strength of the sun or moon (set by draw()), used for the shimmering light path on the water
let celest = null;
let reflCv = null, reflCtx = null;
let glitter = [], glints = [], caps = [];
function buildWaterAssets() {
    const rnd = mulberry32(2024);
    glitter = Array.from({ length: 150 }, () => ({ u: rnd(), side: rnd() - 0.5, len: 0.4 + rnd() * 0.9, sp: 1.2 + rnd() * 3.2, ph: rnd() * TAU, hh: 0.7 + rnd() * 0.8 }));
    caps = Array.from({ length: 170 }, () => ({ u: rnd(), x: rnd(), len: 0.5 + rnd() * 1.2, sp: 1 + rnd() * 3, ph: rnd() * TAU }));
    glints = Array.from({ length: 70 }, () => ({ u: rnd(), x: rnd(), len: 0.04 + rnd() * 0.12, sp: 0.4 + rnd() * 1.4, a: 0.35 + rnd() * 0.65 }));
}

// Ξ–Ξ·Ο„Ξ®ΞΈΞ·ΞΊΞµ ΟΞ·Ο„Ξ¬ "ΞµΟ†Ξ¬ΟΞΌΞΏΟƒΞµ Ξ΄ΞΉΞ¬Ο†ΞΏΟΞΏΟ…Ο‚ ΞΊΟ…ΞΌΞ±Ο„ΞΉΟƒΞΌΞΏΟΟ‚, Ξ±Ο€ΞΏΟ‡ΟΟΟƒΞµΞΉΟ‚ ΞΊΞ±ΞΉ ΞµΟ†Ξ­ ΟƒΟ„ΞΉΟ‚ Ξ³ΟΞ±ΞΌΞΌΞ­Ο‚" (Ξ²ΞµΞ»Ο„Ξ―Ο‰ΟƒΞ·
// ΞΊΟ…ΞΌΞ¬Ο„Ο‰Ξ½) β€” Ξ­ΟΞµΟ…Ξ½Ξ± ΟƒΞµ Ξ±Ξ½Ο„Ξ―ΟƒΟ„ΞΏΞΉΟ‡ΞµΟ‚ canvas-based Ο„ΞµΟ‡Ξ½ΞΉΞΊΞ­Ο‚ (layered sine waves, Ξ³ΞµΞΌΞΉΟƒΞΌΞ­Ξ½ΞµΟ‚ -ΟΟ‡ΞΉ
// ΞΌΟΞ½ΞΏ Ξ³ΟΞ±ΞΌΞΌΞ­Ο‚- ΟΟƒΟ„Ξµ Ξ· ΞΌΞ―Ξ± Ξ»Ο‰ΟΞ―Ξ΄Ξ± Ξ½Ξ± ΞΊΞ±Ξ»ΟΟ€Ο„ΞµΞΉ Ο„Ξ·Ξ½ ΞµΟ€ΟΞΌΞµΞ½Ξ·, gradient Ξ±Ξ½Ξ¬ Ξ»Ο‰ΟΞ―Ξ΄Ξ±, Ξ¬ΞΈΟΞΏΞΉΟƒΞΌΞ± Ξ΄ΟΞΏ
// ΟƒΟ…Ο‡Ξ½ΞΏΟ„Ξ®Ο„Ο‰Ξ½ Ξ³ΞΉΞ± Ο€ΞΉΞΏ ΞΏΟΞ³Ξ±Ξ½ΞΉΞΊΟ ΟƒΟ‡Ξ®ΞΌΞ±) Ξ­Ξ΄ΞµΞΉΞΎΞµ ΟΟ„ΞΉ Ξ· Ο€ΟΞΏΞ·Ξ³ΞΏΟΞΌΞµΞ½Ξ· Ο…Ξ»ΞΏΟ€ΞΏΞ―Ξ·ΟƒΞ· ΞµΞ―Ο‡Ξµ ΞΞΞΞ texture
// (glints/whitecaps) Ο‡Ο‰ΟΞ―Ο‚ ΞΊΞ±ΞΈΟΞ»ΞΏΟ… Ο€ΟΞ±Ξ³ΞΌΞ±Ο„ΞΉΞΊΟ Ξ£Ξ§Ξ—ΞΞ‘ ΞΊΟΞΌΞ±Ο„ΞΏΟ‚. Ξ£Ξµ Ο€ΟΞ±Ξ³ΞΌΞ±Ο„ΞΉΞΊΞ® Ξ±Ξ½ΞΏΞΉΟ‡Ο„Ξ® ΞΈΞ¬Ξ»Ξ±ΟƒΟƒΞ± Ξ±Ο€Ο
// Ξ±Ο€ΟΟƒΟ„Ξ±ΟƒΞ· Ο„Ξ± ΞΌΞ±ΞΊΟΞΉΞ½Ξ¬ ΞΊΟΞΌΞ±Ο„Ξ± "ΟƒΟ…ΞΌΟ€ΞΉΞ­Ξ¶ΞΏΞ½Ο„Ξ±ΞΉ" Ξ±Ο€Ο Ο„Ξ·Ξ½ Ο€ΟΞΏΞΏΟ€Ο„ΞΉΞΊΞ® ΟƒΞµ ΞΏΟΞ±Ο„Ξ­Ο‚, ΞµΟ€Ξ±Ξ½Ξ±Ξ»Ξ±ΞΌΞ²Ξ±Ξ½ΟΞΌΞµΞ½ΞµΟ‚
// Ο†Ο‰Ο„ΞµΞΉΞ½Ξ­Ο‚/ΟƒΞΊΞΏΟ„ΞµΞΉΞ½Ξ­Ο‚ Ξ»Ο‰ΟΞ―Ξ΄ΞµΟ‚ ΞΊΞΏΞ½Ο„Ξ¬ ΟƒΟ„ΞΏΞ½ ΞΏΟΞ―Ξ¶ΞΏΞ½Ο„Ξ± (Ο„ΞΏ ΞΊΞΏΞ½Ο„ΞΉΞ½Ο Ξ½ΞµΟΟ Ο€Ξ±ΟΞ±ΞΌΞ­Ξ½ΞµΞΉ ΟƒΟ‰ΟƒΟ„Ξ¬ texture-based
// β€” Ξ±Ο…Ο„Ο ΞΊΞ±Ξ»ΟΟ€Ο„ΞµΟ„Ξ±ΞΉ Ξ®Ξ΄Ξ· Ξ±Ο€Ο Ο„Ξ± glints/whitecaps Ο€ΞΉΞΏ ΞΊΞ¬Ο„Ο‰, Ξ΄ΞµΞ½ Ξ±Ξ³Ξ³Ξ―Ξ¶ΞµΟ„Ξ±ΞΉ).
function drawHorizonSwell(ctx2, sky, graded) {
    if (S.snowCover > 0.5) return;
    const hz = HORIZON, wh = H - hz;
    const bands = 3 + Math.round(clamp(BFN / 2.5, 0, 1) * 3);   // 3..6 Ξ»Ο‰ΟΞ―Ξ΄ΞµΟ‚ Ξ±Ξ½Ξ¬Ξ»ΞΏΞ³Ξ± Ο„ΞΏΞ½ Ξ¬Ξ½ΞµΞΌΞΏ
    const bandH = Math.min(26, wh * 0.11) * K;
    const amp = (1.2 + BFN * 0.9) * K;
    const speed = 0.5 + BFN * 0.1;
    const crest = mix(graded.hor, [255, 255, 255], 0.35);
    const trough = mix(scaleCol(graded.top, 0.45), [2, 5, 11], 0.5);
    ctx2.save();
    ctx2.beginPath(); ctx2.rect(0, hz, W, Math.min(wh, bandH * bands * 1.6)); ctx2.clip();
    for (let i = 0; i < bands; i++) {
        const k = i / Math.max(1, bands - 1);     // 0 = Ο€ΞΉΞΏ ΞΊΞΏΞ½Ο„Ξ¬ ΟƒΟ„ΞΏΞ½ ΞΏΟΞ―Ξ¶ΞΏΞ½Ο„Ξ±, 1 = Ο€ΞΉΞΏ ΞΊΞΏΞ½Ο„Ξ¬ ΟƒΟ„ΞΏΞ½ ΞΈΞµΞ±Ο„Ξ®
        const y0 = hz + i * bandH * (0.78 + k * 0.5);
        const ph = i * 1.7;
        const crestLine = [];
        ctx2.beginPath();
        ctx2.moveTo(0, y0 + bandH);
        for (let x = 0; x <= W; x += 14) {
            const w1 = Math.sin(x * 0.012 + animTime * speed + ph) * amp;
            const w2 = Math.sin(x * 0.027 - animTime * speed * 0.6 + ph * 1.3) * amp * 0.5;
            const cy = y0 + w1 + w2;
            ctx2.lineTo(x, cy);
            crestLine.push([x, cy]);
        }
        ctx2.lineTo(W, y0 + bandH); ctx2.closePath();
        const g = ctx2.createLinearGradient(0, y0 - bandH * 0.5, 0, y0 + bandH * 0.5);
        g.addColorStop(0, rgba(crest, 0.5 * (1 - k * 0.3)));
        g.addColorStop(0.4, rgba(mix(crest, trough, 0.5), 0.3 * (1 - k * 0.25)));
        g.addColorStop(1, rgba(trough, 0));
        ctx2.fillStyle = g;
        ctx2.fill();
        // Ξ±Ο‡Ξ½Ξ® Ο†Ο‰Ο„ΞµΞΉΞ½Ξ® "ΞΊΞΏΟΟ…Ο†ΞΏΞ³ΟΞ±ΞΌΞΌΞ®" Ο€Ξ¬Ξ½Ο‰ ΟƒΟ„Ξ·Ξ½ Ξ―Ξ΄ΞΉΞ± ΞΊΟ…ΞΌΞ±Ο„ΞΉΟƒΟ„Ξ® Ξ³ΟΞ±ΞΌΞΌΞ® β€” Ο„ΞΏ ΞΊΞ»Ξ±ΟƒΞΉΞΊΟ catch-light effect
        ctx2.beginPath();
        crestLine.forEach((p, pi) => pi ? ctx2.lineTo(p[0], p[1]) : ctx2.moveTo(p[0], p[1]));
        ctx2.strokeStyle = rgba(crest, 0.4 * (1 - k * 0.3));
        ctx2.lineWidth = Math.max(1, 1.4 * K);
        ctx2.stroke();
    }
    ctx2.restore();
}

function drawWater(ctx2, sky, graded) {
    const hz = HORIZON, wh = H - hz;
    const amb = clamp(sky.amb, 0, 1);
    // deep water colour
    const deep = mix(scaleCol(graded.top, 0.5), [3, 7, 15], 0.4);
    const shore = mix(graded.hor, deep, 0.5);
    const wg = ctx2.createLinearGradient(0, hz, 0, H);
    wg.addColorStop(0, rgba(shore, 1)); wg.addColorStop(0.45, rgba(mix(shore, deep, 0.6), 1)); wg.addColorStop(1, rgba(deep, 1));
    ctx2.fillStyle = wg; ctx2.fillRect(0, hz, W, wh);
    drawHorizonSwell(ctx2, sky, graded);

    // sea state follows the wind (Beaufort): 0-2 mirror-like, 3-4 gentle ripples, 5-6 choppy with whitecaps, 7+ rough
    const bfk = clamp(BFN / 10, 0, 1);
    const calm = 1 - clamp(bfk * 0.85 + S.rain * 0.25, 0, 0.92);
    const refl = (0.7 - 0.22 * S.cloud) * (1 - S.snowCover) * (1 - 0.6 * bfk);
    if (refl > 0.03) {
        // 1) the sky is mirrored into a SMALL canvas (1/3 size): the down-scaling gives a naturally soft reflection
        const rw = Math.max(2, Math.round(W / 3)), rh = Math.max(2, Math.round(wh / 3));
        if (!reflCv || reflCv.width !== rw || reflCv.height !== rh) { reflCv = makeCanvas(rw, rh); reflCtx = reflCv.getContext('2d'); }
        const srcH = Math.min(hz, wh * 1.25);
        reflCtx.globalCompositeOperation = 'source-over';
        reflCtx.clearRect(0, 0, rw, rh);
        reflCtx.save(); reflCtx.translate(0, rh); reflCtx.scale(1, -1);
        reflCtx.drawImage(skyCv, 0, hz - srcH, W, srcH, 0, 0, rw, rh);
        reflCtx.restore();
        // 2) erase the mirrored sun/moon DISC (a perfect mirrored disc is what made the old reflection look cartoonish) β€” the
        //    glitter path below replaces it
        if (celest && celest.power > 0.02) {
            const cx = celest.x / 3, cy = ((hz - celest.y) / 1.25) / 3, rr = Math.max(8, celest.r * 3.4 / 3);
            reflCtx.globalCompositeOperation = 'destination-out';
            const eg = reflCtx.createRadialGradient(cx, cy, 0, cx, cy, rr);
            eg.addColorStop(0, 'rgba(0,0,0,0.95)'); eg.addColorStop(0.6, 'rgba(0,0,0,0.6)'); eg.addColorStop(1, 'rgba(0,0,0,0)');
            reflCtx.fillStyle = eg; reflCtx.beginPath(); reflCtx.arc(cx, cy, rr, 0, TAU); reflCtx.fill();
            reflCtx.globalCompositeOperation = 'source-over';
        }
        // 3) paint it into the water in thin bands with smooth, depth-dependent wobble (ripples) and a fade with distance
        ctx2.save();
        ctx2.beginPath(); ctx2.rect(0, hz, W, wh); ctx2.clip();
        ctx2.imageSmoothingEnabled = true; ctx2.imageSmoothingQuality = 'high';
        const band = Math.max(3, Math.round(H / 220));
        const amp = (0.45 + BFN * 0.5 + S.rain * 1.4) * K;
        const fw = 0.8 + BFN * 0.16;                      // ripple speed grows with the wind
        for (let y = 0; y < wh; y += band) {
            const k = y / wh;
            const dx = (Math.sin(animTime * 0.9 * fw + y * (0.05 + bfk * 0.05)) * amp + Math.sin(animTime * 1.9 * fw + y * 0.13) * amp * 0.45) * (0.35 + k);
            ctx2.globalAlpha = refl * (1 - k * 0.78);
            // sample slightly more than one source row (and centre it) so neighbouring bands blend: no blocky steps on hill edges
            const sy = Math.max(0, y / 3 - 0.5), sh = Math.min(rh - sy, band / 3 + 1);
            ctx2.drawImage(reflCv, 0, sy, rw, sh, dx - amp * 2, hz + y, W + amp * 4, band);
        }
        ctx2.restore();
    }

    // snow-covered ground replaces the water
    if (S.snowCover > 0.02) {
        const sg = ctx2.createLinearGradient(0, hz, 0, H);
        const base = scaleCol([214, 224, 240], 0.28 + 0.72 * amb);
        sg.addColorStop(0, rgba(mix(base, graded.hor, 0.3), S.snowCover));
        sg.addColorStop(1, rgba(scaleCol(base, 0.85), S.snowCover));
        ctx2.fillStyle = sg; ctx2.fillRect(0, hz, W, wh);
    }

    // long, faint wave glints drifting with the wind (surface texture)
    if (S.snowCover < 0.5) {
        ctx2.save();
        ctx2.globalCompositeOperation = 'lighter';
        const gcol = mix(graded.hor, [235, 240, 250], 0.55);
        const activeGlints = Math.round(glints.length * clamp(0.3 + BFN / 7, 0, 1));
        for (let gi = 0; gi < activeGlints; gi++) {
            const g = glints[gi];
            const t = Math.pow(g.u, 1.5);
            const y = hz + 6 + t * wh * 0.96;
            const len = (g.len * (0.35 + t * 1.6)) * W;
            const x = (((g.x * W + animTime * g.sp * (6 + t * 22) * (0.5 + S.wind * 0.022) * (S.dir < 0 ? -1 : 1)) % (W + len)) + W + len) % (W + len) - len;
            ctx2.fillStyle = rgba(gcol, 0.05 * g.a * (0.4 + t) * (0.4 + 0.6 * amb) * (0.65 + BFN * 0.12));
            ctx2.fillRect(x, y, len, Math.max(1, (1 + t * 2.2) * K));
        }
        ctx2.restore();
    }

    // whitecaps: short foamy dashes on a choppy sea (Beaufort 4+), drifting with the wind
    const capK = clamp((BFN - 3.5) / 4, 0, 1);
    if (capK > 0.02 && S.snowCover < 0.5) {
        ctx2.save();
        ctx2.globalCompositeOperation = 'lighter';
        const n = Math.round(caps.length * capK);
        const ccol = mix([200, 210, 225], [255, 255, 255], amb);
        for (let ci = 0; ci < n; ci++) {
            const c = caps[ci];
            const t = Math.pow(c.u, 1.25);
            const y = hz + 10 + t * wh * 0.94;
            const len = (6 + 30 * t) * c.len * K;
            const x = (((c.x * W + animTime * (14 + BFN * 6) * (0.4 + t) * (S.dir < 0 ? -1 : 1)) % (W + len)) + W + len) % (W + len) - len;
            const tw = 0.4 + 0.6 * Math.abs(Math.sin(animTime * c.sp + c.ph));
            ctx2.fillStyle = rgba(ccol, 0.32 * capK * tw * (0.3 + 0.7 * t) * (0.3 + 0.7 * amb));
            ctx2.fillRect(x, y, len, Math.max(1, (0.8 + 1.8 * t) * K));
        }
        ctx2.restore();
    }

    // shimmering sun/moon light path (glitter): narrow near the horizon, wider and brighter-flecked toward the viewer
    if (celest && celest.power > 0.02 && S.snowCover < 0.6) {
        ctx2.save();
        ctx2.beginPath(); ctx2.rect(0, hz, W, wh); ctx2.clip();
        ctx2.globalCompositeOperation = 'lighter';
        const p = celest.power * (0.55 + 0.45 * calm);
        // soft column of light
        const colH = wh * 0.95, colW = W * 0.07;
        ctx2.save();
        ctx2.translate(celest.x, hz + colH * 0.42); ctx2.scale(colW / colH, 1);
        const cg = ctx2.createRadialGradient(0, 0, 0, 0, 0, colH * 0.55);
        cg.addColorStop(0, rgba(celest.col, 0.20 * p)); cg.addColorStop(0.5, rgba(celest.col, 0.07 * p)); cg.addColorStop(1, rgba(celest.col, 0));
        ctx2.fillStyle = cg; ctx2.beginPath(); ctx2.arc(0, 0, colH * 0.55, 0, TAU); ctx2.fill();
        ctx2.restore();
        // glitter flecks
        for (const f of glitter) {
            const t = Math.pow(f.u, 1.35);
            const y = hz + 4 + t * wh * 0.97;
            const spread = (0.012 + 0.11 * t) * W * (1 + BFN * 0.12);
            const x = celest.x + f.side * 2 * spread + Math.sin(animTime * 0.7 + f.ph) * spread * 0.18;
            const len = (4 + 46 * t) * f.len * K * (1 - bfk * 0.45);
            const tw = 0.5 + 0.5 * Math.sin(animTime * f.sp * (0.8 + BFN * 0.15) + f.ph);
            const a = p * (1 - t * 0.5) * (0.12 + 0.88 * tw * tw) * 0.75 * (1 - Math.abs(f.side) * 0.9);
            if (a < 0.015) continue;
            ctx2.fillStyle = rgba(celest.col, a);
            ctx2.fillRect(x - len / 2, y, len, Math.max(1, (0.9 + 2.4 * t) * K * f.hh));
        }
        ctx2.restore();
    }

    // soft haze right at the horizon
    const hg = ctx2.createLinearGradient(0, hz - 6, 0, hz + wh * 0.14);
    hg.addColorStop(0, rgba(graded.hor, 0)); hg.addColorStop(0.3, rgba(graded.hor, 0.4)); hg.addColorStop(1, rgba(graded.hor, 0));
    ctx2.fillStyle = hg; ctx2.fillRect(0, hz - 6, W, wh * 0.14 + 6);
}

// Fresnel: a flat water surface reflects the sky strongly at grazing angles (near the horizon) and shows its depth when looked at steeply (near the viewer)
function drawFresnel(ctx2, graded) {
    if (S.snowCover > 0.6) return;
    const a = 0.3 * (1 - S.fog * 0.5) * (1 - 0.4 * S.rain);
    const fg = ctx2.createLinearGradient(0, HORIZON, 0, H);
    fg.addColorStop(0, rgba(graded.hor, a)); fg.addColorStop(0.18, rgba(graded.hor, a * 0.45)); fg.addColorStop(0.6, rgba(graded.hor, a * 0.1)); fg.addColorStop(1, rgba(graded.hor, 0));
    ctx2.fillStyle = fg; ctx2.fillRect(0, HORIZON, W, H - HORIZON);
}

// Foam: short white crests that flash on the waves when the wind is up or it rains hard; a fixed set of crests (no per-frame allocation), more visible and bigger toward the viewer
let foam = null;
function drawFoam(ctx2, amb) {
    const power = clamp((S.wind - 12) / 40 + S.rain * 0.35 + S.storm * 0.4, 0, 1);
    if (power < 0.05 || S.snowCover > 0.5 || tierLevel() < 1) return;
    if (!foam) { const r = mulberry32(31); foam = Array.from({ length: 90 }, () => ({ x: r(), z: Math.pow(r(), 1.6), w: 0.5 + r(), ph: r() * TAU, sp: 0.4 + r() * 1.2 })); }
    const col = mix([150, 165, 190], [250, 252, 255], clamp(amb + 0.15, 0, 1));
    const span = H - HORIZON;
    for (const f of foam) {
        const flash = Math.sin(animTime * f.sp + f.ph); if (flash < 0.35) continue;
        const a = (flash - 0.35) / 0.65 * power * (0.2 + 0.5 * f.z);
        const y = HORIZON + span * (0.04 + 0.9 * f.z), w = (8 + 48 * f.z) * f.w * K, x = ((f.x + animTime * 0.002 * S.dir * (0.5 + f.z)) % 1 + 1) % 1 * W;
        ctx2.fillStyle = rgba(col, a); ctx2.beginPath(); ctx2.ellipse(x, y, w, Math.max(0.7, w * 0.05), 0, 0, TAU); ctx2.fill();
    }
}

function drawRipples(ctx2, dt, amb) {
    if (S.snowCover > 0.6) { ripples.length = 0; return; }
    const col = mix([120, 140, 170], [225, 235, 250], clamp(amb + 0.15, 0, 1));
    ctx2.lineWidth = 1;
    for (let i = ripples.length - 1; i >= 0; i--) {
        const r = ripples[i];
        r.life += dt;
        const p = r.life / r.dur;
        if (p >= 1) { ripples.splice(i, 1); continue; }
        const rx = r.maxR * (0.15 + p), ry = rx * (0.16 + 0.1 * r.k);
        ctx2.strokeStyle = rgba(col, (1 - p) * (0.5 + 0.3 * r.k));
        ctx2.beginPath(); ctx2.ellipse(r.x, r.y, rx, ry, 0, 0, TAU); ctx2.stroke();
        if (r.k > 0.5 && p < 0.7) { ctx2.beginPath(); ctx2.ellipse(r.x, r.y, rx * 0.55, ry * 0.55, 0, 0, TAU); ctx2.stroke(); }
    }
}

function drawRain(ctx2, dt, amb, density) {
    const intensity = S.rain;
    if (intensity < 0.02) return;
    const col = mix([135, 152, 180], [215, 230, 250], clamp(amb + 0.1, 0, 1));
    const layers = [
        { arr: rain.far, w: 0.8, a: 0.24, len: 0.045, rip: 0.22, k: 0.15 },
        { arr: rain.mid, w: 1.15, a: 0.34, len: 0.05, rip: 0.4, k: 0.5 },
        { arr: rain.near, w: 1.7, a: 0.42, len: 0.055, rip: 0.7, k: 1 },
    ];
    ctx2.lineCap = 'round';
    const wind = S.windX;
    for (const L of layers) {
        const n = Math.round(L.arr.length * Math.min(1, intensity * 1.15));
        const aL = L.a * (0.55 + 0.45 * intensity), wL = L.w * Math.sqrt(K);
        ctx2.beginPath();
        for (let i = 0; i < n; i++) {
            const d = L.arr[i];
            d.y += d.v * K * dt; d.x += wind * K * (d.v / 1200) * dt;
            if (d.y >= d.g) {
                if (Math.random() < L.rip * (0.5 + 0.5 * intensity) && ripples.length < 260 && S.snowCover < 0.5)
                    ripples.push({ x: d.x, y: d.g, life: 0, dur: 0.7 + Math.random() * 0.6, maxR: (7 + L.k * 24 + Math.random() * 8) * K, k: L.k });
                if (L.k > 0.45 && density >= 0.6 && splashes.length < 320 && Math.random() < 0.6 && S.snowCover < 0.5) {
                    for (let s = 0; s < 3; s++) splashes.push({ x: d.x, y: d.g, vx: (Math.random() - 0.5) * 90, vy: -(60 + Math.random() * 120), life: 0, dur: 0.35 + Math.random() * 0.2 });
                }
                d.y = -Math.random() * 120 - d.v * 0.05;
                d.x = Math.random() * (W + 240) - 120;
                d.g = d.g;
            }
            if (d.x > W + 120) d.x -= W + 240; else if (d.x < -120) d.x += W + 240;
            const k = L.len;
            ctx2.moveTo(d.x, d.y);
            ctx2.lineTo(d.x - wind * K * (d.v / 1200) * k, d.y - d.v * K * k);
        }
        // motion blur: the same streaks once wide and faint, once thin and sharp (one path, two strokes)
        ctx2.strokeStyle = rgba(col, aL * 0.4); ctx2.lineWidth = wL * 3.2; ctx2.stroke();
        ctx2.strokeStyle = rgba(col, aL); ctx2.lineWidth = wL; ctx2.stroke();
    }
    // wet surfaces: a soft specular sheen on the ground / water that grows with the rain
    if (intensity > 0.2) {
        const wg = ctx2.createLinearGradient(0, HORIZON, 0, H);
        wg.addColorStop(0, rgba(col, 0.1 * intensity)); wg.addColorStop(0.5, rgba(col, 0.03 * intensity)); wg.addColorStop(1, rgba(col, 0));
        ctx2.fillStyle = wg; ctx2.fillRect(0, HORIZON, W, H - HORIZON);
    }
    // splash droplets
    if (splashes.length) {
        ctx2.fillStyle = rgba(col, 0.55);
        for (let i = splashes.length - 1; i >= 0; i--) {
            const s = splashes[i];
            s.life += dt;
            if (s.life >= s.dur) { splashes.splice(i, 1); continue; }
            s.vy += 650 * dt; s.x += s.vx * dt; s.y += s.vy * dt;
            ctx2.globalAlpha = 1 - s.life / s.dur;
            ctx2.fillRect(s.x, s.y, 1.6, 1.6);
        }
        ctx2.globalAlpha = 1;
    }
}

const flakes = { arr: [], n: 0 };
function ensureFlakes(density) {
    const want = Math.round(520 * (W * H) / (1920 * 1080) * density);
    if (flakes.n === want) return;
    flakes.n = want;
    flakes.arr = Array.from({ length: want }, () => {
        const z = Math.random();                                // 0 far .. 1 near
        return { x: Math.random() * W, y: Math.random() * H, z, v: 28 + z * 80, r: 1.2 + z * 4.2, ph: Math.random() * TAU, sw: 12 + z * 26 };
    });
}
function drawSnow(ctx2, dt, amb) {
    if (S.snow < 0.02) return;
    const n = Math.round(flakes.arr.length * Math.min(1, S.snow * 1.1));
    const bright = 0.45 + 0.55 * clamp(amb + 0.25, 0, 1);
    for (let i = 0; i < n; i++) {
        const f = flakes.arr[i];
        f.y += f.v * K * dt;
        f.x += (S.windX * 0.35 * (0.4 + f.z) + Math.sin(animTime * 0.9 + f.ph) * f.sw) * dt;
        if (f.y > H + 10) { f.y = -10; f.x = Math.random() * W; }
        if (f.x > W + 10) f.x -= W + 20; else if (f.x < -10) f.x += W + 20;
        ctx2.globalAlpha = (0.35 + 0.55 * f.z) * bright;
        const s = f.r * 2 * K;
        ctx2.drawImage(flakeSprite, f.x - s, f.y - s, s * 2, s * 2);
    }
    ctx2.globalAlpha = 1;
}

function drawFog(ctx2, dt, graded, amb) {
    if (S.fog < 0.03) return;
    const col = mix(hazeColor(graded.hor), [235, 238, 244], 0.25);
    const base = mix([30, 36, 50], col, clamp(amb * 1.1 + 0.15, 0, 1));
    // veil over the whole scene
    ctx2.fillStyle = rgba(base, S.fog * 0.34);
    ctx2.fillRect(0, 0, W, H);
    // drifting banks (each sprite tiles seamlessly, so a bank is a full-width band without visible edges)
    ctx2.save();
    for (let i = 0; i < 6; i++) {
        const sp = fogSprites[i % 3];
        const bw = W * (0.8 + 0.15 * (i % 3)), bh = bw * 0.25;
        const spd = 6 + i * 4 + S.windX * 0.05;
        const off = ((animTime * spd + i * 413) % bw + bw) % bw;
        const y = HORIZON - bh * 0.55 + (i - 2.5) * H * 0.045;
        ctx2.globalAlpha = clamp(S.fog * (0.5 + 0.3 * Math.sin(i + animTime * 0.1)), 0, 1) * (0.5 + 0.5 * clamp(amb + 0.2, 0, 1));
        for (let x = off - bw - 1; x < W; x += bw) ctx2.drawImage(sp, x, y, bw + 2, bh);
    }
    ctx2.restore();
}

// ---- rain on the "window glass"
// Ξ”Ξ™ΞΞ΅ΞΞ©Ξ£Ξ— "Ο„ΞΏ ΞµΟ†Ξ­ Ο„Ο‰Ξ½ ΟƒΟ„Ξ±Ξ³ΟΞ½Ο‰Ξ½ ΟƒΟ„Ξ·Ξ½ ΞΏΞΈΟΞ½Ξ· Ξ΄ΞµΞ½ Ξ»ΞµΞΉΟ„ΞΏΟ…ΟΞ³ΞµΞ―" (2ΞΏ Ξ³ΟΟΞΏ, ΞΌΞµΟ„Ξ¬ Ο„ΞΏ reorder-fix
// Ο€Ξ±ΟΞ±Ο€Ξ¬Ξ½Ο‰): ΞΏΞΉ ΟƒΟ„Ξ±Ξ³ΟΞ½ΞµΟ‚ ΞΞΞ¤Ξ©Ξ£ Ξ¶Ο‰Ξ³ΟΞ±Ο†Ξ―Ξ¶ΞΏΞ½Ο„Ξ±Ξ½ (ΞµΟ€ΞΉΞ²ΞµΞ²Ξ±ΞΉΟΞΈΞ·ΞΊΞµ live ΟƒΞµ headless Chromium, Ο€Ξ»Ξ¬ΞΉ-ΟƒΞµ-
// Ο€Ξ»Ξ¬ΞΉ frame diff), Ξ±Ξ»Ξ»Ξ¬ ΞΌΞµ Ο„ΟΟƒΞΏ ΞΌΞΉΞΊΟΞ® Ξ±ΞΊΟ„Ξ―Ξ½Ξ± (1.6-6px) ΞΊΞ±ΞΉ Ο„ΟΟƒΞΏ Ο‡Ξ±ΞΌΞ·Ξ»Ο alpha (0.05-0.22) Ο€ΞΏΟ… ΟƒΞµ
// Ο€ΟΞ±Ξ³ΞΌΞ±Ο„ΞΉΞΊΞ® Ξ±Ο€ΟΟƒΟ„Ξ±ΟƒΞ· ΞΈΞ­Ξ±ΟƒΞ·Ο‚ desktop Ξ®Ο„Ξ±Ξ½ ΞΏΟ…ΟƒΞΉΞ±ΟƒΟ„ΞΉΞΊΞ¬ Ξ±ΟΟΞ±Ο„ΞµΟ‚ β€” "Ξ΄ΞµΞ½ Ξ»ΞµΞΉΟ„ΞΏΟ…ΟΞ³ΞµΞ―" Ξ±Ο€Ο Ο„Ξ·Ξ½ ΞΏΟ€Ο„ΞΉΞΊΞ®
// Ξ³Ο‰Ξ½Ξ―Ξ± Ο„ΞΏΟ… Ο‡ΟΞ®ΟƒΟ„Ξ·, Ξ±ΞΊΟΞΌΞ± ΞΊΞΉ Ξ±Ξ½ Ο„ΞµΟ‡Ξ½ΞΉΞΊΞ¬ Ξ¶Ο‰Ξ³ΟΞ±Ο†ΞΉΞ¶ΟΟ„Ξ±Ξ½ ΞΊΞ¬ΞΈΞµ frame. ΞΞµΞ³Ξ±Ξ»ΟΟ„ΞµΟΞµΟ‚ ΟƒΟ„Ξ±Ξ³ΟΞ½ΞµΟ‚ + ΟƒΞ±Ο†ΟΟ‚
// ΞΌΞµΞ³Ξ±Ξ»ΟΟ„ΞµΟΞΏ alpha Ο€Ξ±Ξ½Ο„ΞΏΟ, Ο€ΞΉΞΏ ΞΊΞΏΞ½Ο„Ξ¬ ΟƒΟ„ΞΏ Ο€ΟΟ‚ Ο†Ξ±Ξ―Ξ½ΞµΟ„Ξ±ΞΉ Ο„ΞΏ Ξ±Ξ½Ο„Ξ―ΟƒΟ„ΞΏΞΉΟ‡ΞΏ ΞµΟ†Ξ­ Ο„ΞΏΟ… Lively Wallpaper.
function updateGlass(dt) {
    const want = cfg.glass && S.rain > 0.12 ? Math.round((34 + 150 * S.rain) * Math.min(1.4, W / 1920 + 0.2)) : 0;
    while (glass.length < want && Math.random() < 0.6) {
        glass.push({ x: Math.random() * W, y: Math.random() * H, r: (3 + Math.random() * 7) * K, vy: 0, trail: [], age: 0 });
    }
    for (let i = glass.length - 1; i >= 0; i--) {
        const d = glass[i];
        d.age += dt;
        if (want === 0 || glass.length > want + 6) { d.r -= dt * 2; if (d.r < 0.5) { glass.splice(i, 1); continue; } }
        d.r = Math.min(16, d.r + dt * 0.06 * S.rain * (d.vy > 1 ? 0 : 1));
        if (d.vy === 0 && d.r > 8 + Math.random() * 4) d.vy = 12 + Math.random() * 14;
        if (d.vy > 0) {
            d.vy = Math.min(220, d.vy * (1 + dt * 1.3));
            d.y += d.vy * dt;
            d.x += Math.sin(d.y * 0.04 + i) * dt * 4;
            d.trail.push([d.x, d.y]); if (d.trail.length > 28) d.trail.shift();
            d.r -= dt * 0.55;
            if (d.r < 2 || d.y > H + 20) { d.vy = 0; d.y = Math.random() * H * 0.9; d.x = Math.random() * W; d.r = (2.5 + Math.random() * 3) * K; d.trail.length = 0; }
        }
    }
}

function drawGlass(ctx2, src) {
    if (!glass.length) return;
    ctx2.save();
    for (const d of glass) {
        if (d.trail.length > 1) {
            ctx2.strokeStyle = 'rgba(255,255,255,0.16)'; ctx2.lineWidth = Math.max(1.2, d.r * 0.55); ctx2.lineCap = 'round';
            ctx2.beginPath(); d.trail.forEach((q, i) => i ? ctx2.lineTo(q[0], q[1]) : ctx2.moveTo(q[0], q[1])); ctx2.stroke();
        }
        const r = d.r, x = d.x, y = d.y;
        // refraction: the drop shows a magnified, upside-down piece of the scene behind it
        if (src && r > 2.2) {
            ctx2.save();
            ctx2.beginPath(); ctx2.ellipse(x, y, r, r * 1.18, 0, 0, TAU); ctx2.clip();
            const m = 2.4, sw = r * 2 * m, sh = r * 2.36 * m;
            ctx2.translate(x, y); ctx2.rotate(Math.PI);
            ctx2.drawImage(src, clamp(x - sw / 2, 0, W - sw), clamp(y - sh / 2, 0, H - sh), sw, sh, -r, -r * 1.18, r * 2, r * 2.36);
            ctx2.restore();
        }
        // rim + specular
        ctx2.beginPath(); ctx2.ellipse(x, y, r, r * 1.18, 0, 0, TAU);
        const rg = ctx2.createRadialGradient(x - r * 0.25, y - r * 0.3, r * 0.1, x, y, r * 1.2);
        rg.addColorStop(0, 'rgba(255,255,255,0.16)'); rg.addColorStop(0.75, 'rgba(255,255,255,0.24)'); rg.addColorStop(1, 'rgba(10,16,30,0.55)');
        ctx2.fillStyle = rg; ctx2.fill();
        ctx2.strokeStyle = 'rgba(255,255,255,0.45)'; ctx2.lineWidth = 1.1; ctx2.stroke();
        ctx2.fillStyle = 'rgba(255,255,255,0.9)';
        ctx2.beginPath(); ctx2.arc(x - r * 0.35, y - r * 0.45, Math.max(1, r * 0.28), 0, TAU); ctx2.fill();
    }
    ctx2.restore();
}

function drawVignette(ctx2) {
    const g = ctx2.createRadialGradient(W / 2, H / 2, Math.min(W, H) * 0.45, W / 2, H / 2, Math.hypot(W, H) * 0.62);
    g.addColorStop(0, 'rgba(0,0,0,0)'); g.addColorStop(1, 'rgba(0,0,0,0.38)');
    ctx2.fillStyle = g; ctx2.fillRect(0, 0, W, H);
}

// ---------------------------------------------------------------- main draw
function draw(target, dt, profile) {
    if (!skyCv) return;
    const density = densityFor(profile);
    if (density !== lastDensity) { buildDrops(density); ensureFlakes(density); lastDensity = density; }
    animTime += dt;

    // ease the scene toward the weather targets (so a weather change is a slow cross-fade, never a pop)
    const rate = 0.55;
    S.cloud = approach(S.cloud, TGT.cloud, dt, rate); S.rain = approach(S.rain, TGT.rain, dt, rate);
    S.snow = approach(S.snow, TGT.snow, dt, rate); S.fog = approach(S.fog, TGT.fog, dt, rate * 0.7);
    S.storm = approach(S.storm, TGT.storm, dt, rate); S.windX = approach(S.windX, TGT.windX, dt, 0.4);
    S.wind = approach(S.wind, TGT.wind, dt, 0.35); S.dir = approach(S.dir, TGT.dir, dt, 0.3);
    BFN = beaufort(S.wind);
    S.snowCover = approach(S.snowCover, TGT.snow > 0.3 ? Math.min(1, TGT.snow) : 0, dt, 0.06);

    const t = localMinutes();
    const { sr, ss } = sunTimes(offsetMinutes());
    const sky = skyAt(t, sr, ss);

    drawLightning(dt, sky);

    // shooting stars
    if (sky.amb < 0.2 && S.cloud < 0.4 && S.rain < 0.1) {
        nextShootIn -= dt;
        if (nextShootIn <= 0 && !shoot) {
            nextShootIn = 14 + Math.random() * 26;
            shoot = { x: W * (0.2 + Math.random() * 0.6), y: HORIZON * (0.05 + Math.random() * 0.3), dx: (Math.random() < 0.5 ? -1 : 1) * W * 0.22, dy: HORIZON * 0.12, life: 0, dur: 0.9 };
        }
    }
    if (shoot) { shoot.life += dt; if (shoot.life >= shoot.dur) shoot = null; }

    // ---- sky canvas
    const graded = drawSkyLayer(sky, t, sr, ss);
    drawStarsLayer(sky, 1 - S.cloud * 0.8 - S.fog * 0.5);
    drawMilkyWay(sky);
    const moon = drawMoonLayer(t, sr, ss, 1);
    moonBright = moon && moon.alpha > 0.05 ? clamp(moon.alpha, 0, 1) * (1 - Math.abs(moonPhase() - 0.5) * 2 > 0.3 ? 1 : 0.4) : 0;
    const sun = drawSunLayer(sky, t, sr, ss);
    drawRainbow(sky, sun);
    // the brighter visible body drives the light path on the water
    celest = null;
    if (sun && sun.vis > 0.05 && sun.e > 0.0) celest = { x: sun.x, y: sun.y, r: sun.r, power: clamp(sun.vis * (0.55 + 0.45 * sky.glow), 0, 1), col: mix([255, 255, 255], [255, 170, 110], clamp(1 - sun.e * 2.2, 0, 1)) };
    else if (moon && moon.alpha > 0.05) celest = { x: moon.x, y: moon.y, r: moon.r, power: clamp(moon.alpha * 0.9, 0, 1), col: [205, 220, 248] };
    drawCloudsLayer(sky, dt);
    if (flash > 0) { skyCtx.fillStyle = `rgba(200,215,255,${flash * 0.55})`; skyCtx.fillRect(0, 0, W, HORIZON); }
    drawHillsLayer(sky, graded);

    // ---- composite
    // Ξ”Ξ™ΞΞ΅ΞΞ©Ξ£Ξ— "Ο„ΞΏ ΞµΟ†Ξ­ Ο„Ο‰Ξ½ ΟƒΟ„Ξ±Ξ³ΟΞ½Ο‰Ξ½ ΟƒΟ„Ξ·Ξ½ ΞΏΞΈΟΞ½Ξ· Ξ΄ΞµΞ½ Ξ»ΞµΞΉΟ„ΞΏΟ…ΟΞ³ΞµΞ―": Ο„ΞΏ updateGlass (Ο€ΞΏΟ… Ξ“Ξ•ΞΞ™Ξ–Ξ•Ξ™ Ο„ΞΏ
    // glass[] array) ΞΊΞ±Ξ»ΞΏΟΞ½Ο„Ξ±Ξ½ ΞΞ•Ξ¤Ξ‘ Ο„ΞΏΞ½ Ο…Ο€ΞΏΞ»ΞΏΞ³ΞΉΟƒΞΌΟ Ο„ΞΏΟ… useGlass β€” ΟƒΟ„ΞΏ Ο€ΟΟΟ„ΞΏ frame Ο€ΞΏΟ… Ξ±ΟΟ‡Ξ―Ξ¶ΞµΞΉ Ξ·
    // Ξ²ΟΞΏΟ‡Ξ®, glass.length Ξ®Ο„Ξ±Ξ½ Ξ±ΞΊΟΞΌΞ± 0 Ξ¤Ξ— Ξ£Ξ¤Ξ™Ξ“ΞΞ— Ο„ΞΏΟ… ΞµΞ»Ξ­Ξ³Ο‡ΞΏΟ…, ΞΏΟ€ΟΟ„Ξµ useGlass Ξ­Ξ²Ξ³Ξ±ΞΉΞ½Ξµ false ΞΊΞ±ΞΉ Ο„ΞΏ
    // drawGlass Ο€Ξ±ΟΞ±Ξ»ΞµΞΉΟ€ΟΟ„Ξ±Ξ½ Ξ•ΞΞ¤Ξ•Ξ›Ξ©Ξ£ Ξ³ΞΉΞ± ΞµΞΊΞµΞ―Ξ½ΞΏ Ο„ΞΏ frame (ΞΊΞ±ΞΉ ΞΊΞ¬ΞΈΞµ Ο†ΞΏΟΞ¬ Ο€ΞΏΟ… Ο„ΞΏ glass[] Ξ¬Ξ΄ΞµΞΉΞ±Ξ¶Ξµ
    // Ο€ΟΞΏΟƒΟ‰ΟΞΉΞ½Ξ¬ ΞΊΞΏΞ½Ο„Ξ¬ ΟƒΟ„ΞΏ ΞΊΞ±Ο„ΟΟ†Ξ»ΞΉ Ξ²ΟΞΏΟ‡Ξ®Ο‚ 0.12, Ο„ΞΏ effect Ξ­ΟƒΞ²Ξ·Ξ½Ξµ/Ξ±Ξ½Ξ±Ξ²ΟΟƒΞ²Ξ·Ξ½Ξµ Ξ±Ξ½Ο„Ξ― Ξ½Ξ± Ο€Ξ±ΟΞ±ΞΌΞ­Ξ½ΞµΞΉ
    // ΟƒΟ„Ξ±ΞΈΞµΟΟ). Ξ¤ΟΟΞ± Ο„ΞΏ updateGlass Ο„ΟΞ­Ο‡ΞµΞΉ Ξ Ξ΅Ξ©Ξ¤Ξ‘, ΟΟƒΟ„Ξµ Ο„ΞΏ useGlass Ξ½Ξ± Ξ²Ξ»Ξ­Ο€ΞµΞΉ Ο€Ξ¬Ξ½Ο„Ξ± Ο„Ξ·Ξ½
    // ΞµΞ½Ξ·ΞΌΞµΟΟ‰ΞΌΞ­Ξ½Ξ· ΞΊΞ±Ο„Ξ¬ΟƒΟ„Ξ±ΟƒΞ· Ο„ΞΏΟ… Ξ™Ξ”Ξ™ΞΞ¥ frame.
    updateGlass(dt);
    const useGlass = cfg.glass && glass.length > 0;
    let c2 = target;
    if (useGlass) {
        if (!sceneCv || sceneCv.width !== W || sceneCv.height !== H) { sceneCv = makeCanvas(W, H); sceneCtx = sceneCv.getContext('2d'); }
        c2 = sceneCtx;
    }
    c2.setTransform(1, 0, 0, 1, 0, 0);
    c2.globalAlpha = 1; c2.globalCompositeOperation = 'source-over';
    c2.drawImage(skyCv, 0, 0);
    drawBirds(c2, dt, sky);
    if (onPhoto()) drawScenePhoto(c2, sky, graded);
    else if (onLand()) drawLand(c2, sky, graded);
    else { drawWater(c2, sky, graded); drawFresnel(c2, graded); drawFoam(c2, sky.amb); drawRipples(c2, dt, sky.amb); }
    drawFog(c2, dt, graded, sky.amb);
    drawRain(c2, dt, sky.amb, density);
    drawSnow(c2, dt, sky.amb);
    if (flash > 0) { c2.fillStyle = `rgba(210,222,255,${flash * 0.35})`; c2.fillRect(0, 0, W, H); }
    drawBolt(c2);
    drawVignette(c2);

    if (useGlass) {
        target.setTransform(1, 0, 0, 1, 0, 0);
        target.drawImage(sceneCv, 0, 0);
        drawGlass(target, sceneCv);
    }
}

window.WeatherWallpaper = {
    setConfig, setWeather, resize,
    draw: (ctx2, dt, profile) => draw(ctx2, Math.min(dt, 0.1), profile),
    // info overlay helpers (clock / date / temperature drawn by index.html)
    info: () => {
        const m = localMinutes();
        const d = new Date(Date.now() + (offsetMinutes() + new Date().getTimezoneOffset()) * 60000);   // wall-clock date at the location
        // Ξ–Ξ·Ο„Ξ®ΞΈΞ·ΞΊΞµ ΟΞ·Ο„Ξ¬ "ΞµΞΌΟ†Ξ¬Ξ½ΞΉΟƒΞ· Ξ±Ξ½Ξ­ΞΌΞΏΟ… ΞΌΞ±Ξ¶Ξ― ΞΌΞµ Ο„Ξ·Ξ½ ΟΟΞ±/Ξ·ΞΌΞµΟΞΏΞΌΞ·Ξ½Ξ―Ξ±/ΞΈΞµΟΞΌΞΏΞΊΟΞ±ΟƒΞ―Ξ± ΟƒΟ„Ξ·Ξ½ ΞµΟ€ΞΉΟ†Ξ¬Ξ½ΞµΞΉΞ±
        // ΞµΟΞ³Ξ±ΟƒΞ―Ξ±Ο‚" β€” windKmh/beaufort ΞµΞΊΟ„Ξ―ΞΈΞµΞ½Ο„Ξ±ΞΉ ΞµΞ΄Ο ΟΟƒΟ„Ξµ Ο„ΞΏ index.html Ξ½Ξ± Ο„Ξ± Ξ΄ΞµΞ―ΞΎΞµΞΉ ΟƒΟ„ΞΏ Ξ―Ξ΄ΞΉΞΏ
        // overlay (Ξ²Ξ». updateWeatherInfo). S.wind ΞµΞ―Ξ½Ξ±ΞΉ Ξ®Ξ΄Ξ· ΟƒΞµ km/h (Ξ²Ξ». computeTargets).
        return { h: Math.floor(m / 60) % 24, m: Math.floor(m % 60), date: d, temp: data && data.ok ? data.temp : null, code: cfg.weatherSim && cfg.weatherSim !== 'Auto' ? ({ Clear: 0, PartlyCloudy: 2, Cloudy: 3, Drizzle: 51, Rain: 63, HeavyRain: 65, Thunderstorm: 95, Snow: 73, Fog: 45 })[cfg.weatherSim] : (data && data.ok ? data.code : null), windKmh: Math.round(S.wind), beaufort: BFN };
    },
    // test helpers (headless screenshots)
    _state: () => ({ S, TGT, bf: BFN, t: localMinutes(), sun: sunTimes(offsetMinutes()) }),
    _bolt: () => { nextBoltIn = 0; },
    _warm: (secs, ctx2, profile) => { for (let i = 0; i < secs * 30; i++) draw(ctx2, 1 / 30, profile); },
};
})();

