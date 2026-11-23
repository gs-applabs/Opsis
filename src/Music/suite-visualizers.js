// Canvas visualizers adapted from the MIT-licensed Opsis Media Suite renderer (see OPSIS-MEDIA-SUITE-MIT.txt).
return (() => {
let vctx, canvas, freqData, timeData, palette;
let particles = [], previous = null;
const devicePixelRatio = 1;
function tone(index, opacity = 1) {
  const hex = palette[Math.abs(index) % palette.length] || '#a78bfa';
  const color = Number.parseInt(hex.slice(1), 16);
  return `rgba(${color >> 16},${(color >> 8) & 255},${color & 255},${opacity})`;
}

function bandAvg(from, to) {
  let sum = 0;
  for (let i = from; i < to; i++) sum += freqData[i];
  return sum / ((to - from) * 255);
}

function drawBars() {
  const w = canvas.width, h = canvas.height;
  
  const barCount = 64;
  const step = Math.floor(freqData.length / barCount);
  const barWidth = w / barCount;
  const grad = vctx.createLinearGradient(0, h, 0, 0);
  grad.addColorStop(0, tone(0));
  grad.addColorStop(1, tone(1));
  vctx.fillStyle = grad;
  for (let i = 0; i < barCount; i++) {
    const v = freqData[i * step] / 255;
    const barH = v * h;
    vctx.fillRect(i * barWidth + 1, h - barH, Math.max(0.5, barWidth - 2), barH);
  }
}
function drawWave() {
  const w = canvas.width, h = canvas.height;
  
  vctx.lineWidth = 2 * devicePixelRatio;
  vctx.strokeStyle = tone(2);
  vctx.beginPath();
  const step = w / timeData.length;
  for (let i = 0; i < timeData.length; i++) {
    const v = timeData[i] / 128 - 1;
    const y = h / 2 + v * h * 0.45;
    if (i === 0) vctx.moveTo(0, y); else vctx.lineTo(i * step, y);
  }
  vctx.stroke();
}
function drawCircular() {
  const w = canvas.width, h = canvas.height;
  
  const cx = w / 2, cy = h / 2;
  const baseR = Math.min(w, h) * 0.18;
  const bars = 90;
  vctx.strokeStyle = tone(0);
  vctx.lineWidth = 3 * devicePixelRatio;
  for (let i = 0; i < bars; i++) {
    const v = freqData[Math.floor((i / bars) * freqData.length)] / 255;
    const angle = (i / bars) * Math.PI * 2;
    const r1 = baseR;
    const r2 = baseR + v * (Math.min(w, h) * 0.35);
    vctx.beginPath();
    vctx.moveTo(cx + Math.cos(angle) * r1, cy + Math.sin(angle) * r1);
    vctx.lineTo(cx + Math.cos(angle) * r2, cy + Math.sin(angle) * r2);
    vctx.stroke();
  }
}
function drawMirrorBars() {
  const w = canvas.width, h = canvas.height;
  
  const barCount = 48;
  const step = Math.floor(freqData.length / barCount);
  const barWidth = w / barCount;
  const grad = vctx.createLinearGradient(0, 0, 0, h);
  grad.addColorStop(0, tone(1)); grad.addColorStop(0.5, tone(0)); grad.addColorStop(1, tone(1));
  vctx.fillStyle = grad;
  for (let i = 0; i < barCount; i++) {
    const v = freqData[i * step] / 255;
    const barH = v * h * 0.45;
    vctx.fillRect(i * barWidth + 1, h / 2 - barH, Math.max(0.5, barWidth - 2), barH * 2);
  }
}
function drawDualWave() {
  const w = canvas.width, h = canvas.height;
  
  const step = w / timeData.length;
  vctx.lineWidth = 2 * devicePixelRatio;
  [{ color: tone(2), scale: 0.4, offset: 0 }, { color: tone(1, 0.6), scale: 0.28, offset: 10 }].forEach((layer) => {
    vctx.strokeStyle = layer.color;
    vctx.beginPath();
    for (let i = 0; i < timeData.length; i++) {
      const v = timeData[i] / 128 - 1;
      const y = h / 2 + v * h * layer.scale + layer.offset;
      if (i === 0) vctx.moveTo(0, y); else vctx.lineTo(i * step, y);
    }
    vctx.stroke();
  });
}
function drawParticles() {
  const w = canvas.width, h = canvas.height;
  
  const bass = bandAvg(0, 16);
  if (Math.random() < 0.15 + bass * 0.5) {
    particles.push({ x: Math.random() * w, y: h, vy: -(1 + bass * 4) * devicePixelRatio, r: (2 + bass * 4) * devicePixelRatio, life: 1 });
  }
  particles.forEach((p) => { p.y += p.vy; p.life -= 0.01; });
  particles = particles.filter((p) => p.life > 0 && p.y > -20);
  vctx.fillStyle = tone(1);
  particles.forEach((p) => {
    vctx.globalAlpha = Math.max(0, p.life);
    vctx.beginPath(); vctx.arc(p.x, p.y, p.r, 0, Math.PI * 2); vctx.fill();
  });
  vctx.globalAlpha = 1;
}
function drawStarfield() {
  const w = canvas.width, h = canvas.height;
  
  const cx = w / 2, cy = h / 2;
  const treble = bandAvg(freqData.length - 32, freqData.length);
  if (particles.length < 120) particles.push({ a: Math.random() * Math.PI * 2, r: 0, speed: 1 + Math.random() * 2 });
  particles.forEach((p) => { p.r += p.speed * (1 + treble * 3); });
  particles = particles.filter((p) => p.r < Math.max(w, h));
  vctx.fillStyle = tone(3);
  particles.forEach((p) => {
    const x = cx + Math.cos(p.a) * p.r, y = cy + Math.sin(p.a) * p.r;
    vctx.beginPath(); vctx.arc(x, y, 1.5 * devicePixelRatio, 0, Math.PI * 2); vctx.fill();
  });
}
let tunnelT = 0;
function drawTunnel() {
  const w = canvas.width, h = canvas.height;
  
  const cx = w / 2, cy = h / 2;
  tunnelT += 0.02;
  const rings = 10;
  for (let i = 0; i < rings; i++) {
    const v = freqData[Math.floor((i / rings) * freqData.length)] / 255;
    const r = (i / rings) * Math.min(w, h) * 0.5 + v * 20;
    vctx.strokeStyle = tone(0, 0.15 + v * 0.5);
    vctx.lineWidth = 2 * devicePixelRatio;
    vctx.beginPath();
    vctx.arc(cx, cy, r, tunnelT + i, tunnelT + i + Math.PI * 1.5);
    vctx.stroke();
  }
}
let spiralT = 0;
function drawSpiral() {
  const w = canvas.width, h = canvas.height;
  
  const cx = w / 2, cy = h / 2;
  spiralT += 0.015;
  const n = 80;
  vctx.fillStyle = tone(2);
  for (let i = 0; i < n; i++) {
    const v = freqData[Math.floor((i / n) * freqData.length)] / 255;
    const angle = i * 0.35 + spiralT;
    const r = (i / n) * Math.min(w, h) * 0.48;
    const x = cx + Math.cos(angle) * r, y = cy + Math.sin(angle) * r;
    vctx.beginPath(); vctx.arc(x, y, (1.5 + v * 3) * devicePixelRatio, 0, Math.PI * 2); vctx.fill();
  }
}
function drawBattery() {
  const w = canvas.width, h = canvas.height;
  
  const cols = 32, rows = 12;
  const cw = w / cols, ch = h / rows;
  for (let c = 0; c < cols; c++) {
    const v = freqData[Math.floor((c / cols) * (freqData.length - 1))] / 255;
    const lit = Math.round(v * rows);
    for (let r = 0; r < rows; r++) {
      if (r < rows - lit) continue;
      vctx.fillStyle = r < rows * 0.25 ? tone(2) : r < rows * 0.5 ? tone(3) : tone(0);
      vctx.fillRect(c * cw + 1, r * ch + 1, cw - 2, ch - 2);
    }
  }
}
let plasmaT = 0;
function drawPlasma() {
  const w = canvas.width, h = canvas.height;
  
  plasmaT += 0.02;
  const bass = bandAvg(0, 16);
  const mid = bandAvg(16, 64);
  const blobs = [
    { x: w * (0.3 + Math.sin(plasmaT) * 0.1), y: h * 0.5, r: 60 + bass * 100, color: tone(0) },
    { x: w * (0.7 + Math.cos(plasmaT * 1.3) * 0.1), y: h * 0.5, r: 50 + mid * 90, color: tone(1) },
  ];
  blobs.forEach((b) => {
    const grad = vctx.createRadialGradient(b.x, b.y, 0, b.x, b.y, b.r * devicePixelRatio);
    grad.addColorStop(0, b.color); grad.addColorStop(1, 'transparent');
    vctx.globalAlpha = 0.55; vctx.fillStyle = grad;
    vctx.fillRect(0, 0, w, h);
  });
  vctx.globalAlpha = 1;
}
let lastBassPeak = 0;
function drawFireworks() {
  const w = canvas.width, h = canvas.height;
  
  const bass = bandAvg(0, 12);
  const now = performance.now();
  if (bass > 0.62 && now - lastBassPeak > 260) {
    lastBassPeak = now;
    const cx = Math.random() * w, cy = h * (0.25 + Math.random() * 0.3);
    const hue = Math.floor(Math.random() * 360);
    for (let i = 0; i < 28; i++) {
      const a = (i / 28) * Math.PI * 2;
      particles.push({ x: cx, y: cy, vx: Math.cos(a) * (2 + Math.random() * 2), vy: Math.sin(a) * (2 + Math.random() * 2), life: 1, hue });
    }
  }
  particles.forEach((p) => { p.x += p.vx; p.y += p.vy; p.vy += 0.03; p.life -= 0.015; });
  particles = particles.filter((p) => p.life > 0);
  particles.forEach((p) => {
    vctx.globalAlpha = Math.max(0, p.life);
    vctx.fillStyle = `hsl(${p.hue}, 85%, 65%)`;
    vctx.beginPath(); vctx.arc(p.x, p.y, 2 * devicePixelRatio, 0, Math.PI * 2); vctx.fill();
  });
  vctx.globalAlpha = 1;
}
let ribbonT = 0;
function drawRibbon() {
  const w = canvas.width, h = canvas.height;
  
  ribbonT += 0.03;
  const vol = bandAvg(0, freqData.length);
  vctx.strokeStyle = tone(0);
  vctx.lineWidth = (4 + vol * 22) * devicePixelRatio;
  vctx.lineCap = 'round';
  vctx.beginPath();
  for (let x = 0; x <= w; x += 6) {
    const y = h / 2 + Math.sin(x * 0.01 + ribbonT) * h * 0.22 * (0.4 + vol);
    if (x === 0) vctx.moveTo(x, y); else vctx.lineTo(x, y);
  }
  vctx.stroke();
}
function drawKaleidoscope() {
  const w = canvas.width, h = canvas.height;
  
  const cx = w / 2, cy = h / 2;
  const segments = 8, n = 40;
  for (let s = 0; s < segments; s++) {
    vctx.save();
    vctx.translate(cx, cy);
    vctx.rotate((s / segments) * Math.PI * 2);
    vctx.beginPath();
    for (let i = 0; i < n; i++) {
      const v = freqData[Math.floor((i / n) * freqData.length)] / 255;
      const r = (i / n) * Math.min(w, h) * 0.5;
      const x = r, y = Math.sin(i * 0.5) * v * 30;
      if (i === 0) vctx.moveTo(x, y); else vctx.lineTo(x, y);
    }
    vctx.strokeStyle = tone(1, 0.6);
    vctx.lineWidth = 2 * devicePixelRatio;
    vctx.stroke();
    vctx.restore();
  }
}
function drawRain() {
  const w = canvas.width, h = canvas.height;
  
  const vol = bandAvg(0, freqData.length);
  if (particles.length < 200 && Math.random() < 0.3 + vol * 0.6) {
    particles.push({ x: Math.random() * w, y: -10, vy: (4 + Math.random() * 5) * devicePixelRatio, len: (10 + Math.random() * 20) * devicePixelRatio });
  }
  particles.forEach((p) => { p.y += p.vy; });
  particles = particles.filter((p) => p.y - p.len < h);
  vctx.strokeStyle = tone(0, 0.55);
  vctx.lineWidth = 1.5 * devicePixelRatio;
  particles.forEach((p) => {
    vctx.beginPath(); vctx.moveTo(p.x, p.y - p.len); vctx.lineTo(p.x, p.y); vctx.stroke();
  });
}
let lastRingPeak = 0;
function drawPulseRings() {
  const w = canvas.width, h = canvas.height;
  
  const cx = w / 2, cy = h / 2;
  const bass = bandAvg(0, 12);
  const now = performance.now();
  if (bass > 0.55 && now - lastRingPeak > 200) {
    lastRingPeak = now;
    particles.push({ r: 10, life: 1 });
  }
  particles.forEach((p) => { p.r += 4 * devicePixelRatio; p.life -= 0.02; });
  particles = particles.filter((p) => p.life > 0);
  particles.forEach((p) => {
    vctx.globalAlpha = Math.max(0, p.life);
    vctx.strokeStyle = tone(1);
    vctx.lineWidth = 3 * devicePixelRatio;
    vctx.beginPath(); vctx.arc(cx, cy, p.r, 0, Math.PI * 2); vctx.stroke();
  });
  vctx.globalAlpha = 1;
}
let gridT = 0;
function drawGridWave() {
  const w = canvas.width, h = canvas.height;
  
  gridT += 0.05;
  const cols = 24;
  vctx.strokeStyle = tone(0, 0.5);
  vctx.lineWidth = 1.5 * devicePixelRatio;
  for (let row = 0; row < 5; row++) {
    vctx.beginPath();
    for (let c = 0; c <= cols; c++) {
      const v = freqData[Math.floor((c / cols) * (freqData.length - 1))] / 255;
      const x = (c / cols) * w;
      const y = h * (0.3 + row * 0.15) - v * 40 * devicePixelRatio - row * 10 + Math.sin(gridT + c * 0.3) * 4;
      if (c === 0) vctx.moveTo(x, y); else vctx.lineTo(x, y);
    }
    vctx.stroke();
  }
}
function drawVortex() {
  const w = canvas.width, h = canvas.height;
  
  const cx = w / 2, cy = h / 2;
  const treble = bandAvg(freqData.length - 32, freqData.length);
  if (particles.length < 100) {
    const a = Math.random() * Math.PI * 2;
    const r = Math.min(w, h) * 0.5;
    particles.push({ x: cx + Math.cos(a) * r, y: cy + Math.sin(a) * r, a, r });
  }
  particles.forEach((p) => { p.r -= (1 + treble * 4) * devicePixelRatio; p.a += 0.05; p.x = cx + Math.cos(p.a) * p.r; p.y = cy + Math.sin(p.a) * p.r; });
  particles = particles.filter((p) => p.r > 4);
  vctx.fillStyle = tone(3);
  particles.forEach((p) => { vctx.beginPath(); vctx.arc(p.x, p.y, 2 * devicePixelRatio, 0, Math.PI * 2); vctx.fill(); });
}
function drawSunburst() {
  const w = canvas.width, h = canvas.height;
  
  const cx = w / 2, cy = h / 2;
  const n = 48;
  for (let i = 0; i < n; i++) {
    const v = freqData[Math.floor((i / n) * freqData.length)] / 255;
    const a0 = (i / n) * Math.PI * 2, a1 = ((i + 0.8) / n) * Math.PI * 2;
    const r = Math.min(w, h) * 0.12 + v * Math.min(w, h) * 0.38;
    vctx.beginPath();
    vctx.moveTo(cx, cy);
    vctx.lineTo(cx + Math.cos(a0) * r, cy + Math.sin(a0) * r);
    vctx.lineTo(cx + Math.cos(a1) * r, cy + Math.sin(a1) * r);
    vctx.closePath();
    vctx.fillStyle = tone(Math.floor(i * 4 / n), 0.5);
    vctx.fill();
  }
}
function drawBubbles() {
  const w = canvas.width, h = canvas.height;
  
  const mid = bandAvg(16, 64);
  if (particles.length < 60 && Math.random() < 0.1 + mid * 0.3) {
    particles.push({ x: Math.random() * w, y: h + 10, r: (4 + Math.random() * 16) * devicePixelRatio, vy: -(0.5 + Math.random() * 1.5) * devicePixelRatio });
  }
  particles.forEach((p) => { p.y += p.vy; });
  particles = particles.filter((p) => p.y + p.r > 0);
  vctx.strokeStyle = tone(2, 0.7);
  vctx.lineWidth = 1.5 * devicePixelRatio;
  particles.forEach((p) => { vctx.beginPath(); vctx.arc(p.x, p.y, p.r, 0, Math.PI * 2); vctx.stroke(); });
}

const VIS_DRAW = {
  bars: drawBars, wave: drawWave, circular: drawCircular, mirrorBars: drawMirrorBars,
  dualWave: drawDualWave, particles: drawParticles, starfield: drawStarfield, tunnel: drawTunnel,
  spiral: drawSpiral, battery: drawBattery, plasma: drawPlasma, fireworks: drawFireworks,
  ribbon: drawRibbon, kaleidoscope: drawKaleidoscope, rain: drawRain, pulseRings: drawPulseRings,
  gridWave: drawGridWave, vortex: drawVortex, sunburst: drawSunburst, bubbles: drawBubbles,
};


return {
  draw(mode, context, x, y, width, height, frequency, waveform, colors) {
    const visualizer = VIS_DRAW[mode];
    if (!visualizer || width < 8 || height < 8) return;
    if (previous !== mode) { previous = mode; particles = []; }
    vctx = context;
    canvas = { width, height };
    freqData = frequency;
    timeData = waveform;
    palette = colors;
    vctx.save();
    try {
      vctx.beginPath();
      vctx.rect(x, y, width, height);
      vctx.clip();
      vctx.translate(x, y);
      visualizer();
    } finally { vctx.restore(); }
  },
  reset() { previous = null; particles = []; }
};
})();
