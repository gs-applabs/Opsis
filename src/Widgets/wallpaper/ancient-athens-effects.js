// Integrated Ancient Athens atmospheric effects. No external downloads or extra wallpaper/plugin.
(() => {
    const clamp = (n, lo = 0, hi = 1) => Math.max(lo, Math.min(hi, n));
    const smooth = n => { n = clamp(n); return n * n * (3 - 2 * n); };
    const TAU = Math.PI * 2;
    const current = { cloud: .12, rain: 0, snow: 0, fog: 0, storm: 0, wind: 7 };
    const target = { ...current };
    const scenarios = {
        0: [.04,0,0,0,0], 1: [.2,0,0,0,0], 2: [.55,0,0,0,0],
        3: [.94,0,0,0,0], 45: [.65,0,0,.82,0], 48: [.7,0,0,.92,0],
        51: [.8,.2,0,0,0], 53: [.9,.38,0,0,0], 55: [.95,.54,0,0,0],
        56: [.85,.3,0,0,0], 57: [.95,.52,0,0,0],
        61: [.85,.45,0,0,0], 63: [.96,.72,0,0,0], 65: [1,1,0,.05,0],
        66: [.94,.48,0,0,0], 67: [1,.8,0,0,0],
        71: [.8,0,.38,0,0], 73: [.9,0,.68,0,0], 75: [1,0,1,.08,0],
        77: [.85,0,.4,0,0], 80: [.8,.6,0,0,0], 81: [.9,.83,0,0,0],
        82: [1,1,0,.08,0], 85: [.87,0,.66,0,0], 86: [1,0,1,.1,0],
        95: [1,.78,0,0,.75], 96: [1,.95,0,0,1], 99: [1,1,0,0,1]
    };
    let lastWeatherSource = 'fallback', lastTick = 0;
    const hash = n => { const f = Math.sin(n * 127.1 + 78.233) * 43758.5453; return f - Math.floor(f); };
    function setWeather(observation) {
        if (!observation || observation.ok !== true) return false;
        const code = Number(observation.code);
        const values = scenarios[code] || [0.3,0,0,0,0];
        [target.cloud,target.rain,target.snow,target.fog,target.storm] = values;
        if (Number.isFinite(observation.cloud)) target.cloud = Math.max(target.rain * .82, target.snow * .82, clamp(observation.cloud / 100));
        target.wind = clamp(Number(observation.wind) || 0, 0, 120);
        lastWeatherSource = observation.source || 'weather-cache';
        return true;
    }
    function step(seconds) {
        const fraction = 1 - Math.exp(-clamp(seconds, 0, 1) * .65);
        for (const key of Object.keys(current)) current[key] += (target[key] - current[key]) * fraction;
    }
    // The photographic sky contains hills, statues and roofs: prevent astronomical
    // sprites, birds and cloud ellipses from drawing across the Acropolis.
    function clipSky(g, w, h) {
        g.beginPath(); g.moveTo(0,0); g.lineTo(w,0);
        [[1,.22],[.90,.20],[.76,.20],[.69,.13],[.62,.12],
         [.54,.125],[.48,.14],[.38,.15],[.29,.20],[.18,.20],[0,.18]]
            .forEach(([x,y]) => g.lineTo(x*w, y*h));
        g.closePath(); g.clip();
    }
    function sunMoon(g, w, h, day, minutes, sr, ss, manual) {
        const daylight = manual === 'Day' || manual === 'Golden' ||
            (manual === 'Auto' && minutes >= sr && minutes <= ss);
        const twilight = manual === 'Golden' || manual === 'Blue' ||
            (manual === 'Auto' && (Math.abs(minutes-sr)<42 || Math.abs(minutes-ss)<42));
        const t = clamp((minutes-sr) / Math.max(1,ss-sr));
        const size = Math.min(w,h);
        const sunX = (.15 + .12 * smooth(t)) * w;
        const sunY = (.15 - .08 * Math.sin(t*Math.PI)) * h;
        if (daylight) {
            const x=sunX,y=sunY,r=size*.013;
            const visibility=clamp(1-current.cloud*.76-current.rain*.22-current.fog*.75);
            if (visibility > .025) {
                const halo=g.createRadialGradient(x,y,r*.25,x,y,r*7.8);
                halo.addColorStop(0,`rgba(255,244,213,${visibility*.57})`);
                halo.addColorStop(.17,`rgba(255,215,148,${visibility*.19})`);
                halo.addColorStop(1,'rgba(255,190,127,0)');
                g.fillStyle=halo; g.fillRect(x-r*8,y-r*8,r*16,r*16);
                g.fillStyle=`rgba(255,249,226,${visibility*.85})`;
                g.beginPath();g.arc(x,y,r,0,TAU);g.fill();
            }
        } else {
            const x=w*.83,y=h*(twilight?.097:.105),r=size*.012;
            const visibility=clamp(.83-current.cloud*.83-current.fog*.76);
            if (visibility>.025) {
                const light=g.createRadialGradient(x-r*.3,y-r*.3,0,x,y,r*4.4);
                light.addColorStop(0,`rgba(236,241,254,${visibility*.54})`);
                light.addColorStop(.18,`rgba(211,223,253,${visibility*.22})`);
                light.addColorStop(1,'rgba(190,206,255,0)');
                g.fillStyle=light;g.fillRect(x-r*5,y-r*5,r*10,r*10);
                g.fillStyle=`rgba(231,235,244,${visibility})`;
                g.beginPath();g.arc(x,y,r,0,TAU);g.fill();
                // Static photometric moon phase, not a second sun-like white disc.
                const lunarDays=(day.getTime()-Date.UTC(2000,0,6,18,14))/86400000;
                const p=((lunarDays/29.530588853)%1+1)%1;
                const shadow=Math.abs(p-.5)*2;
                g.save();g.beginPath();g.arc(x,y,r,0,TAU);g.clip();
                g.fillStyle=`rgba(9,17,34,${visibility*.82})`;
                g.beginPath();g.ellipse(x+(p<.5?-1:1)*r*(1-shadow),y,r*shadow,r,0,0,TAU);g.fill();
                g.restore();
            }
        }
    }
    function drawClouds(g,w,h,seconds,daytime) {
        if (current.cloud < .05) return;
        const count=Math.ceil(2+current.cloud*7);
        const speed=(4+current.wind*.16)*Math.min(1,w/1920);
        for (let i=0;i<count;i++) {
            const cx=((hash(i*2+99)*w+seconds*speed*(.7+hash(i*33)))%(w+w*.32))-w*.16;
            const cy=h*(.035+hash(i*5+14)*.16);
            const rx=w*(.06+.09*hash(i*19+31));
            const ry=h*(.018+.022*hash(i*24));
            const color=daytime?'232,237,238':'119,133,166';
            const fade=current.cloud*(.13+.14*hash(i*6));
            const gr=g.createRadialGradient(cx,cy,rx*.06,cx,cy,rx);
            gr.addColorStop(0,`rgba(${color},${fade})`);
            gr.addColorStop(1,`rgba(${color},0)`);
            g.save();g.translate(cx,cy);g.scale(1,ry/rx);g.fillStyle=gr;
            g.beginPath();g.arc(0,0,rx,0,TAU);g.fill();g.restore();
        }
    }
    function birds(g,w,h,seconds,daytime,reduced) {
        if (!daytime || current.cloud>.72 || current.rain>.12 || current.snow>.12 || current.storm>.08) return;
        const count=9, velocity=(.008+current.wind*.00013)*w;
        g.lineWidth=Math.max(.7,Math.min(1.75,w/1600));
        for(let i=0;i<count;i++) {
            const x=((hash(i*5+6)*w+seconds*velocity*(.7+hash(i*9)))%(w*1.18))-w*.09;
            const y=h*(.052+hash(i*7+2)*.105)+(reduced?0:Math.sin(seconds*.65+i)*h*.002);
            const wing=w*(.0022+.0013*hash(i*13));
            g.strokeStyle=`rgba(25,39,50,${.26+hash(i*17)*.25})`;
            g.beginPath();g.moveTo(x-wing,y-wing*.25);g.quadraticCurveTo(x-wing*.28,y+wing*.25,x,y);
            g.quadraticCurveTo(x+wing*.28,y+wing*.25,x+wing,y-wing*.25);g.stroke();
        }
    }
    function rainSnow(g,w,h,seconds,lowPower) {
        const rain=clamp(current.rain),snow=clamp(current.snow);
        if(rain<.025 && snow<.025)return;
        const base=lowPower?95:240;
        if (rain>.025) {
            const n=Math.round(base*rain),velocity=450+current.wind*8;
            g.strokeStyle=`rgba(195,215,234,${.12+rain*.2})`;
            g.lineWidth=Math.max(.7,w/2100);
            g.beginPath();
            for(let i=0;i<n;i++){
                const x=((hash(i*9+12)*w+seconds*(current.wind-12)*1.7)%(w+30)+w+30)%(w+30);
                const y=((hash(i*7+56)*h+seconds*velocity*(.5+hash(i*4)))%(h+40));
                const len=(8+hash(i*2)*19)*Math.max(.6,w/1920);
                g.moveTo(x,y);g.lineTo(x-current.wind*.035*len,y-len);
            }g.stroke();
        }
        if(snow>.025){
            const n=Math.round(base*.6*snow);
            g.fillStyle=`rgba(241,245,255,${.28+snow*.4})`;
            for(let i=0;i<n;i++){
                const x=((hash(i*14+37)*w+seconds*(6+current.wind*.3))%w+w)%w;
                const y=((hash(i*21+29)*h+seconds*(22+hash(i*8)*35))%h+h)%h;
                const r=Math.max(.7,Math.min(w,h)*(.00065+.0011*hash(i*3)));
                g.beginPath();g.arc(x,y,r,0,TAU);g.fill();
            }
        }
    }
    function fog(g,w,h,seconds) {
        if(current.fog<.025)return;
        g.save();
        const mist=g.createLinearGradient(0,h*.16,0,h*.92);
        mist.addColorStop(0,'rgba(195,208,221,0)');
        mist.addColorStop(.38,`rgba(174,193,205,${current.fog*.42})`);
        mist.addColorStop(1,`rgba(170,185,199,${current.fog*.12})`);
        g.fillStyle=mist;g.fillRect(0,h*.16,w,h*.76);
        for(let i=0;i<4;i++) {
            const x=(hash(i*34)*w+seconds*(2+current.wind*.15))%(w*1.3)-w*.15;
            const y=h*(.28+i*.11);
            const radius=w*.3;
            const gr=g.createRadialGradient(x,y,0,x,y,radius);
            gr.addColorStop(0,`rgba(222,229,233,${current.fog*.13})`);
            gr.addColorStop(1,'rgba(220,230,241,0)');
            g.fillStyle=gr;g.fillRect(x-radius,y-radius,radius*2,radius*2);
        }
        g.restore();
    }
    function render(g,w,h,{date=new Date(),sr=390,ss=1170,minutes=720,selection='Auto',
                           reducedMotion=false,lowPower=false,delta=0.04}={}) {
        step(delta);
        const seconds=(reducedMotion?0:date.getTime()/1000);
        const daylight=selection==='Day' || selection==='Golden' ||
            (selection==='Auto' && minutes>sr+7 && minutes<ss-7);
        g.save();g.setTransform(1,0,0,1,0,0);
        g.save();clipSky(g,w,h);
        sunMoon(g,w,h,date,minutes,sr,ss,selection);
        drawClouds(g,w,h,seconds,daylight);
        birds(g,w,h,seconds,daylight,reducedMotion);
        g.restore();
        fog(g,w,h,seconds);
        rainSnow(g,w,h,seconds,lowPower);
        if(current.storm>.2 && !reducedMotion){
            const phase=(date.getTime()%17300)/17300;
            if(phase>.777 && phase<.788){
                g.fillStyle=`rgba(213,229,252,${current.storm*.24*smooth((phase-.777)/.011)})`;
                g.fillRect(0,0,w,h);
            }
        }
        g.restore();
        lastTick=date.getTime();
    }
    function clear() { lastTick=0; Object.assign(current,{cloud:.12,rain:0,snow:0,fog:0,storm:0,wind:7}); Object.assign(target,current); }
    window.OpsisAncientAthensFx={setWeather,render,clear,_state:()=>({current:{...current},target:{...target},lastTick,source:lastWeatherSource})};
})();
