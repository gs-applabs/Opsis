(() => {
    const clamp = (value, min = 0, max = 1) => Math.max(min, Math.min(max, value));
    const rgb = (channels, alpha = 1) => `rgba(${channels.map(value => Math.round(clamp(value, 0, 255))).join(',')},${clamp(alpha)})`;

    // Camera-style framing. Extra zoom on narrow displays covers the ground below the sky.
    function photoBounds(width, height, horizon, imageWidth, imageHeight, focusX = .5) {
        const coverage = height - horizon + height * .18;
        const zoom = Math.max(width / imageWidth, coverage / imageHeight);
        const photoWidth = Math.ceil(imageWidth * zoom);
        const photoHeight = Math.ceil(imageHeight * zoom);
        // Portrait displays cut more from a landscape source: keep the user's subject visible.
        // Clamp so the final canvas is always fully covered (no black bars).
        const x = Math.round((width - photoWidth) * clamp(focusX));
        return { x, y: height - photoHeight, width: photoWidth, height: photoHeight };
    }

    function photoGrade(ambient, glow, weather) {
        const night = clamp(1 - ambient / .38);
        const day = Math.pow(clamp(ambient), .64);
        const storm = clamp(weather.storm + weather.rain * .43);
        return {
            exposure: .27 + day * .73 - storm * .15,
            saturation: .80 + day * .16 - storm * .23,
            contrast: 1.07 + day * .05,
            night,
            warmth: clamp(glow * (1 - ambient) * 1.5),
            haze: clamp(weather.fog * .66 + weather.rain * .12)
        };
    }

    function gradientVeil(g, width, top, bottom, color, stops) {
        const veil = g.createLinearGradient(0, top, 0, bottom);
        for (const [position, opacity] of stops) veil.addColorStop(position, rgb(color, opacity));
        g.fillStyle = veil;
        g.fillRect(0, top, width, bottom - top);
    }

    function drawSky(g, width, horizon, sky, graded, source, weather) {
        const day = clamp(sky.amb);
        const mist = clamp(.28 + weather.fog * .52 + weather.cloud * .15);
        g.save();
        gradientVeil(g, width, horizon * .36, horizon, graded.hor, [[0, 0], [.63, .12 * mist], [1, .63 * mist]]);
        if (source && source.power > .05) {
            const radius = width * (.24 + sky.glow * .18);
            const color = day > .24 ? [255, 224 - sky.glow * 35, 184 - sky.glow * 26] : [176, 198, 242];
            const light = g.createRadialGradient(source.x, source.y, 0, source.x, source.y, radius);
            const scatter = source.power * (1 - weather.cloud * .64) * (.11 + mist * .16);
            light.addColorStop(0, rgb(color, scatter));
            light.addColorStop(.26, rgb(color, scatter * .47));
            light.addColorStop(1, rgb(color, 0));
            g.globalCompositeOperation = 'screen';
            g.fillStyle = light;
            g.fillRect(0, 0, width, horizon);
        }
        g.restore();
    }

    function drawDepth(g, width, height, horizon, graded, weather, photograph) {
        const scatter = clamp(.11 + weather.fog * .32 + weather.rain * .15);
        const top = horizon - height * .18, bottom = Math.min(height, horizon + height * .28);
        g.save();
        gradientVeil(g, width, top, bottom, graded.hor, [[0, 0], [.51, scatter * (photograph ? 1 : .66)], [1, 0]]);
        if (weather.rain > .12) {
            const wetness = clamp(weather.rain * .17 + weather.storm * .10);
            gradientVeil(g, width, horizon, height, [18, 27, 41], [[0, 0], [1, wetness]]);
        }
        g.restore();
    }

    // Conservative water-only regions in source-image coordinates. Quays, buildings
    // and hills retain their photographic detail even on cropped portrait screens.
    const gulf = [[0,.45],[.99,.45],[.99,.474],[.86,.474],[.82,.496],[.57,.493],[.49,.521],[.32,.55],[.18,.59],[0,.645]];
    const waters = {
        patras: gulf, 'patras-harbor': gulf,
        whitetower: [[0,.54],[.30,.54],[.37,.56],[.60,.58],[.60,.73],[.47,.80],[.28,.86],[.07,.97],[0,.985]],
    };
    function drawPhotoWater(g, id, image, x, y, w, h, light, weather, seconds, high) {
        const shore = waters[id];
        if (!shore) return;
        const first = Math.min(...shore.map(p=>p[1])), last = Math.max(...shore.map(p=>p[1]));
        const rows = high ? 54 : 30;
        const wind = clamp(weather.wind / 45);
        g.save();
        g.translate(x,y);
        g.beginPath();
        shore.forEach(([u,v],i) => i ? g.lineTo(u*w,v*h) : g.moveTo(u*w,v*h));
        g.closePath(); g.clip();
        const sourceX = light ? (light.x-x)/w : .5;
        for (let i=0;i<rows;i++) {
            const depth=(i+.5)/rows, v=first+depth*(last-first);
            const band=h*(last-first)/rows;
            const phase=seconds*(.65+wind*.95)+i*.83;
            const drift=Math.sin(phase)*(.25+1.3*depth)*w/1280;
            g.globalAlpha=.48;
            g.drawImage(image,0,v*image.height,image.width,band*image.height/h,
                drift,v*h,w,band+.4);
            if (!light || light.power<=.03) continue;
            const glint=Math.max(0,Math.sin(i*19.3+seconds*.85))**3;
            const breadth=.017+depth*.065;
            const centre=sourceX+Math.sin(i*7.17+seconds*.35)*breadth;
            g.globalAlpha=glint*light.power*(1-weather.fog*.85)*.55;
            g.fillStyle=rgb(light.col);
            g.beginPath();
            g.ellipse(centre*w,v*h,(.002+.009*depth)*w,Math.max(.35,h*(.0004+.0005*depth)),0,0,Math.PI*2);
            g.fill();
        }
        g.restore();
    }

    window.OpsisWeatherAtmosphere = { photoBounds, photoGrade, drawSky, drawDepth, drawPhotoWater };
})();
