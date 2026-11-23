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

    function detect(image, file) {
        const profile = window.OpsisSceneSkyProfiles?.[file];
        if (profile === null) return null;
        const canvas = document.createElement('canvas');
        canvas.width = 512;
        canvas.height = profile?.[0] ??
            Math.max(1, Math.min(512, Math.round(canvas.width * image.naturalHeight / image.naturalWidth)));
        const g = canvas.getContext('2d', { willReadFrequently: !profile });
        let rows = profile?.[1];
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
        return canvas;
    }

    if (typeof module !== 'undefined') module.exports = { skyline };
    else window.OpsisSkyMask = { detect };
})();
