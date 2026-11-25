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
    let lastFrame;
    function load(name, highResolution) {
        const src = 'ancient-athens/' + name + (highResolution ? '-4k.webp' : '.jpg');
        const image = new Image();
        image.src = src;
        try {
            return image.decode().then(() => image, () => highResolution ? load(name, false) : null);
        } catch {
            return Promise.resolve(null);
        }
    }
    function frames(width, height, date, selection) {
        const state = phaseAt(date, selection);
        // Select the original only when it can cover the canvas at its native resolution.
        const highResolution = width > 1376 || height > 768;
        const wanted = new Set([state.from, state.to]);
        for (const [name, frame] of cache)
            if (!wanted.has(name) || frame.highResolution !== highResolution) cache.delete(name);
        for (const name of wanted) {
            if (cache.has(name)) continue;
            const frame = { highResolution, image: null };
            frame.ready = load(name, highResolution).then(image => { frame.image = image; });
            cache.set(name, frame);
        }
        return { ...state, base: cache.get(state.from), overlay: cache.get(state.to) };
    }
    function prepare(width, height, selection) {
        const { base, overlay } = frames(width, height, new Date(), selection);
        return Promise.all([base.ready, overlay.ready]);
    }
    function release() {
        cache.clear();
        lastFrame = null;
    }
    function draw(ctx, width, height, selection, force = false) {
        const { base, overlay, blend } = frames(width, height, new Date(), selection);
        const primary = base.image || overlay.image;
        if (!primary) return;
        const frame = [width, height, primary.src, overlay.image?.src, blend.toFixed(4)].join('|');
        if (!force && lastFrame === frame) return;
        lastFrame = frame;
        function cover(image) {
            const scale = Math.max(width / image.naturalWidth, height / image.naturalHeight);
            const w = image.naturalWidth * scale;
            const h = image.naturalHeight * scale;
            ctx.drawImage(image, (width - w) / 2, (height - h) / 2, w, h);
        }
        ctx.save();
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = 'high';
        ctx.globalAlpha = 1;
        cover(primary);
        if (overlay.image && overlay.image !== primary && blend > 0) {
            ctx.globalAlpha = blend;
            cover(overlay.image);
        }
        ctx.restore();
    }
    window.OpsisAncientAthens = { phaseAt, prepare, release, draw };
})();
