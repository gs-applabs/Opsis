(() => {
    const clamp = (value, min = 0, max = 1) => Math.max(min, Math.min(max, value));
    const rgb = (channels, alpha = 1) => `rgba(${channels.map(value => Math.round(clamp(value, 0, 255))).join(',')},${clamp(alpha)})`;

    // Camera-style framing. Extra zoom on narrow displays covers the ground below the sky.
    function photoBounds(width, height, horizon, imageWidth, imageHeight) {
        const coverage = height - horizon + height * .18;
        const zoom = Math.max(width / imageWidth, coverage / imageHeight);
        const photoWidth = Math.ceil(imageWidth * zoom);
        const photoHeight = Math.ceil(imageHeight * zoom);
        return { x: Math.round((width - photoWidth) / 2), y: height - photoHeight, width: photoWidth, height: photoHeight };
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

    window.OpsisWeatherAtmosphere = { photoBounds, photoGrade, drawSky, drawDepth };
})();
