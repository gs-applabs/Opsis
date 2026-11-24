// Offline, stylised scene meshes. Coordinates are local art units, Y is up.
// Only the selected Gallery location is built and uploaded by weather-spatial.
(function (root) {
'use strict';
const limestone = [.79,.71,.57], marble = [.88,.84,.73], slate = [.27,.32,.34];
const roof = [.57,.28,.18], grass = [.36,.43,.26], water = [.13,.44,.53];
const tau = 2 * Math.PI;
function build(id, city, high) {
    const vertices = [];
    let seed = 2166136261;
    for (const c of id) seed = Math.imul(seed ^ c.charCodeAt(0), 16777619);
    function random() {
        seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
        return seed / 4294967296;
    }
    function tri(a,b,c,color) { vertices.push(...a,...color,...b,...color,...c,...color); }
    function quad(a,b,c,d,color) { tri(a,b,c,color); tri(a,c,d,color); }
    // Connect two horizontal rings. Offset centres also allow inclined piers and stays.
    function shaft(x,y,z,r,h,top,color,n=12,dx=0,dz=0) {
        const p = (i,upper) => {
            const a = i*tau/n, radius = upper ? top : r;
            return [x+Math.cos(a)*radius+(upper?dx:0),y+(upper?h:0),z+Math.sin(a)*radius+(upper?dz:0)];
        };
        for (let i=0;i<n;i++) {
            quad(p(i,false),p(i+1,false),p(i+1,true),p(i,true),color);
            tri(p(i,true),p(i+1,true),[x+dx,y+h,z+dz],color);
        }
    }
    function box(x,y,z,w,h,d,color) {
        const a=[x-w/2,y,z-d/2], b=[x+w/2,y,z-d/2], c=[x+w/2,y,z+d/2], e=[x-w/2,y,z+d/2];
        const up=p=>[p[0],p[1]+h,p[2]];
        for (const [i,j] of [[a,b],[b,c],[c,e],[e,a]]) quad(i,j,up(j),up(i),color);
        quad(up(a),up(b),up(c),up(e),color);
    }
    function gable(x,y,z,w,h,d,color) {
        for (const s of [-1,1]) {
            tri([x-w/2,y,z+s*d/2],[x+w/2,y,z+s*d/2],[x,y+h,z+s*d/2],color);
            quad([x+s*w/2,y,z-d/2],[x+s*w/2,y,z+d/2],[x,y+h,z+d/2],[x,y+h,z-d/2],color);
        }
    }
    function battlements(x,y,z,r,n) {
        for (let i=0;i<n;i++) box(x+r*Math.cos(i*tau/n),y,z+r*Math.sin(i*tau/n),.14,.16,.14,limestone);
    }
    function temple() {
        shaft(0,-.3,0,4.6,1.4,3.5,limestone,15);
        for (let i=0;i<3;i++) box(0,1.1+.1*i,0,2.9-.12*i,.1,5.7-.12*i,marble);
        for (let j=0;j<17;j++) for (let i=0;i<8;i++) {
            if (j>0 && j<16 && i>0 && i<7) continue;
            const x=(i-3.5)*.33, z=(j-8)*.31;
            shaft(x,1.4,z,.106,1.16,.082,marble,high?16:8);
            box(x,2.56,z,.22,.1,.22,marble);
        }
        box(0,1.4,0,1.55,1.22,3.9,limestone);
        box(0,2.66,0,2.65,.24,5.34,marble);
        gable(0,2.9,0,2.7,.38,5.39,marble);
        // Adjacent sanctuary and entrance volumes on the Acropolis plateau.
        for (const [x,z,width,depth] of [[-2.12,-.5,.85,1.65],[1.65,1.88,1.1,.9]]) {
            box(x,1.1,z,width,.58,depth,limestone);
            for(let i=0;i<4;i++) shaft(x-width*.45+i*width*.3,1.1,z+depth*.65,.05,.57,.045,marble,8);
            gable(x,1.68,z,width+.16,.15,depth*1.45,marble);
        }
    }
    function lighthouse() {
        box(0,0,0,2.2,.36,1.8,limestone);
        shaft(0,.36,0,.69,2.16,.50,limestone,8);
        shaft(0,2.52,0,.68,.16,.68,marble);
        shaft(0,2.68,0,.36,.48,.36,[.62,.77,.79],8);
        for (let i=0;i<8;i++) shaft(.36*Math.cos(i*tau/8),2.68,.36*Math.sin(i*tau/8),.025,.48,.025,slate,5);
        shaft(0,3.16,0,.49,.34,.05,slate);
    }
    function whiteTower() {
        shaft(0,0,0,1.28,2.9,1.20,limestone,32);
        shaft(0,2.9,0,1.28,.25,1.28,marble,32);
        battlements(0,3.15,0,1.21,26);
        shaft(0,3.15,0,.64,.66,.64,limestone,24);
        battlements(0,3.81,0,.59,15);
        for(let row=0;row<3;row++) for(let i=0;i<12;i++) {
            const a=(i+row*.35)*tau/12, r=1.27-row*.025;
            box(r*Math.cos(a),.5+row*.84,r*Math.sin(a),.10,.22,.10,slate);
        }
    }
    function bridge() {
        box(0,.72,0,15,.14,.52,slate);
        for (const x of [-4.95,-1.65,1.65,4.95]) {
            shaft(x,-.25,0,.38,1.05,.26,limestone);
            for(const s of [-1,1]) for(const side of [-1,1]) {
                shaft(x+s*.23,.8,side*.23,.079,1.75,.05,marble,8,-s*.23,-side*.23);
                for(let i=1;i<=8;i++) {
                    const span=s*i*.19;
                    shaft(x+span,.85,side*.24,.015,1.25+i*.052,.014,marble,4,-span,-side*.24);
                }
            }
        }
    }
    function fort() {
        shaft(0,-.2,0,2.5,.55,2.2,limestone,12);
        box(0,.35,0,3.25,.79,1.45,limestone);
        shaft(-.23,1.14,0,.72,1.32,.72,limestone,8);
        battlements(-.23,2.46,0,.65,10);
        for(const x of [-1.5,1.5]) shaft(x,.36,0,.48,1.0,.46,limestone,10);
    }
    const lake = id==='mountain-lake', seaBridge=id==='rioantirrio';
    const terrain = city?.terrain || (id==='parthenon'?'hill':'coast');
    const wet = lake || ['coast','lake','river'].includes(terrain);
    box(0,-.52,-20,250,.32,190,wet?water:grass);
    // A continuous triangular relief across the far shore, with per-location ridges.
    const phase=random()*tau, cols=high?84:48;
    function ridge(i,j) {
        const x=(i/cols-.5)*180, z=-8-j*4;
        const envelope=lake?1.6:terrain==='hill'?1.3:.85;
        const h=j===0?-.22:(1+Math.sin(x*.17+phase+j*.46)*.42+Math.cos(x*.31-phase)*.22)*Math.min(j,4)*envelope;
        return [x,h,z];
    }
    for(let j=0;j<7;j++) for(let i=0;i<cols;i++) {
        const tint=.65+random()*.13+j*.035;
        quad(ridge(i,j),ridge(i+1,j),ridge(i+1,j+1),ridge(i,j+1),grass.map(v=>v*tint));
    }
    const bourtzi=id==='nafplio' || id==='nafplio-bourtzi';
    if (!lake && !seaBridge) {
        const shore = x => bourtzi ? -5+.6*Math.sin(x*.6) :
            terrain==='river' ? .85+1.4*Math.sin(x*.37+phase) :
            1.1+.65*Math.sin(x*.69+phase)+.42*Math.cos(x*.34);
        for(let x=-14;x<14;x++) {
            quad([x,.02,-9],[x+1,.02,-9],[x+1,.02,shore(x+1)],[x,.02,shore(x)],grass);
            quad([x,.02,shore(x)],[x+1,.02,shore(x+1)],[x+1,-.22,shore(x+1)+.12],[x,-.22,shore(x)+.12],limestone);
        }
        const houses=high?92:48;
        for(let i=0;i<houses;i++) {
            const x=(random()-.5)*24, z=-6.7+random()*6.5;
            if ((Math.abs(x)<3.8 && z>-4) || z+.55>shore(x)) continue;
            const w=.31+random()*.55,h=.32+random()*.82,d=.43+random()*.55;
            const wall=[.71+random()*.15,.68+random()*.13,.58+random()*.15];
            box(x,.02,z,w,h,d,wall);
            if (city?.motif!=='harbor' || random()>.58) gable(x,h+.02,z,w+.04,.14,d+.04,roof);
            if(high) for(const side of [-1,1]) box(x+side*w*.25,h*.55,z+d/2,.08,.15,.02,slate);
        }
    }
    if (id==='parthenon') temple();
    else if (id==='whitetower') whiteTower();
    else if (seaBridge) bridge();
    else if (['patras','patras-lighthouse'].includes(id) || city?.motif==='lighthouse') lighthouse();
    else if (['nafplio','nafplio-bourtzi'].includes(id) || city?.motif==='fort') fort();
    else if (city?.motif==='bridge') {
        box(0,.78,1,5,.24,.72,limestone);
        for(const x of [-2,-1,0,1,2]) box(x,-.2,1,.22,1,.64,limestone);
    } else if(city?.motif==='tower') {
        box(0,0,0,.82,2.15,.82,limestone); gable(0,2.15,0,1,.42,1,roof);
    } else if(city?.motif==='theater') {
        for(let j=0;j<9;j++) for(let i=0;i<22;i++) {
            const a=i*Math.PI/21,r=1+j*.15;
            box(Math.cos(a)*r,0,-Math.sin(a)*r,.36,.12+j*.09,.24,limestone);
        }
    } else if(city?.motif==='falls') {
        box(0,0,0,2.4,1.6,.9,limestone); box(0,0,.47,.7,1.62,.08,[.7,.85,.85]);
    } else if(city?.motif==='harbor') {
        box(0,-.06,1.9,4,.22,.37,limestone);
        for(let i=0;i<4;i++) {
            box(i*.82-1.2,-.14,2.4,.29,.19,.82,marble);
            shaft(i*.82-1.2,.05,2.4,.017,.77,.014,slate,5);
        }
    }
    return new Float32Array(vertices);
}
root.OpsisWeatherLandscape = {build};
})(window);
