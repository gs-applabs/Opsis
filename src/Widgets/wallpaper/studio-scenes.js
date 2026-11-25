// Original fragment shaders for the Opsis wallpaper gallery.
(function (root) {
    const common = `
precision highp float;
uniform vec2 resolution;
uniform float time;
const float PI = 3.14159265;
float hash(vec2 p){ vec3 q=fract(vec3(p.xyx)*.1031); q+=dot(q,q.yzx+33.33); return fract((q.x+q.y)*q.z); }
float noise(vec2 p){ vec2 i=floor(p),f=fract(p); vec2 u=f*f*(3.-2.*f); return mix(mix(hash(i),hash(i+vec2(1,0)),u.x),mix(hash(i+vec2(0,1)),hash(i+1.),u.x),u.y); }
mat2 rotate(float a){ float s=sin(a),c=cos(a); return mat2(c,-s,s,c); }
float fb(vec2 p){ float a=.5,v=0.; for(int i=0;i<5;i++){v+=a*noise(p); p=rotate(.71)*p*2.03+vec2(13.7,5.2);a*=.5;}return v; }
float band(float x,float w){return exp(-abs(x)/w);}
vec3 ramp(vec3 a,vec3 b,float x){return mix(a,b,clamp(x,0.,1.));}
float stars(vec2 p,float t,float density){
 vec2 q=p*density,i=floor(q),f=fract(q)-.5; float h=hash(i);
 vec2 off=vec2(hash(i+7.),hash(i+8.))*.62-.31;
 return (1.-smoothstep(.01,.045+.12*h,length(f-off)))*pow(h,18.)*(.72+.28*sin(t*(.55+h)+h*45.));
}
float ridge(float x,float seed){return fb(vec2(x,seed))*.20 + fb(vec2(x*2.4,seed+17.))*.085;}
vec3 finish(vec3 color,vec2 uv){
 vec2 edge=(uv-.5)*1.22;
 float vignette=1.-.17*dot(edge,edge);
 float grain=(hash(gl_FragCoord.xy)-.5)*.005;
 vec3 exposed=max(color,0.)*vignette;
 return clamp(exposed/(1.+max(exposed-.82,0.)*.37)+grain,0.,1.);
}
`;
    const shaders = {
        'Studio Aurora': `
vec3 scene(vec2 p,vec2 u,float t){
 float h=.34; bool water=u.y<h; float y=water?2.*h-u.y:u.y, x=p.x;
 if(water){ x+=sin(p.y*125.+t*2.+x*4.)*.0035+sin(p.y*264.-t*1.8)*.002; }
 vec3 c=ramp(vec3(.08,.15,.26),vec3(.014,.026,.095),(y-h)*1.9);
 c+=vec3(.43,.63,.86)*stars(vec2(x,y),t,82.)*smoothstep(h,.7,y);
 float curtain=.59+.16*sin(x*2.6+t*.23)+.085*sin(x*7.4-t*.29)+.032*sin(x*15.+t*.38);
 float fibres=.56+.23*sin(x*146.+12.*fb(vec2(x*5.,y*3.))+t*.4)+.2*sin(x*291.-t*.28);
 float glow=band(y-curtain,.072)*fibres+band(y-curtain+.015,.016)*.34;
 vec3 light=ramp(vec3(.19,.33,.89),vec3(.09,.89,.63),.5+.5*sin(x*3.+y*3.-t*.18));
 c+=light*glow*smoothstep(h,.49,y)*1.04;
 for(int i=0;i<3;i++){float k=float(i);float height=h+ridge(x*(2.+k*1.3)+k*6.,16.+k)-k*.033;
   c=mix(c,vec3(.095,.14,.25)/(1.+k*.66),1.-smoothstep(height-.002,height+.002,y));}
 if(water){c=c*.62+vec3(.006,.025,.052);c+=vec3(.11,.26,.33)*band(u.y-h,.018)*.23;}
 return c;
}`,
        'Studio Nebula': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 q=rotate(.52)*p;
 q.x+=t*.006;
 float w=fb(q*4.+vec2(1.7,2.)), f=fb(q*6.+vec2(w*4.,w*1.8));
 float ridged=1.-abs(2.*fb(q*10.+f*5.)-1.);
 float fog=pow(max(0., f-.20),1.25)*band(q.y+.07+.14*sin(q.x*4.),.32)*2.1;
 vec3 c=vec3(.018,.013,.058);
 c+=ramp(vec3(.13,.25,.73),vec3(.78,.24,.43),smoothstep(-.55,.42,q.x))*fog*(.6+ridged*.54);
 c+=vec3(.84,.47,.23)*pow(max(0.,f-.45),1.6)*4.7*band(q.y+.03,.17);
 c+=vec3(.22,.54,.89)*pow(w,3.)*band(q.y-.15,.24);
 c+=vec3(.8,.83,1.)*(stars(p,t,93.)*.73+stars(p+3.2,t,52.)*1.24);
 vec2 d=p-vec2(.29,.11); float r=length(d);
 c+=vec3(.7,.72,1.)*(band(r,.048)*.21+band(r,.009)*.97+band(d.x,.002)*band(d.y,.073)*.22+band(d.y,.002)*band(d.x,.095)*.36);
 return c;
}`,
        'Studio Tide': `
vec3 scene(vec2 p,vec2 u,float t){
 float horizon=.50; float y=u.y-horizon; vec2 moon=vec2(-.27,.21); vec2 d=p-moon;
 vec3 c=ramp(vec3(.095,.20,.29),vec3(.011,.033,.09),max(y,0.)*2.55);
 c=mix(c,lunar(d,.068,.62),disc(d,.068));
 c+=vec3(.35,.5,.68)*band(length(d),.15)*.26;
 c+=vec3(.44,.64,.81)*stars(p,t,95.)*smoothstep(.015,.24,y);
 float cloud=bank(vec2(p.x,y),t,6.31);
 c=mix(c,vec3(.07,.115,.19)+vec3(.15,.13,.13)*bank(vec2(p.x-.07,y+.019),t,6.31),cloud*band(y-.09,.10)*.76);
 if(y<0.){
  float z=.62/(.1-y); vec2 q=vec2(p.x*z,z);
  float f=fb(vec2(q.x*5.,q.y*15.-t*.95));
  float wave=sin(q.y*23.-t*2.1+f*3.0);
  c=ramp(vec3(.009,.034,.076),vec3(.035,.17,.27),pow(f,1.7)+.12*wave);
  float spec=band(p.x-moon.x+.05*sin(q.y*8.+t+q.x*6.),.08+(-y)*.22)*pow(max(0.,wave),8.);
  c+=vec3(.37,.68,.86)*spec*.61;
  float foam=pow(clamp(1.-abs(wave+.4)*1.6,0.,1.),5.)*pow(f,3.)*2.5;
  c+=vec3(.015,.81,1.)*foam;
  c+=vec3(.19,.31,.37)*band(y,.026)*.16;
 }
 return c;
}`,
        'Studio Dunes': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 s=p-vec2(.39,.225); float sun=length(s);
 vec3 c=ramp(vec3(.92,.53,.29),vec3(.31,.125,.255),smoothstep(.26,1.,u.y));
 c+=vec3(.99,.43,.14)*band(sun,.23)*.26;
 float granules=fb(rotate(t*.014)*s*135.)*.13;
 c=mix(c,vec3(1.,.88,.65)+vec3(0.,.51,.57)*granules,disc(s,.106));
 float haze=fb(vec2(p.x*2.3-t*.026,u.y*25.));
 c+=vec3(.4,.19,.12)*haze*band(u.y-.46,.12)*.32;
 for(int i=0;i<4;i++){
  float k=float(i),x=p.x+.19*k;
  float crest=.61-.123*k+.064*sin(x*(3.3+k*.62)+k*1.75+t*.063)+.039*fb(vec2(x*4.+k,3.2));
  float shade=smoothstep(-.16,.068,x+.22*sin(u.y*7.+k));
  vec3 low=ramp(vec3(.26,.095,.13),vec3(.66,.25,.115),k*.23);
  vec3 high=ramp(vec3(.85,.33,.17),vec3(1.,.61,.24),k*.25);
  float silk=sin((u.y-crest)*267.+sin(x*19.)*1.6+fb(vec2(x*16.,u.y*33.))*5.+t*.17)*.024;
  vec3 sand=mix(low,high,shade)+silk+vec3(.25,.14,.06)*band(u.y-crest,.027);
  c=mix(c,sand,1.-smoothstep(crest-.0015,crest+.0015,u.y));
 }
 return c;
}`,
        'Studio Prism': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 q=rotate(.4+.23*sin(t*.24))*p;
 float b=fb(q*4.+vec2(t*.08,-t*.06));
 vec3 c=ramp(vec3(.022,.034,.083),vec3(.18,.29,.39),b*.63);
 for(int i=0;i<5;i++){
  float k=float(i);vec2 x=rotate(-.65+k*.26)*q;
  float pos=.29*cos(k*2.4+t*.17), offset=x.x-pos+.12*x.y*x.y+.029*sin(11.*x.y+t*.4+k);
  float width=.061+.014*k;
  float shape=max(abs(offset)/width,abs(x.y+.04*k)*1.28);
  float edge=band(shape-1.,.065);
  vec3 spectrum=.52+.48*cos(vec3(.0,2.1,4.2)+k*.94+x.y*5.+t*.24);
  float refract=.58+.42*sin(62.*x.y+18.*offset+7.*b);
  c+=spectrum*(edge*.50+exp(-shape*3.)*.22+step(shape,1.)*(.13+refract*.20));
  c+=vec3(.66,.82,1.)*band(offset+width*.78,.009)*exp(-3.*abs(x.y-.12*k))*.24;
 }
 c+=vec3(.11,.23,.29)*stars(p,t,46.)*.24;
 return c;
}`,
        'Studio Ink': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 q=rotate(.42)*p;
 vec2 warp=vec2(fb(q*3.8+vec2(t*.042,0.)),fb(q*3.8+vec2(8.,-t*.051)));
 vec2 cur=q*3.2+warp*2.9;
 float flow=fb(cur+vec2(fb(cur*1.7),fb(cur*1.5+2.))*2.);
 float main=sin(23.*flow+6.*q.y+t*.20);
 float m=smoothstep(-.43,.56,main);
 vec3 c=ramp(vec3(.027,.052,.084),vec3(.09,.40,.40),m);
 float wash=smoothstep(.33,.70,fb(cur*.85+4.));
 c=mix(c,mix(vec3(.25,.12,.22),vec3(.72,.33,.28),m),wash*.88);
 float vein=band(main-.28,.085)*smoothstep(.39,.66,warp.x);
 c+=vec3(.88,.53,.20)*vein*.74;
 c+=vec3(.56,.48,.39)*band(main+.91,.074)*.16;
 c=mix(c,vec3(.82,.80,.68),pow(max(0.,flow-.57)*2.3,2.)*.51);
 return c;
}`,
        'Studio Neon': `
vec3 scene(vec2 p,vec2 u,float t){
 float horizon=.43, y=u.y-horizon, x=p.x;
 vec3 c=ramp(vec3(.23,.10,.29),vec3(.018,.035,.105),max(y,0.)*2.25);
 c+=vec3(.36,.17,.50)*band(length(vec2(x-.25,y+.035)),.37);
 if(y>0.){
  for(int i=0;i<3;i++){
   float k=float(i); float scale=13.+k*5.; vec2 q=vec2(x*scale+k*7.,y);
   float cell=floor(q.x); float hh=.055+hash(vec2(cell,k))*.25;
   float side=step(.09,fract(q.x))*step(fract(q.x),.91)*step(y,hh);
   vec3 building=vec3(.048,.064,.12)/(1.+k*.31);
   vec2 win=vec2(floor(q.x*4.),floor(y*70.));
   float lit=step(.89,hash(win+k*23.))*step(fract(q.x*4.),.43)*step(fract(y*70.),.42);
   c=mix(c,building+vec3(.75,.36,.78)*lit*.62+vec3(.03,.61,.82)*band(y-hh,.004)*.47,side);
  }
  float trail=band(y-.27-.018*sin(x*2.5),.0017)*band(fract(x*.34-t*.18)-.55,.10);
  c+=vec3(.49,.83,1.)*trail;
 } else {
   float z=.48/(.075-y);vec2 q=vec2(x*z,z+t*.33);
   float lines=band(fract(q.x*2.)-.5,.032)*.23+band(fract(q.y*2.8)-.5,.038)*.36;
   c=vec3(.014,.029,.068)+vec3(.11,.37,.63)*lines;
   c+=vec3(.52,.12,.34)*band(y,.048)*.42;
   c+=vec3(.84,.15,.44)*band(x-.34*y,.017+abs(y)*.07)*.34*noise(vec2(q.y*7.,q.x));
 }
 return c;
}`,
        'Studio Forest': `
vec3 scene(vec2 p,vec2 u,float t){
 float x=p.x, y=u.y;
 vec3 c=ramp(vec3(.83,.63,.43),vec3(.28,.48,.52),smoothstep(.48,1.,y));
 c+=vec3(.72,.44,.17)*band(length(p-vec2(.33,.24)),.20)*.37;
 float cloud=bank(vec2(x,y-.5),t,7.1);
 c=mix(c,vec3(.71,.75,.66)+vec3(.16,.14,.11)*bank(vec2(x-.04,y-.46),t,7.1),cloud*.55);
 for(int i=0;i<5;i++){
  float k=float(i),xx=x+k*3.;
  float gr=.54-k*.092+ridge(xx*(1.7+k*.4),5.+k)*.65;
  float cell=floor(xx*(15.+k*6.));
  float yTree=y-gr;
  float spread=fract(xx*(15.+k*6.))-.5;
  float branch=spruce(vec2(spread/(7.+k*2.),yTree),.028+hash(vec2(cell,k))*(.061+k*.013),hash(vec2(cell+4.,k)));
  vec3 land=ramp(vec3(.34,.46,.44),vec3(.021,.105,.095),k*.25);
  land+=.055*fb(vec2(xx*8.,y*21.));
  c=mix(c,land,max(branch,1.-smoothstep(gr-.002,gr+.002,y)));
  float fog=band(y-gr-.025,.036)*fb(vec2(x*3.-t*.043+k,y*14.));
  c=mix(c,vec3(.65,.75,.69),fog*(.33-k*.04));
 }
 c+=vec3(.77,.65,.26)*stars(p+vec2(t*.008,0.),t,45.)*(1.-smoothstep(.19,.47,y))*.37;
 return c;
}`,
        'Studio Eclipse': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 q=p-vec2(.02+.016*sin(t*.12),.036);
 float r=length(q), a=atan(q.y,q.x);
 float f=fb(vec2(cos(a)*3.,sin(a)*3.)+vec2(t*.041,-t*.032));
 float h=fb(vec2(a*3.+f*1.4, r*13.+t*.046));
 float mask=smoothstep(.192,.199,r);
 float luminous=band(r-.205,.075+.061*f)*pow(h,1.84)*2.1;
 float flare=pow(max(0.,sin(a*7.+f*3.+t*.24)),11.)*band(r-.204,.14)*(.24+.73*f);
 vec3 c=vec3(.023,.014,.058)+vec3(.47,.25,.64)*stars(p,t,84.);
 c+=vec3(.12,.061,.19)*band(r-.24,.28);
 c+=mask*(ramp(vec3(.87,.24,.15),vec3(1.,.62,.33),.55+.45*sin(a-.2))*luminous+vec3(1.,.48,.25)*flare);
 c+=mask*vec3(1.,.76,.46)*band(r-.199,.009)*(.55+.43*sin(a*2.+1.));
 vec3 planet=vec3(.024,.026,.055)+.048*fb(q*28.)*smoothstep(-.16,.12,q.x);
 return mix(planet,c,mask);
}`,
        'Studio Abyss': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 q=vec2(p.x*(1.4-u.y*.49),u.y);
 float haze=fb(vec2(q.x*3.+t*.035,q.y*4.));
 vec3 c=ramp(vec3(.008,.043,.098),vec3(.024,.43,.47),pow(u.y,1.3)+.18*haze);
 float ray=pow(max(0.,sin(13.*(p.x+.25*u.y)/(1.23-u.y)+t*.22+2.*haze)),5.);
 c+=vec3(.17,.52,.53)*ray*u.y*.43;
 vec2 d=rotate(.24)*p*13.;
 float caustic=band(sin(d.x+1.3*sin(d.y*1.15+t*.4))+sin(d.y+1.1*sin(d.x*.87-t*.39)),.15);
 c+=vec3(.08,.33,.35)*caustic*pow(u.y,2.)*.48;
 for(int i=0;i<3;i++){
  float k=float(i);float h=.255-.070*k+.032*fb(vec2(p.x*6.+k*2.,k));
  vec3 dark=ramp(vec3(.027,.13,.18),vec3(.011,.056,.11),k*.5);
  c=mix(c,dark,1.-smoothstep(h-.001,h+.001,u.y));
 }
 for(int i=0;i<7;i++){
  float k=float(i);vec2 b=vec2(fract(.14*k+t*(.021+k*.004))*.92+.055, .34+.025*k+.025*sin(t*.65+k));
  vec2 d0=(u-b)*vec2(1.55,1.);
  float mask=finFish(d0*(1.3+k*.025),t*4.+k);
  float lateral=band(d0.y+.001,.0019) * (1.-smoothstep(.008,.022,abs(d0.x)));
  c=mix(c,vec3(.021,.15,.22)+vec3(.09,.22,.19)*lateral,mask*.90);
 }
 c+=vec3(.40,.71,.67)*stars(p+vec2(-t*.012,t*.015),t,70.)*.43;
 return c;
}`
    };

    const landscapes = typeof module !== 'undefined' && module.exports
        ? require('./studio-landscapes.js') : root.OpsisStudioLandscapes;
    Object.assign(shaders, landscapes.scenes);

    const plates = {
        'Studio Forest': { file: 'forest', kind: 'forest', grade: 'vec3(1.03,.98,.91)' },
        'Studio Cedar': { file: 'cedar', kind: 'forest', grade: 'vec3(.96,1.,1.02)' },
        'Studio Neon': { file: 'city', kind: 'city', grade: 'vec3(.99,1.02,1.10)' },
        'Studio Alpine': { file: 'alpine', kind: 'lake', grade: 'vec3(1.02,1.02,1.0)', shoreline: '.266' },
        'Studio Fjord': { file: 'fjord', kind: 'lake', grade: 'vec3(.99,1.02,1.04)', shoreline: '.214' },
        'Studio Reef': { file: 'reef', kind: 'reef', grade: 'vec3(1.02,1.,1.04)' }
    };
    const plateShaders = {
        forest: `
 float drift=sin(t*.072);
 vec2 sway=vec2((.0012+.0020*(1.-uv.y))*drift, .001*sin(t*.053));
 vec3 c=samplePlate(uv,sway);
 float hollow=band(uv.y-.49-.039*sin(p.x*3.7),.091)*smoothstep(.21,.44,uv.y);
 float haze=bank(vec2(p.x*.73,uv.y*.75),t*.76,9.82);
 c=mix(c,vec3(.82,.75,.65),hollow*haze*.21);
 c += vec3(.1,.084,.046)*band(uv.y-.65,.22)*smoothstep(.39,.68,haze);
 for(int i=0;i<4;i++){
   float k=float(i), x=fract(.17+k*.032+t*.016);
   vec2 d=vec2((uv.x-x)*resolution.x/resolution.y,uv.y-.77-.018*sin(t*.42+k)-k*.011);
   float visibility=smoothstep(.012,.060,x)*(1.-smoothstep(.94,.988,x))*smoothstep(.30,.49,c.r);
   c=mix(c,vec3(.062,.065,.055),soaring(d*(2.2+k*.23),t*3.6+k*.57)*visibility*.86);
 }
 return c;
`,
        city: `
 vec3 c=samplePlate(uv,vec2(.0014*sin(t*.067),0.));
 float blue=smoothstep(.08,.22,c.b-c.r);
 float skyline=smoothstep(.49,.81,uv.y)*blue;
 float stream=bank(vec2(p.x*.88,uv.y-.48),t*.74,4.62);
 c=mix(c,c*vec3(.79,.90,1.04)+vec3(.015,.024,.044),stream*skyline*.65);
 float lamps=smoothstep(.20,.42,c.r-c.b)*smoothstep(.30,.63,c.r)*(1.-smoothstep(.51,.74,uv.y));
 float seed=hash(floor(uv*vec2(67.,36.)));
 float flutter=smoothstep(.78,.97,sin(t*(.13+seed*.27)+seed*71.));
 c+=c*vec3(.34,.16,.035)*lamps*flutter;
 c+=vec3(.49,.61,.83)*stars(p,t,56.)*smoothstep(.81,1.,uv.y)*.22;
 return c;
`,
        lake: `
 float mask=1.-smoothstep(shoreline-.020,shoreline+.013,uv.y);
 float perspective=(.08+pow(max(0.,shoreline+.022-uv.y),.72));
 float ripple=sin(uv.y*267.+p.x*12.-t*1.9)+.54*sin(uv.y*443.-p.x*19.+t*1.30);
 vec2 offset=vec2(.0012*sin(t*.068)+ripple*mask*perspective*.009,
   sin(uv.y*178.+p.x*9.-t*1.3)*mask*.003);
 vec3 c=samplePlate(uv,offset);
 float sparkle=pow(max(0.,ripple*.62),10.)*band(p.x-.22,.28);
 c+=vec3(.12,.19,.22)*mask*sparkle*.23;
 float mist=bank(vec2(p.x,uv.y-.25),t*.55,7.8);
 c=mix(c,vec3(.66,.73,.78),mist*band(uv.y-shoreline-.042,.025)*.17);
 for(int i=0;i<3;i++){
   float k=float(i), x=fract(.43+k*.025+t*.011);
   vec2 d=vec2((uv.x-x)*resolution.x/resolution.y,uv.y-.73-k*.013-.012*cos(t*.31+k));
   float visibility=smoothstep(.04,.08,x)*(1.-smoothstep(.93,.98,x));
   c=mix(c,vec3(.036,.053,.081),soaring(d*(2.6+k*.3),t*2.9+k*.7)*visibility*.72);
 }
 return c;
`,
        reef: `
 float swell=sin(uv.y*15.+p.x*4.-t*.41);
 vec3 c=samplePlate(uv,vec2(.0014*swell,.0007*sin(p.x*17.+t*.27)));
 float openWater=smoothstep(.030,.17,c.b-c.r)*smoothstep(.22,.50,uv.y);
 vec2 lens=p*vec2(13.,9.)+vec2(-t*.17,t*.09);
 float caustics=pow(max(0.,sin(lens.x+1.8*sin(lens.y))+sin(lens.y*1.6)),5.)*.01;
 c+=vec3(.13,.38,.45)*caustics*(.25+.75*openWater);
 for(int i=0;i<8;i++){
   float k=float(i);
   float x=fract(.14-t*.021+k*.038);
   float y=.52+k*.024+.032*sin(t*.68+k*.81);
   vec2 d=vec2((x-uv.x)*resolution.x/resolution.y,uv.y-y);
   float size=.61+.48*hash(vec2(k,2.));
   float visibility=smoothstep(.02,.08,x)*(1.-smoothstep(.92,.98,x));
   vec3 fish=ramp(vec3(.084,.22,.22),vec3(.74,.57,.27),.25+.4*band(d.y,.003));
   c=mix(c,fish,finFish(d/size,t*5.2+k)*visibility*openWater*.91);
 }
 c+=vec3(.38,.62,.63)*stars(p+vec2(t*.008,-t*.010),t,79.)*.28*openWater;
 return c;
`

    };

    function plateFragment(plate) {
        if (!plate) return '';
        return `
uniform sampler2D photo;
uniform float photoAspect;
uniform float photoReady;
vec3 samplePlate(vec2 uv,vec2 delta){
 float display=resolution.x/resolution.y;
 vec2 cover=vec2(min(1.,display/photoAspect),min(1.,photoAspect/display));
 vec2 q=.5+(uv-.5)*cover*.985+delta;
 return texture2D(photo,q).rgb*${plate.grade};
}
vec3 photoScene(vec2 p,vec2 uv,float t){
 ${plate.shoreline ? `const float shoreline=${plate.shoreline};` : ''}
 ${plateShaders[plate.kind]}
}
`;
    }

    function fragment(id) {
        const photo = plateFragment(plates[id]);
        const color = photo ? 'photoReady > .5 ? photoScene(p,uv,time) : scene(p,uv,time)' : 'scene(p,uv,time)';
        return common + landscapes.optics + shaders[id] + photo + `
void main(){
 vec2 uv=gl_FragCoord.xy/resolution;
 vec2 p=(gl_FragCoord.xy-resolution*.5)/resolution.y;
 p.x*=min(1.,2.35/(resolution.x/resolution.y));
 gl_FragColor=vec4(finish(${color},uv),1.);
}
`;
    }

    if (typeof module !== 'undefined' && module.exports) {
        module.exports = { ids: Object.keys(shaders), fragment, plate: id => plates[id]?.file };
        return;
    }

    const vertex = 'attribute vec2 point; void main(){gl_Position=vec4(point,0.,1.);}';
    let target, gl, program, locations, active, buffer, lastPoster, poster, unavailable = false;
    const failedScenes = new Set();
    let plateTexture, activePlate, plateImage, plateAspect = 0;

    function selectPlate(id) {
        const file = plates[id]?.file;
        if (activePlate === file) return;
        activePlate = file;
        plateAspect = 0;
        plateImage = null;
        if (!file) return;
        const image = new Image();
        image.onload = () => {
            if (image === plateImage) plateAspect = -1;
        };
        plateImage = image;
        image.src = 'gallery/plates/' + file + '.webp';
    }

    function bindPlate() {
        if (!locations[2]) return;
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, plateTexture);
        if (plateAspect < 0) {
            gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true);
            try {
                gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, plateImage);
                plateAspect = plateImage.naturalWidth / plateImage.naturalHeight;
            } finally { gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false); }
        }
        gl.uniform1i(locations[2], 0);
        gl.uniform1f(locations[3], plateAspect || 1);
        gl.uniform1f(locations[4], plateAspect ? 1 : 0);
    }
    function compile(type, source) {
        const shader = gl.createShader(type);
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        if (gl.getShaderParameter(shader, gl.COMPILE_STATUS)) return shader;
        const error = gl.getShaderInfoLog(shader);
        gl.deleteShader(shader);
        throw new Error(error);
    }
    function select(id) {
        if (active === id && program) return;
        if (program) gl.deleteProgram(program);
        program = null;
        const v = compile(gl.VERTEX_SHADER, vertex);
        let f;
        try {
            f = compile(gl.FRAGMENT_SHADER, fragment(id));
            program = gl.createProgram();
            gl.attachShader(program, v);
            gl.attachShader(program, f);
            gl.linkProgram(program);
        } finally {
            gl.deleteShader(v);
            if (f) gl.deleteShader(f);
        }
        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
            const message = gl.getProgramInfoLog(program);
            gl.deleteProgram(program);
            program = null;
            throw new Error(message);
        }
        gl.useProgram(program);
        const point = gl.getAttribLocation(program, 'point');
        gl.enableVertexAttribArray(point);
        gl.vertexAttribPointer(point, 2, gl.FLOAT, false, 0, 0);
        locations = ['resolution', 'time', 'photo', 'photoAspect', 'photoReady'].map(name => gl.getUniformLocation(program, name));
        selectPlate(id);
        active = id;
    }
    function posterId(id) { return id.slice(7).toLowerCase(); }
    function fallback(ctx, id, w, h) {
        if (lastPoster !== id) {
            lastPoster = id;
            poster = new Image();
            poster.src = 'gallery/' + posterId(id) + '.jpg';
        }
        if (poster.complete && poster.naturalWidth) {
            const scale = Math.max(w / poster.naturalWidth, h / poster.naturalHeight);
            const dw = poster.naturalWidth * scale, dh = poster.naturalHeight * scale;
            ctx.drawImage(poster, (w - dw) / 2, (h - dh) / 2, dw, dh);
        } else {
            ctx.fillStyle = '#071223';
            ctx.fillRect(0, 0, w, h);
        }
    }
    function prepareGraphics() {
        if (!target) {
            target = document.createElement('canvas');
            target.addEventListener('webglcontextlost', event => event.preventDefault());
            target.addEventListener('webglcontextrestored', () => {
                // A restored context is empty. Recreate the vertex buffer, shader, and photo texture.
                program = locations = active = buffer = plateTexture = null;
                activePlate = undefined;
                plateImage = null;
                plateAspect = 0;
                failedScenes.clear();
            });
            gl = target.getContext('webgl', {
                alpha: false, antialias: false, depth: false, stencil: false, failIfMajorPerformanceCaveat: true
            });
            unavailable = !gl;
        }
        if (!gl || gl.isContextLost() || buffer) return;

        buffer = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
        gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1,-1,3,-1,-1,3]), gl.STATIC_DRAW);
        plateTexture = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, plateTexture);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([0,0,0,255]));
    }

    function draw(ctx, id, w, h, seconds) {
        if (w <= 0 || h <= 0) return;
        ctx.save();
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        try {
            if (!unavailable) prepareGraphics();
            if (unavailable || failedScenes.has(id) || gl.isContextLost()) fallback(ctx, id, w, h);
            else {
                if (target.width !== w || target.height !== h) { target.width = w; target.height = h; }
                select(id);
                gl.viewport(0, 0, w, h);
                gl.uniform2f(locations[0], w, h);
                gl.uniform1f(locations[1], seconds);
                bindPlate();
                gl.drawArrays(gl.TRIANGLES, 0, 3);
                ctx.drawImage(target, 0, 0);
            }
        } catch (error) {
            if (!gl) unavailable = true;
            else if (!gl.isContextLost()) failedScenes.add(id);
            console.warn('Opsis gallery scene', id, error);
            fallback(ctx, id, w, h);
        } finally { ctx.restore(); }
    }
    root.OpsisStudioScenes = { has: id => Object.hasOwn(shaders, id), draw };
})(globalThis);
