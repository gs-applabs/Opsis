if (window.opsisMusicPainter) return true;

const canvas = document.createElement('canvas');
canvas.id = 'opsisMusicVisualizer';
canvas.setAttribute('aria-hidden', 'true');
Object.assign(canvas.style, {
    position: 'fixed',
    pointerEvents: 'none',
    zIndex: '1',
    visibility: 'hidden'
});
document.body.append(canvas);
const ctx = canvas.getContext('2d');
if (!ctx) {
    canvas.remove();
    return false;
}

const baseWidth = 478;
const baseHeight = 180;
const defaultColors = ['#87eaff', '#a896ff', '#ff8bc9', '#ffe7ac'];
const fonts = new Set(['Potra', 'Potra Light', 'Pavelt', 'MODERN WARFARE']);
let artwork = null;
let lastTrack;
let appearedAt = 0;

const fraction = (value) => Math.min(1, Math.max(0, Number(value) || 0));
const time = (seconds) => {
    const total = Math.floor(Math.max(0, seconds));
    const hours = Math.floor(total / 3600);
    const minutes = Math.floor(total / 60) % 60;
    const rest = String(total % 60).padStart(2, '0');
    return hours ? hours + ':' + String(minutes).padStart(2, '0') + ':' + rest : minutes + ':' + rest;
};

function radial(bands, colors, centerX, centerY, radiusX, radiusY) {
    const count = 64;
    ctx.save();
    ctx.lineCap = 'round';
    ctx.lineWidth = 2.5;
    ctx.shadowBlur = 8;
    for (let i = 0; i < count; i++) {
        const angle = i / count * Math.PI * 2 - Math.PI / 2;
        const energy = fraction(bands[i]);
        const reach = .67 + Math.pow(energy, .72) * .33;
        const color = colors[Math.floor(i * colors.length / count)];
        ctx.strokeStyle = ctx.shadowColor = color;
        const x = Math.cos(angle) * radiusX;
        const y = Math.sin(angle) * radiusY;
        ctx.beginPath();
        ctx.moveTo(centerX + x * .61, centerY + y * .61);
        ctx.lineTo(centerX + x * reach, centerY + y * reach);
        ctx.stroke();
    }
    ctx.restore();
}

function mirror(bands, colors, width, centerY, amplitude) {
    const count = 48;
    const edge = Math.min(20, width * .08);
    ctx.save();
    ctx.lineWidth = 3;
    ctx.lineCap = 'round';
    const gradient = ctx.createLinearGradient(edge, 0, width - edge, 0);
    colors.forEach((color, index) => gradient.addColorStop(index / (colors.length - 1), color));
    ctx.strokeStyle = gradient;
    ctx.shadowColor = colors[1];
    ctx.shadowBlur = 8;
    for (let i = 0; i < count; i++) {
        const distance = Math.abs(i - (count - 1) / 2) / ((count - 1) / 2);
        const value = fraction(bands[Math.floor(distance * 63)]);
        const bar = 2 + Math.pow(value, .7) * amplitude;
        const x = edge + i / (count - 1) * (width - 2 * edge);
        ctx.beginPath();
        ctx.moveTo(x, centerY - bar);
        ctx.lineTo(x, centerY + bar);
        ctx.stroke();
    }
    ctx.restore();
}

function drawCover(cover, x, y, radius, round) {
    if (!cover?.complete || !cover.naturalWidth) return;
    ctx.save();
    ctx.beginPath();
    if (round) ctx.arc(x, y, radius, 0, 2 * Math.PI);
    else ctx.roundRect(x - radius, y - radius, radius * 2, radius * 2, 7);
    ctx.clip();
    ctx.drawImage(cover, x - radius, y - radius, radius * 2, radius * 2);
    ctx.restore();
}

function note(x, y, opacity) {
    if (opacity <= 0) return;
    ctx.save();
    ctx.globalAlpha = opacity;
    ctx.beginPath();
    ctx.arc(x, y, 15, 0, 2 * Math.PI);
    const shine = ctx.createRadialGradient(x - 5, y - 6, 1, x, y, 15);
    shine.addColorStop(0, '#edfcff');
    shine.addColorStop(.28, '#79ccf6d9');
    shine.addColorStop(.62, '#536bd9b3');
    shine.addColorStop(1, '#172952b8');
    ctx.fillStyle = shine;
    ctx.strokeStyle = '#dff5ffad';
    ctx.shadowColor = '#6dc8ff';
    ctx.shadowBlur = 10;
    ctx.fill();
    ctx.stroke();
    ctx.shadowBlur = 0;
    ctx.save();
    ctx.strokeStyle = '#dcf6ff90';
    ctx.lineWidth = .7;
    ctx.beginPath();
    ctx.ellipse(x, y, 13, 5, -.6, 0, Math.PI * 2);
    ctx.stroke();
    ctx.beginPath();
    ctx.ellipse(x, y, 5, 13, -.6, 0, Math.PI * 2);
    ctx.stroke();
    ctx.restore();
    ctx.fillStyle = '#ffffff';
    ctx.font = 'bold 20px "Segoe UI Symbol",sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText('♪', x, y + 1);
    ctx.restore();
}

function fitText(text, x, y, maxWidth) {
    const value = String(text).replace(/\s+/g, ' ').trim();
    if (!value || maxWidth <= 0) return;
    if (ctx.measureText(value).width <= maxWidth) {
        ctx.fillText(value, x, y);
        return;
    }
    const letters = Array.from(value.slice(0, 512));
    while (letters.length && ctx.measureText(letters.join('') + '…').width > maxWidth) letters.pop();
    ctx.fillText(letters.join('') + '…', x, y);
}

function details(frame, x, centerY, available, maxHeight, accent, centered = false) {
    const title = frame.title?.trim();
    const subtitle = [frame.artist?.trim(), frame.album?.trim(), frame.subtitle?.trim()]
        .filter((value, index, list) => value && list.indexOf(value) === index).join(' · ');
    const source = String(frame.source || '').split('!')[0].replace(/\.exe$/i, '').trim();
    const trackLabel = frame.trackNumber > 0 ? '#' + frame.trackNumber + (frame.albumTrackCount > 0 ? '/' + frame.albumTrackCount : '') : '';
    const extra = [frame.albumArtist !== frame.artist ? frame.albumArtist?.trim() : '', trackLabel,
        ...(Array.isArray(frame.genres) ? frame.genres.slice(0, 3) : []), source].filter(Boolean).join(' · ');
    const timeline = frame.duration > 0;
    const blockHeight = (title ? 26 : 0) + (subtitle ? 20 : 0) + (extra ? 16 : 0) + (timeline ? 16 : 0);
    if (!blockHeight || maxHeight <= 0 || available <= 0) return;
    const scale = Math.min(1, maxHeight / blockHeight);
    let y = 0;
    const typeface = fonts.has(frame.font) ? '"' + frame.font + '",' : '';
    ctx.save();
    ctx.shadowColor = '#080e21';
    ctx.shadowBlur = 7;
    ctx.shadowOffsetY = 1.5;
    const reveal = frame.reducedMotion ? 1 : fraction((performance.now() - appearedAt) / 480);
    const eased = 1 - Math.pow(1 - reveal, 3);
    ctx.globalAlpha *= .15 + .85 * eased;
    ctx.translate(x + (centered ? 0 : (1 - eased) * 9), centerY - blockHeight * scale / 2);
    ctx.scale(scale, scale);
    available /= scale;
    const textX = centered ? available / 2 : 0;
    ctx.textAlign = centered ? 'center' : 'left';
    if (title) {
        ctx.fillStyle = '#fff';
        ctx.font = '600 23px ' + typeface + '"Segoe UI",sans-serif';
        fitText(title, textX, y + 21, available);
        y += 26;
    }
    if (subtitle) {
        ctx.fillStyle = '#e7f2ff';
        ctx.font = '15px "Segoe UI",sans-serif';
        fitText(subtitle, textX, y + 14, available);
        y += 20;
    }
    if (extra) {
        ctx.fillStyle = '#c9e6f9';
        ctx.font = '12px "Segoe UI",sans-serif';
        fitText(extra, textX, y + 12, available);
        y += 16;
    }
    if (timeline) {
        ctx.textAlign = 'left';
        const clock = time(frame.elapsed) + ' / ' + time(frame.duration);
        ctx.font = '12px "Segoe UI",sans-serif';
        const clockWidth = Math.min(ctx.measureText(clock).width + 2, available);
        const timelineWidth = centered ? Math.min(available, 240 / scale) : available;
        const timelineX = (available - timelineWidth) / 2;
        const trackWidth = Math.max(0, timelineWidth - clockWidth - 9);
        ctx.fillStyle = '#e7f2ff';
        fitText(clock, timelineX + (trackWidth > 0 ? trackWidth + 9 : 0), y + 12, clockWidth);
        if (trackWidth > 0) {
            ctx.fillStyle = '#d7e0ff82';
            ctx.fillRect(timelineX, y + 5, trackWidth, 3);
            ctx.fillStyle = accent;
            ctx.fillRect(timelineX, y + 5, trackWidth * fraction(frame.elapsed / frame.duration), 3);
        }
    }
    ctx.restore();
}

// The weather block always wins; never persist an automatic placement.
function placeMusicOverlay(vw, vh, w, h, x, y, weather, gap) {
    const clamp = (v, min, max) => Math.min(max, Math.max(min, v));
    const maxX = vw - gap - w, maxY = vh - gap - h;
    if (maxX < gap || maxY < gap) return null;
    const candidate = (left, top) => ({left: clamp(left, gap, maxX), top: clamp(top, gap, maxY)});
    const preferred = candidate(x, y);
    if (!weather) return preferred;
    const safe = p => p.left + w <= weather.left - gap || p.left >= weather.right + gap ||
        p.top + h <= weather.top - gap || p.top >= weather.bottom + gap;
    if (safe(preferred)) return preferred;
    const choices = [
        candidate(x, weather.top - h - gap), candidate(x, weather.bottom + gap),
        candidate(weather.left - w - gap, y), candidate(weather.right + gap, y)
    ].filter(safe);
    choices.sort((a,b) => Math.hypot(a.left-x,a.top-y)-Math.hypot(b.left-x,b.top-y));
    return choices[0] ?? null;
}

function paint(frame) {
    const track = frame.trackKey ?? JSON.stringify([frame.title, frame.artist, frame.album]);
    if (lastTrack !== track) {
        lastTrack = track;
        appearedAt = performance.now();
        artwork = null;
    }
    if (frame.updateCover) {
        artwork = frame.cover ? new Image() : null;
        if (artwork) artwork.src = frame.cover;
    }

    if (typeof cfg === 'undefined' || !cfg.mode || cfg.mode === 'Application' ||
        (typeof isPaused !== 'undefined' && isPaused)) {
        canvas.style.visibility = 'hidden';
        return true;
    }

    const scale = Math.min(1.4, window.innerWidth / (baseWidth + 32), window.innerHeight / (baseHeight + 32));
    if (scale <= 0) return true;
    const hasDetails = Boolean(frame.title?.trim() || frame.artist?.trim() || frame.album?.trim() || frame.subtitle?.trim() || frame.albumArtist?.trim() || frame.source || frame.trackNumber > 0 || frame.genres?.length || frame.duration > 0);
    const hasHeader = hasDetails || Boolean(artwork);
    const suite = frame.preset && window.opsisSuiteVisualizer;
    const round = suite ? frame.preset === 'circular' || frame.preset === 'none' : !frame.bars;
    const logicalWidth = !round || hasDetails ? baseWidth : baseHeight;
    const logicalHeight = round && hasDetails ? baseHeight + 94 : suite ? baseHeight + 52 : baseHeight;
    const margin = 12 * scale;
    const width = Math.min(logicalWidth * (frame.width ?? 100) / 100, (window.innerWidth - 2 * margin) / scale);
    const height = Math.min(logicalHeight * (frame.height ?? 100) / 100, (window.innerHeight - 2 * margin) / scale);
    const preferredX = margin + (window.innerWidth - 2 * margin - width * scale) * fraction(frame.x);
    const preferredY = margin + (window.innerHeight - 2 * margin - height * scale) * fraction(frame.y);
    // Read the real transformed bounds, including Signature card and user text scaling.
    const weatherBox = document.getElementById('wxInfo');
    // The painter can be injected into a page without the normal wallpaper config.
    // Use the actual DOM visibility/bounds instead of assuming cfg is in scope.
    const weatherBounds = weatherBox && weatherBox.getClientRects().length &&
        getComputedStyle(weatherBox).visibility !== 'hidden' ? weatherBox.getBoundingClientRect() : null;
    const weather = weatherBounds && weatherBounds.width > 0 && weatherBounds.height > 0 ? weatherBounds : null;
    const placement = placeMusicOverlay(window.innerWidth, window.innerHeight, width * scale, height * scale,
        preferredX, preferredY, weather, Math.max(12, margin));
    if (!placement || width < 1 || height < 1) {
        canvas.style.visibility = 'hidden';
        return true;
    }
    const {left, top} = placement;
    const density = Math.min(2.5, (window.devicePixelRatio || 1) * scale);
    const pixelWidth = Math.max(1, Math.round(width * density));
    const pixelHeight = Math.max(1, Math.round(height * density));
    if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
        canvas.width = pixelWidth;
        canvas.height = pixelHeight;
    }
    // Avoid reassigning identical CSS geometry on every audio frame.
    for (const [key, value] of [['left', left], ['top', top], ['width', width * scale], ['height', height * scale]]) {
        const px = value + 'px';
        if (canvas.style[key] !== px) canvas.style[key] = px;
    }
    ctx.setTransform(pixelWidth / width, 0, 0, pixelHeight / height, 0, 0);
    ctx.clearRect(0, 0, width, height);

    const colors = frame.colors ?? defaultColors;
    const bands = frame.preview
        ? frame.bands.map((_, i) => .08 + .5 * Math.pow(.5 + .5 * Math.sin(performance.now() / 170 - i / 6), 3))
        : frame.bands;
    const opacity = frame.preview || frame.reducedMotion ? 1 : .65 + .35 * fraction((performance.now() - appearedAt) / 480);
    function glassPanel(x, y, w, h) {
        if (w < 10 || h < 10) return;
        ctx.save();
        ctx.beginPath();
        ctx.roundRect(x, y, w, h, Math.min(12, h / 3));
        const glass = ctx.createLinearGradient(x, y, x + w, y + h);
        glass.addColorStop(0, '#243b60b8');
        glass.addColorStop(.55, '#10213b99');
        glass.addColorStop(1, '#334f7fa6');
        ctx.fillStyle = glass;
        ctx.fill();
        ctx.strokeStyle = '#c4e6ff55';
        ctx.lineWidth = 1;
        ctx.stroke();
        ctx.restore();
    }
    function drawSuite(x, y, w, h) {
        const decode = bytes => bytes ? Uint8Array.from(atob(bytes), value => value.charCodeAt(0)) : null;
        const frequency = !frame.preview && decode(frame.frequency) || Uint8Array.from({ length: 512 },
            (_, i) => Math.round(255 * fraction(bands[Math.min(bands.length - 1, Math.floor(i / 8))])));
        const waveform = !frame.preview && decode(frame.waveform) || Uint8Array.from({ length: 256 },
            (_, i) => frame.preview ? Math.round(128 + 60 * Math.sin(i * .16 + performance.now() / 110)) : 128);
        suite.draw(frame.preset, ctx, x, y, w, h, frequency, waveform, colors);
    }
    if (!round) {
        const headerHeight = hasHeader ? Math.min(104, height * .59) : 0;
        if (hasHeader) glassPanel(3, 3, width - 6, headerHeight - 6);
        const spectrumHeight = height - headerHeight;
        if (suite) drawSuite(7, headerHeight + 3, width - 14, spectrumHeight - 6);
        else mirror(bands, colors, width, headerHeight + spectrumHeight * .5, Math.max(3, spectrumHeight / 2 - 9));
        const coverRadius = Math.min(26, headerHeight * .4);
        if (artwork) drawCover(artwork, hasDetails ? 17 + coverRadius : width / 2, headerHeight / 2, coverRadius, false);
        const textX = artwork ? 29 + 2 * coverRadius : 20;
        if (hasDetails) details(frame, textX, headerHeight / 2, width - textX - 35, headerHeight - 8, colors[0]);
        note(width - 19, Math.max(17, headerHeight - 17), opacity);
    } else {
        const textHeight = hasDetails ? Math.min(94, height * .46) : 0;
        if (hasDetails) glassPanel(3, height - textHeight + 2, width - 6, textHeight - 4);
        const centerX = width / 2;
        const centerY = (height - textHeight) / 2;
        const radiusX = Math.max(7, (hasDetails ? centerX * baseHeight / baseWidth : centerX) - 9);
        const radiusY = Math.max(7, centerY - 9);
        if (suite && frame.preset === 'circular') drawSuite(centerX - radiusX, centerY - radiusY, 2 * radiusX, 2 * radiusY);
        else if (!suite) radial(bands, colors, centerX, centerY, radiusX, radiusY);
        drawCover(artwork, centerX, centerY, Math.min(radiusX, radiusY) * (frame.preset === 'none' ? .95 : .52), true);
        if (hasDetails) details(frame, 12, height - textHeight / 2, width - 24, textHeight - 4, colors[0], true);
        note(Math.max(17, centerX - radiusX * .65), Math.min(height - 17, centerY + radiusY * .65), opacity);
    }
    canvas.style.visibility = 'visible';
    return true;
}

window.opsisMusicPainter = {
    paint,
    remove() {
        window.opsisSuiteVisualizer?.reset();
        canvas.remove();
        delete window.opsisMusicPainter;
    }
};
return true;
