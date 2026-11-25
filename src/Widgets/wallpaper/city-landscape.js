(() => {
    const geometry = new Map();
    const profiles = {
        coast: { skyline: .82, ridge: .56, stone: [146, 124, 109], roof: [117, 76, 64] },
        lake: { skyline: .60, ridge: .85, stone: [140, 128, 105], roof: [90, 65, 58] },
        river: { skyline: .64, ridge: .74, stone: [155, 137, 115], roof: [97, 61, 52] },
        hill: { skyline: .65, ridge: 1.12, stone: [163, 137, 112], roof: [116, 70, 57] },
        plain: { skyline: .90, ridge: .35, stone: [159, 142, 115], roof: [123, 81, 59] }
    };

    function layout(city) {
        if (geometry.has(city.id)) return geometry.get(city.id);
        let seed = 2166136261;
        for (const ch of city.id) seed = Math.imul(seed ^ ch.charCodeAt(0), 16777619);
        const random = () => ((seed = Math.imul(seed, 1664525) + 1013904223) >>> 0) / 4294967296;
        const entries = [];
        const { ridge: bend, skyline } = profiles[city.terrain];
        const center = .34 + random() * .32;
        const riverMouth = .23 + random() * .53;
        for (let i = 0; i < 45; i++) {
            const x = (i + random() * .35) / 45;
            const width = .011 + random() * .026;
            const roof = random() > .35;
            const elevation = Math.sin(x * Math.PI) * .16 * bend;
            if (city.terrain === 'river' && Math.abs((1 - skyline) / 2 + x * skyline - riverMouth) < .035) continue;
            entries.push({ x, width, roof, elevation, height: .12 + random() * .52, lights: random() > .24 });
        }
        const result = { entries, center, riverMouth, direction: random() < .5 ? -1 : 1 };
        geometry.set(city.id, result);
        return result;
    }

    function shade(color, pal, lighten = 0) {
        const light = .33 + .67 * pal.amb;
        const mist = .12 + pal.fog * .65;
        const painted = color.map((channel, i) => Math.round(
            (channel * light + lighten) * (1 - mist) + pal.haze[i] * mist));
        return `rgb(${painted.join(',')})`;
    }

    function roof(g, x, y, w, h) {
        g.beginPath();
        g.moveTo(x, y);
        g.lineTo(x + w / 2, y - h);
        g.lineTo(x + w, y);
        g.closePath();
        g.fill();
    }

    function drawMotif(g, motif, x, base, size, pal, color) {
        g.save();
        g.translate(x, base);
        g.scale(size, size);
        g.fillStyle = shade(color, pal, -18);
        if (motif === 'bridge') {
            g.fillRect(-1.6, -.43, 3.2, .15);
            g.fillRect(-1.6, -.30, .19, .31);
            g.fillRect(1.41, -.30, .19, .31);
            g.strokeStyle = g.fillStyle;
            g.lineWidth = .10;
            g.beginPath();
            g.moveTo(-1.42, -.01);
            g.quadraticCurveTo(-1, -.60, -.54, -.01);
            g.moveTo(-.54, -.01);
            g.quadraticCurveTo(0, -.85, .54, -.01);
            g.moveTo(.54, -.01);
            g.quadraticCurveTo(1, -.60, 1.42, -.01);
            g.stroke();
        } else if (motif === 'fort' || motif === 'theater' || motif === 'temple') {
            const w = 2.3;
            g.fillRect(-w / 2, -.36, w, .36);
            for (let i = 0; i < 6; i++) g.fillRect(-w / 2 + i * .40, -.60, .18, .28);
            if (motif === 'temple') roof(g, -1.26, -.57, 2.52, .29);
        } else if (motif === 'lighthouse' || motif === 'tower' || motif === 'roundtower') {
            const width = motif === 'roundtower' ? .62 : .32;
            g.fillRect(-width / 2, -.98, width, .98);
            g.fillRect(-width * .60, -1.13, width * 1.20, .19);
            roof(g, -width * .65, -1.12, width * 1.30, .18);
            if (motif === 'lighthouse' && pal.night > .1) {
                g.fillStyle = `rgba(255,216,143,${pal.night})`;
                g.fillRect(-width * .24, -1.10, width * .48, .13);
            }
        } else if (motif === 'harbor') {
            g.fillRect(-1.45, -.16, 2.9, .15);
            for (let i = 0; i < 7; i++) g.fillRect(-1.4 + i * .44, -.30, .10, .24);
        } else if (motif === 'falls') {
            g.fillStyle = shade([176, 198, 210], pal, 10);
            g.fillRect(-.38, -.79, .11, .78);
            g.fillRect(-.15, -.67, .13, .66);
            g.fillRect(.07, -.75, .08, .74);
        }
        g.restore();
    }

    function draw(g, city, width, horizon, pal) {
        const profile = profiles[city.terrain];
        const scene = layout(city);
        const scale = Math.max(10, Math.min(horizon * .12, width * .055));
        const color = shade(profile.stone, pal);
        const roofColor = shade(profile.roof, pal, -12);
        const span = width * profile.skyline;
        const offset = (width - span) / 2;
        g.save();
        for (const building of scene.entries) {
            const x = offset + building.x * span;
            const w = building.width * span;
            const h = scale * building.height;
            const base = horizon - scale * building.elevation + 1;
            g.fillStyle = color;
            g.fillRect(x, base - h, w, horizon - base + h + 1);
            if (building.roof) {
                g.fillStyle = roofColor;
                roof(g, x - 1, base - h + 1, w + 2, Math.min(w * .24, scale * .16));
            }
            if (pal.night > .15 && building.lights && w > 5 && h > 5) {
                g.fillStyle = `rgba(255,218,156,${pal.night * (1 - pal.fog * .65)})`;
                g.fillRect(x + w * .27, base - h * .62, Math.max(1, w * .15), Math.max(1, h * .13));
            }
        }
        const x = width * (city.terrain === 'river' && city.motif === 'bridge' ? scene.riverMouth : scene.center);
        drawMotif(g, city.motif, x, horizon + 1, scale * .72, pal, profile.stone);
        g.restore();
    }

    function drawRiver(g, city, width, height, horizon, horizonColor) {
        const scene = layout(city);
        const origin = width * scene.riverMouth;
        const end = origin + scene.direction * width * .18;
        const gradient = g.createLinearGradient(0, horizon, 0, height);
        gradient.addColorStop(0, `rgb(${horizonColor.map(value => Math.round(value * .71)).join(',')})`);
        gradient.addColorStop(1, '#182f3e');
        g.fillStyle = gradient;
        g.beginPath();
        g.moveTo(origin - width * .012, horizon);
        g.lineTo(origin + width * .012, horizon);
        g.bezierCurveTo(origin - width * .01, height * .79, end + width * .07, height * .86, end + width * .14, height);
        g.lineTo(end - width * .12, height);
        g.bezierCurveTo(end - width * .04, height * .88, origin + width * .03, height * .79, origin - width * .012, horizon);
        g.closePath();
        g.fill();
    }
    window.OpsisCityLandscape = { draw, drawRiver };
})();
