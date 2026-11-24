(() => {
    const phases = { Night: 'night', Golden: 'golden', Day: 'day', Blue: 'blue' };
    const time = new Intl.DateTimeFormat('en-GB', {
        timeZone: 'Europe/Athens', hourCycle: 'h23', hour: 'numeric', minute: 'numeric', second: 'numeric'
    });
    const localMinutes = date => {
        const parts = Object.fromEntries(time.formatToParts(date).map(part => [part.type, part.value]));
        return Number(parts.hour) * 60 + Number(parts.minute) + Number(parts.second) / 60;
    };
    const calendar = new Intl.DateTimeFormat('en-CA', {
        timeZone: 'Europe/Athens', year: 'numeric', month: 'numeric', day: 'numeric'
    });
    const radians = Math.PI / 180;
    let cachedDay, sunrise, sunset;
    function sunTimes(date) {
        const { year, month, day: dayOfMonth } = Object.fromEntries(
            calendar.formatToParts(date).map(part => [part.type, Number(part.value)]));
        const midnight = Date.UTC(year, month - 1, dayOfMonth);
        if (midnight === cachedDay) return;
        cachedDay = midnight;
        const day = (midnight - Date.UTC(year, 0, 1)) / 86400000;
        const g = 2 * Math.PI * day / ((Date.UTC(year + 1, 0, 1) - Date.UTC(year, 0, 1)) / 86400000);
        const equation = 229.18 * (0.000075 + 0.001868 * Math.cos(g) - 0.032077 * Math.sin(g) -
            0.014615 * Math.cos(2 * g) - 0.040849 * Math.sin(2 * g));
        const declination = 0.006918 - 0.399912 * Math.cos(g) + 0.070257 * Math.sin(g) -
            0.006758 * Math.cos(2 * g) + 0.000907 * Math.sin(2 * g) -
            0.002697 * Math.cos(3 * g) + 0.00148 * Math.sin(3 * g);
        const latitude = 37.9715 * radians;
        const hourAngle = Math.acos(Math.cos(90.833 * radians) / (Math.cos(latitude) * Math.cos(declination)) -
            Math.tan(latitude) * Math.tan(declination)) / radians;
        const noon = 720 - 4 * 23.7257 - equation;
        sunrise = localMinutes(new Date(cachedDay + (noon - 4 * hourAngle) * 60000));
        sunset = localMinutes(new Date(cachedDay + (noon + 4 * hourAngle) * 60000));
    }
    function phaseAt(date, selection = 'Auto') {
        if (Object.hasOwn(phases, selection)) return { from: phases[selection], to: phases[selection], blend: 0 };
        sunTimes(date);
        const keyframes = [[0, 'night'], [sunrise - 55, 'night'], [sunrise - 22, 'blue'],
            [sunrise + 12, 'golden'], [sunrise + 67, 'day'], [sunset - 64, 'day'],
            [sunset - 10, 'golden'], [sunset + 26, 'blue'], [sunset + 70, 'night'], [1440, 'night']];
        const minute = localMinutes(date);
        const i = keyframes.findIndex(([at]) => minute < at);
        const [[start, from], [end, to]] = [keyframes[i - 1], keyframes[i]];
        const t = (minute - start) / (end - start);
        return { from, to, blend: from === to ? 0 : t * t * (3 - 2 * t) };
    }
    const cache = new Map();
    let lastFrame, blended, blendedContext, darkAcropolis, darkAcropolisKey = '', lastDrawAt = 0;
    const atlas = (image, width, height) => {
        const scale = Math.max(width / image.naturalWidth, height / image.naturalHeight);
        const w = image.naturalWidth * scale, h = image.naturalHeight * scale;
        return { x: (width - w) / 2, y: (height - h) / 2, w, h };
    };
    function cover(g, image, width, height) {
        const b = atlas(image, width, height);
        g.drawImage(image, b.x, b.y, b.w, b.h);
    }
    // Night panoramas have some baked-in artificial lamps on the sacred plateau.
    // Repaint only the Acropolis silhouette with night-graded daylight texture;
    // retaining naturally lit streets in the surrounding city.
    function acropolisPatch(dayImage, width, height) {
        const key = [dayImage.src,width,height].join('|');
        if (darkAcropolisKey === key) return darkAcropolis;
        darkAcropolisKey = key;
        // A 1080p color/mask patch is sufficient for a softly feathered light correction.
        // Do not allocate two extra full-resolution 4K canvases on each monitor.
        const scale = Math.min(1, 1920 / width, 1080 / height);
        const pw = Math.max(1, Math.round(width * scale)), ph = Math.max(1, Math.round(height * scale));
        const overlay = document.createElement('canvas');
        overlay.width = pw; overlay.height = ph;
        const painter = overlay.getContext('2d');
        if (!painter) return null;
        painter.filter = 'brightness(0.17) saturate(0.44) contrast(0.91)';
        cover(painter, dayImage, pw, ph);
        painter.filter = 'none';
        const mask = document.createElement('canvas');
        mask.width = pw; mask.height = ph;
        const mc = mask.getContext('2d');
        if (!mc) return null;
        const b = atlas(dayImage, pw, ph);
        const silhouette = [
            [.200,.515],[.241,.455],[.267,.398],[.259,.330],[.320,.320],
            [.327,.296],[.425,.287],[.476,.259],[.480,.194],[.604,.197],
            [.625,.279],[.732,.320],[.782,.435],[.810,.509],[.709,.565],
            [.612,.566],[.525,.611],[.401,.606],[.287,.551]
        ];
        mc.filter = 'blur(' + Math.max(4, Math.round(pw * .007)) + 'px)';
        mc.fillStyle = 'white'; mc.beginPath();
        silhouette.forEach(([x,y],i) => i ? mc.lineTo(b.x+x*b.w,b.y+y*b.h) :
            mc.moveTo(b.x+x*b.w,b.y+y*b.h));
        mc.closePath(); mc.fill();
        painter.globalCompositeOperation = 'destination-in';
        painter.drawImage(mask,0,0);
        painter.globalCompositeOperation = 'source-over';
        darkAcropolis = overlay;
        return overlay;
    }
    function load(name, highResolution) {
        // Prefer WebP at both sizes; keep legacy JPEGs as a backwards-compatible
        // fallback so upgrades never blank a built-in wallpaper.
        const files = highResolution
            ? [name + '-4k.webp', name + '.webp', name + '.jpg']
            : [name + '.webp', name + '.jpg'];
        function attempt(index) {
            if (index >= files.length) return Promise.resolve(null);
            const image = new Image();
            image.src = 'ancient-athens/' + files[index];
            let ready;
            try { ready = typeof image.decode === 'function' ? image.decode() :
                new Promise((resolve,reject) => { image.onload=resolve; image.onerror=reject; }); }
            catch { return attempt(index + 1); }
            return Promise.resolve(ready).then(() => image, () => attempt(index + 1));
        }
        return attempt(0);
    }
    function frames(width, height, date, selection) {
        const state = phaseAt(date, selection);
        // Select the original only when it can cover the canvas at its native resolution.
        const highResolution = width > 1376 || height > 768;
        const wanted = new Set([state.from, state.to]);
        if (wanted.has('night')) wanted.add('day');
        for (const [name, frame] of cache)
            if (!wanted.has(name) || frame.highResolution !== highResolution) cache.delete(name);
        for (const name of wanted) {
            if (cache.has(name)) continue;
            const frame = { highResolution, image: null };
            frame.ready = load(name, highResolution).then(image => { frame.image = image; });
            cache.set(name, frame);
        }
        return { ...state, base: cache.get(state.from), overlay: cache.get(state.to), daylight: cache.get('day') };
    }
    function prepare(width, height, selection) {
        const { base, overlay, daylight } = frames(width, height, new Date(), selection);
        return Promise.all([base.ready, overlay.ready, daylight?.ready].filter(Boolean));
    }
    function release() {
        cache.clear();
        lastFrame = null;
        blended = blendedContext = darkAcropolis = null;
        darkAcropolisKey = '';
        lastDrawAt = 0;
        window.OpsisAncientAthensFx?.clear();
    }
    function setWeather(data) { return window.OpsisAncientAthensFx?.setWeather(data) ?? false; }
    function draw(ctx, width, height, selection, force = false, options = {}) {
        const date = options.now instanceof Date ? options.now : new Date();
        const { base, overlay, daylight, blend, from, to } = frames(width, height, date, selection);
        const primary = base.image || overlay.image;
        if (!primary) return;
        if (!blended || blended.width !== width || blended.height !== height) {
            blended = document.createElement('canvas');
            blended.width = width; blended.height = height;
            blendedContext = blended.getContext('2d');
            lastFrame = null;
        }
        if (!blendedContext) return;
        const key=[width,height,primary.src,base.image?.src,overlay.image?.src,
            daylight?.image?.src,Math.round(blend*300),from,to].join('|');
        if (force || lastFrame !== key) {
            const g = blendedContext;
            g.save();g.setTransform(1,0,0,1,0,0);
            g.clearRect(0,0,width,height);
            g.imageSmoothingEnabled = true;g.imageSmoothingQuality='high';
            function phase(image, name, opacity) {
                if (!image || opacity <= 0) return;
                g.globalAlpha = opacity;
                cover(g,image,width,height);
                if (name === 'night' && daylight?.image) {
                    const patch=acropolisPatch(daylight.image,width,height);
                    if(patch) { g.globalAlpha=opacity;g.drawImage(patch,0,0,width,height); }
                }
            }
            if (base.image && overlay.image && base.image !== overlay.image) {
                phase(base.image,from,1);
                phase(overlay.image,to,blend);
            } else {
                const frame=primary===base.image?from:to;
                phase(primary,frame,1);
            }
            g.restore();
            lastFrame = key;
        }
        ctx.save();ctx.setTransform(1,0,0,1,0,0);
        ctx.globalCompositeOperation='source-over';ctx.globalAlpha=1;
        ctx.drawImage(blended,0,0);
        sunTimes(date);
        const now=date.getTime();
        const dt=lastDrawAt ? Math.max(0,Math.min(1,(now-lastDrawAt)/1000)) : .04;
        lastDrawAt=now;
        window.OpsisAncientAthensFx?.render(ctx,width,height,{
            date,minutes:localMinutes(date),sr:sunrise,ss:sunset,
            selection,reducedMotion:options.reducedMotion===true,
            lowPower:options.lowPower===true,delta:dt
        });
        ctx.restore();
    }
    window.OpsisAncientAthens = { phaseAt, prepare, release, setWeather, draw };
})();
