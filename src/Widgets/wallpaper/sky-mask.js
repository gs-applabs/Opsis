(() => {
    'use strict';

    // Work in image coordinates, independent of monitor size, grading and weather.
    // A column stops at its first sustained colour/texture change. It can never
    // reopen under a rooftop, ridge or tree, even if the pixels below look blue.
    function skyline(pixels, width, height) {
        const edge = new Uint16Array(width);
        const seedRow = 3; // skip borders introduced by some scene assets
        if (height < 24) return edge;
        const limit = Math.floor(height * .65);
        const distance = (a, b) => Math.max(
            Math.abs(pixels[a] - pixels[b]),
            Math.abs(pixels[a + 1] - pixels[b + 1]),
            Math.abs(pixels[a + 2] - pixels[b + 2])
        );
        for (let x = 0; x < width; x++) {
            const at = y => (y * width + x) * 4;
            const seed = at(seedRow);
            const [r, g, b, a] = pixels.subarray(seed, seed + 4);
            // Blue skies, warm skies and bright neutral skies are plausible seeds.
            const sky = b >= r + 9 && b >= g + 4 && b >= 65
                || r >= 125 && r >= g && g >= b && g >= r * .68
                || Math.min(r, g, b) >= 155 && Math.max(r, g, b) - Math.min(r, g, b) < 30;
            if (!sky || a < 250 || distance(seed, at(seedRow + 3)) > 19) continue;
            let y = seedRow + 3;
            for (; y < limit; y++) {
                const p = at(y);
                if (pixels[p + 3] < 250 || distance(p, seed) > 96 ||
                    distance(p, at(y - 3)) > 15) break;
            }
            // No boundary found: uniform water, haze and walls can resemble sky.
            if (y < limit && y >= seedRow + 6) edge[x] = y - 3;
        }

        // Require supporting neighbouring columns and err toward preserving the
        // foreground. A one-column low estimate must never cut through a spire.
        const safe = new Uint16Array(width);
        for (let x = 0; x < width; x++) {
            const left = Math.max(0, x - 1), right = Math.min(width - 1, x + 1);
            safe[x] = Math.min(edge[left], edge[x], edge[right]);
        }
        return safe;
    }

    // A deterministic fingerprint over 32x32 sampled image pixels and original
    // dimensions. This is a content-change detector, not a cryptographic signature.
    function fingerprint(image) {
        const tile = document.createElement('canvas');
        tile.width = tile.height = 32;
        const g = tile.getContext('2d', { willReadFrequently: true });
        g.drawImage(image, 0, 0, 32, 32);
        const bytes = g.getImageData(0, 0, 32, 32).data;
        let left = 2166136261, right = 2166136261 ^ (image.naturalWidth * 31 + image.naturalHeight);
        for (let i = 0; i < bytes.length; i++) {
            left = Math.imul(left ^ bytes[i], 16777619) >>> 0;
            right = Math.imul(right ^ bytes[bytes.length - 1 - i], 16777619) >>> 0;
        }
        return [image.naturalWidth, image.naturalHeight,
            left.toString(16).padStart(8, '0'), right.toString(16).padStart(8, '0')].join('-');
    }

    // No persistent updates on frame ticks: only a first-time image load or an
    // explicit editor save writes. LocalStorage is scoped to the wallpaper WebView2.
    function storageKey(file) { return 'opsis.sky-mask.v1:' + file; }
    function loadSaved(file, hash, width, height) {
        try {
            const stored = JSON.parse(localStorage.getItem(storageKey(file)) || 'null');
            if (stored?.hash === hash && stored.width === width && stored.height === height &&
                Array.isArray(stored.rows) && stored.rows.length === width &&
                stored.rows.every(n => Number.isInteger(n) && n >= 0 && n <= height))
                return stored.rows;
        } catch { /* restricted storage or corrupt JSON: fall back to detection */ }
        return null;
    }
    function saveMask(file, hash, width, height, rows) {
        try {
            const value = JSON.stringify({ hash, width, height, rows: Array.from(rows) });
            if (value.length < 12000 && localStorage.getItem(storageKey(file)) !== value)
                localStorage.setItem(storageKey(file), value);
        } catch { /* best effort: an unavailable store never breaks wallpaper */ }
    }

    function detect(image, file, manual) {
        const profile = window.OpsisSceneSkyProfiles?.[file];
        if (profile === null) return null;
        const canvas = document.createElement('canvas');
        canvas.width = 512;
        // Manual edits originate from the actual photograph ratio and intentionally
        // override older generated catalogue dimensions for this single image.
        canvas.height = manual?.height !== undefined && Number.isInteger(manual.height) &&
            manual.height >= 1 && manual.height <= 512 ? manual.height : (profile?.[0] ??
            Math.max(1, Math.min(512, Math.round(canvas.width * image.naturalHeight / image.naturalWidth))));
        const g = canvas.getContext('2d', { willReadFrequently: !profile });
        let hash = null;
        try { hash = fingerprint(image); } catch { /* tainted/undecodable image */ }
        const edited = manual && manual.width === canvas.width && manual.height === canvas.height &&
            Array.isArray(manual.rows) && manual.rows.length === canvas.width &&
            manual.rows.every(n => Number.isInteger(n) && n >= 0 && n <= canvas.height)
            ? manual.rows : null;
        const saved = hash ? loadSaved(file, hash, canvas.width, canvas.height) : null;
        let rows = edited || saved || profile?.[1];
        if (!rows) {
            g.drawImage(image, 0, 0, canvas.width, canvas.height);
            rows = skyline(g.getImageData(0, 0, canvas.width, canvas.height).data, canvas.width, canvas.height);
            // Skip isolated patches to avoid puncturing photographs with little clear sky.
            if (rows.filter(y => y > 0).length < rows.length * .25) return null;
        }
        const mask = g.createImageData(canvas.width, canvas.height);
        for (let x = 0; x < rows.length; x++) {
            for (let y = 0; y < rows[x]; y++) {
                // Feather into the sky, keeping the terrain side fully opaque.
                mask.data[(y * canvas.width + x) * 4 + 3] =
                    Math.round(255 * Math.min(1, (rows[x] - y) / 5));
            }
        }
        g.putImageData(mask, 0, 0);
        canvas.opsisRows = rows; // only used when the user enables the skyline overlay
        // Persist the image-specific sky profile only after successful detection.
        if (hash && !saved && !edited) saveMask(file, hash, canvas.width, canvas.height, rows);
        return canvas;
    }

    // Refine catalogue outlines at photo resolution. The coarse profile narrows
    // the search so blue sea and white facades cannot become a second sky.
    function refinePhotoSky(image, coarse) {
        if (!coarse?.opsisRows) return coarse;
        const c=document.createElement('canvas');
        c.width=Math.min(image.naturalWidth,1600);
        c.height=Math.round(image.naturalHeight*c.width/image.naturalWidth);
        const g=c.getContext('2d',{willReadFrequently:true});
        g.drawImage(image,0,0,c.width,c.height);
        const rgb=g.getImageData(0,0,c.width,c.height).data;
        const mask=g.createImageData(c.width,c.height);
        const scale=c.height/coarse.height;
        const edges=new Uint16Array(c.width);
        for(let x=0;x<c.width;x++) {
            const prior=coarse.opsisRows[Math.floor(x*coarse.width/c.width)];
            if (!prior) continue;
            const top=Math.max(3,Math.round((prior-3)*scale));
            const limit=Math.min(c.height-3,Math.round((prior+10)*scale));
            let edge=Math.round(prior*scale), best=3;
            for(let y=top;y<=limit;y++) {
                const above=((y-2)*c.width+x)*4, below=((y+1)*c.width+x)*4;
                // Daylight skies are brighter than the distant skyline beneath them.
                const drop=(rgb[above]-rgb[below])*.22+(rgb[above+1]-rgb[below+1])*.54+(rgb[above+2]-rgb[below+2])*.24;
                const score=drop-.18*Math.abs(y-prior*scale);
                if(score>best) { edge=y; best=score; }
            }
            edges[x]=edge;
        }
        for(let x=0;x<c.width;x++) {
            if (!edges[x]) continue;
            const nearby=edges.slice(Math.max(0,x-4),Math.min(c.width,x+5)).sort();
            const edge=nearby[Math.floor(nearby.length/2)];
            for(let y=0;y<edge;y++) mask.data[(y*c.width+x)*4+3]=255;
            mask.data[(edge*c.width+x)*4+3]=128;
        }
        g.putImageData(mask,0,0);
        return c;
    }

    function outline(g, mask, bounds) {
        if (!mask?.opsisRows || !bounds) return;
        const rows = mask.opsisRows;
        g.save();
        g.lineWidth = 2;
        g.strokeStyle = 'rgba(255,196,40,.95)';
        g.setLineDash([6, 3]);
        g.beginPath();
        let active = false;
        for (let x = 0; x < rows.length; x++) {
            if (!rows[x]) { active = false; continue; }
            const px = bounds.x + x / rows.length * bounds.width;
            const py = bounds.y + rows[x] / mask.height * bounds.height;
            if (!active) { g.moveTo(px, py); active = true; } else g.lineTo(px, py);
        }
        g.stroke();
        g.restore();
    }

    if (typeof module !== 'undefined') module.exports = { skyline };
    else window.OpsisSkyMask = { detect, outline, refinePhotoSky };
})();
