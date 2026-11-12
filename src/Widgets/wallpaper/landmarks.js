// Landmark silhouettes for the weather wallpaper (plan 2.1.5): the skyline of the place where the computer is (the weather location) gets its best-known landmark on the horizon.
// All shapes are drawn here as simple polygons (own work, nothing downloaded, no model licences): x in units of the landmark height around 0, y up from the horizon, 0..1.
// pickLandmark(lat, lon) chooses the nearest one within maxKm; drawLandmark paints it in the colour of the distant hills so it follows the time of day, fog and snow of the scene.
(function (root) {
    const L = (id, name, lat, lon, polys) => ({ id, name, lat, lon, polys });
    const rect = (x0, y0, x1, y1) => [[x0, y0], [x1, y0], [x1, y1], [x0, y1]];

    const LANDMARKS = [
        L('parthenon', 'Parthenon, Athens', 37.9715, 23.7267, [rect(-0.75, 0, 0.75, 0.07), rect(-0.68, 0.07, 0.68, 0.14), ...Array.from({ length: 8 }, (_, i) => rect(-0.62 + i * 0.18, 0.14, -0.55 + i * 0.18, 0.62)), rect(-0.66, 0.62, 0.66, 0.7), [[-0.7, 0.7], [0.7, 0.7], [0, 1]]]),
        L('whitetower', 'White Tower, Thessaloniki', 40.6264, 22.9484, [rect(-0.3, 0, 0.3, 0.9), rect(-0.34, 0.85, 0.34, 0.97), [[-0.34, 0.97], [0.34, 0.97], [0.26, 1.0], [-0.26, 1.0]]]),
        L('eiffel', 'Eiffel Tower, Paris', 48.8584, 2.2945, [[[0, 1.0], [0.03, 0.9], [0.07, 0.62], [0.17, 0.36], [0.28, 0.16], [0.42, 0.0], [0.28, 0.0], [0.17, 0.1], [0.0, 0.2], [-0.17, 0.1], [-0.28, 0.0], [-0.42, 0.0], [-0.28, 0.16], [-0.17, 0.36], [-0.07, 0.62], [-0.03, 0.9]]]),
        L('bigben', 'Big Ben, London', 51.5007, -0.1246, [rect(-0.07, 0, 0.07, 0.62), rect(-0.09, 0.62, 0.09, 0.78), [[-0.09, 0.78], [0.09, 0.78], [0, 1.0]], rect(-0.45, 0, -0.07, 0.3)]),
        L('liberty', 'Statue of Liberty, New York', 40.6892, -74.0445, [rect(-0.2, 0, 0.2, 0.22), [[-0.12, 0.22], [0.12, 0.22], [0.08, 0.7], [-0.08, 0.7]], [[-0.07, 0.7], [0.07, 0.7], [0.0, 0.8]], [[0.04, 0.72], [0.14, 1.0], [0.18, 0.98], [0.09, 0.7]], [[-0.07, 0.8], [-0.16, 0.84], [-0.14, 0.72]]]),
        L('colosseum', 'Colosseum, Rome', 41.8902, 12.4922, [rect(-0.95, 0, 0.95, 0.05), ...[0, 1, 2].flatMap(level => Array.from({ length: 11 }, (_, i) => rect(-0.9 + i * 0.18, 0.05 + level * 0.17, -0.82 + i * 0.18, 0.2 + level * 0.17))), rect(-0.95, 0.54, 0.35, 0.6), rect(-0.95, 0.2, 0.95, 0.22), rect(-0.95, 0.37, 0.95, 0.39)]),
        L('pisa', 'Leaning Tower, Pisa', 43.723, 10.3966, [[[-0.08, 0], [0.1, 0], [0.19, 0.95], [0.01, 0.97]], rect(-0.3, 0, 0.3, 0.05)]),
        L('sagrada', 'Sagrada Família, Barcelona', 41.4036, 2.1744, [rect(-0.4, 0, 0.4, 0.4), ...[-0.3, -0.1, 0.1, 0.3].map((x, i) => [[x - 0.05, 0.4], [x + 0.05, 0.4], [x + 0.03, 0.8 + (i % 2) * 0.1], [x, 1.0 - (i % 2) * 0.05], [x - 0.03, 0.8 + (i % 2) * 0.1]])]),
        L('brandenburg', 'Brandenburg Gate, Berlin', 52.5163, 13.3777, [rect(-0.7, 0.6, 0.7, 0.72), ...Array.from({ length: 6 }, (_, i) => rect(-0.62 + i * 0.25, 0, -0.52 + i * 0.25, 0.6)), rect(-0.2, 0.72, 0.2, 0.9), [[-0.12, 0.9], [0.12, 0.9], [0.05, 1.0], [-0.05, 1.0]]]),
        L('hagia', 'Hagia Sophia, Istanbul', 41.0086, 28.98, [[[-0.7, 0], [0.7, 0], [0.7, 0.32], [0.45, 0.4], [0.3, 0.62], [0.12, 0.8], [0, 0.85], [-0.12, 0.8], [-0.3, 0.62], [-0.45, 0.4], [-0.7, 0.32]], rect(-0.9, 0, -0.8, 0.8), [[-0.9, 0.8], [-0.8, 0.8], [-0.85, 0.95]], rect(0.8, 0, 0.9, 0.8), [[0.8, 0.8], [0.9, 0.8], [0.85, 0.95]]]),
        L('pyramids', 'Pyramids, Giza', 29.9792, 31.1342, [[[-1.0, 0], [-0.12, 0], [-0.56, 0.62]], [[-0.3, 0], [0.9, 0], [0.3, 1.0]], [[0.7, 0], [1.4, 0], [1.05, 0.42]]]),
        L('burj', 'Burj Khalifa, Dubai', 25.1972, 55.2744, [[[-0.02, 1.0], [0.0, 1.0], [0.02, 0.8], [0.05, 0.55], [0.09, 0.3], [0.14, 0.0], [-0.14, 0.0], [-0.09, 0.3], [-0.05, 0.55], [-0.02, 0.8]]]),
        L('taj', 'Taj Mahal, Agra', 27.1751, 78.0421, [rect(-0.5, 0, 0.5, 0.3), [[-0.3, 0.3], [0.3, 0.3], [0.28, 0.5], [0.15, 0.78], [0, 0.88], [-0.15, 0.78], [-0.28, 0.5]], [[-0.02, 0.88], [0.02, 0.88], [0, 1.0]], ...[-0.7, 0.7].map(x => rect(x - 0.05, 0, x + 0.05, 0.75)), ...[-0.7, 0.7].map(x => [[x - 0.07, 0.75], [x + 0.07, 0.75], [x, 0.9]])]),
        L('tokyotower', 'Tokyo Tower', 35.6586, 139.7454, [[[0, 1.0], [0.015, 0.8], [0.06, 0.5], [0.14, 0.22], [0.24, 0.0], [0.15, 0.0], [0.0, 0.1], [-0.15, 0.0], [-0.24, 0.0], [-0.14, 0.22], [-0.06, 0.5], [-0.015, 0.8]], rect(-0.1, 0.45, 0.1, 0.5)]),
        L('opera', 'Sydney Opera House', -33.8568, 151.2153, [[[-0.9, 0], [-0.3, 0], [-0.55, 0.55], [-0.9, 0.3]], [[-0.4, 0], [0.2, 0], [0.0, 0.8], [-0.4, 0.5]], [[0.1, 0], [0.7, 0], [0.55, 0.65], [0.3, 0.9]], rect(-1.0, 0, 1.0, 0.04)]),
        L('goldengate', 'Golden Gate Bridge, San Francisco', 37.8199, -122.4783, [rect(-1.0, 0.18, 1.0, 0.22), rect(-0.5, 0, -0.44, 0.95), rect(0.44, 0, 0.5, 0.95), [[-1.0, 0.25], [-0.47, 0.95], [-0.47, 0.9], [-0.96, 0.22]], [[1.0, 0.25], [0.47, 0.95], [0.47, 0.9], [0.96, 0.22]], [[-0.47, 0.95], [0.47, 0.95], [0.47, 0.9], [0, 0.3], [-0.47, 0.9]]]),
        L('christ', 'Christ the Redeemer, Rio de Janeiro', -22.9519, -43.2105, [rect(-0.06, 0, 0.06, 0.55), rect(-0.5, 0.52, 0.5, 0.6), rect(-0.07, 0.6, 0.07, 0.9), [[-0.05, 0.9], [0.05, 0.9], [0, 1.0]], [[-0.25, 0], [0.25, 0], [0.1, 0.2], [-0.1, 0.2]]]),
        L('matterhorn', 'Matterhorn', 45.9766, 7.6585, [[[-1.2, 0], [-0.3, 0.7], [-0.15, 0.78], [0.0, 1.0], [0.12, 0.82], [0.3, 0.6], [1.2, 0]]]),
        L('fuji', 'Mount Fuji', 35.3606, 138.7274, [[[-1.4, 0], [-0.25, 0.9], [-0.12, 1.0], [0.12, 1.0], [0.25, 0.9], [1.4, 0]]]),
        L('cntower', 'CN Tower, Toronto', 43.6426, -79.3871, [[[-0.015, 1.0], [0.015, 1.0], [0.02, 0.62], [0.05, 0.58], [0.12, 0.56], [0.12, 0.5], [0.05, 0.47], [0.03, 0.4], [0.05, 0.0], [-0.05, 0.0], [-0.03, 0.4], [-0.05, 0.47], [-0.12, 0.5], [-0.12, 0.56], [-0.05, 0.58], [-0.02, 0.62]]]),
        L('petronas', 'Petronas Towers, Kuala Lumpur', 3.1579, 101.7116, [...[-0.18, 0.18].map(x => [[x - 0.1, 0], [x + 0.1, 0], [x + 0.08, 0.75], [x + 0.03, 0.9], [x, 1.0], [x - 0.03, 0.9], [x - 0.08, 0.75]]), rect(-0.1, 0.5, 0.1, 0.56)]),
        L('lisbon', 'Belém Tower, Lisbon', 38.6916, -9.2160, [rect(-0.3, 0, 0.3, 0.55), rect(-0.34, 0.5, 0.34, 0.6), ...[-0.3, 0.3].map(x => [[x - 0.07, 0.6], [x + 0.07, 0.6], [x, 0.78]]), rect(-0.18, 0.55, 0.18, 0.85), [[-0.18, 0.85], [0.18, 0.85], [0, 1.0]]]),
        L('cyprus', 'Kyrenia Castle, Cyprus', 35.3427, 33.3192, [rect(-0.8, 0, 0.8, 0.45), ...[-0.7, -0.2, 0.3, 0.7].map(x => rect(x - 0.1, 0.45, x + 0.1, 0.9))]),
    ];

    function distKm(lat1, lon1, lat2, lon2) {
        const r = Math.PI / 180, dLat = (lat2 - lat1) * r, dLon = (lon2 - lon1) * r;
        const a = Math.sin(dLat / 2) ** 2 + Math.cos(lat1 * r) * Math.cos(lat2 * r) * Math.sin(dLon / 2) ** 2;
        return 6371 * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    }

    // nearest landmark within maxKm of the given position; null when nothing is near (the plain hills stay)
    function pickLandmark(lat, lon, maxKm = 250) {
        if (typeof lat !== 'number' || typeof lon !== 'number' || isNaN(lat) || isNaN(lon)) return null;
        let best = null, bd = Infinity;
        for (const m of LANDMARKS) { const d = distKm(lat, lon, m.lat, m.lon); if (d < bd) { bd = d; best = m; } }
        return best && bd <= maxKm ? { id: best.id, name: best.name, km: Math.round(bd) } : null;
    }

    // paints landmark `id` standing on the horizon at x = cx, `h` pixels tall, in colour `fill` (a CSS colour, already faded for distance / fog / night)
    function drawLandmark(g, id, cx, baseY, h, fill) {
        const m = LANDMARKS.find(x => x.id === id); if (!m) return false;
        g.save(); g.fillStyle = fill;
        for (const poly of m.polys) {
            g.beginPath();
            poly.forEach(([x, y], i) => { const px = cx + x * h, py = baseY - y * h; if (i === 0) g.moveTo(px, py); else g.lineTo(px, py); });
            g.closePath(); g.fill();
        }
        g.restore(); return true;
    }

    root.OpsisLandmarks = { LANDMARKS, pickLandmark, drawLandmark, distKm };
    if (typeof module !== 'undefined') module.exports = root.OpsisLandmarks;
})(typeof window !== 'undefined' ? window : globalThis);
