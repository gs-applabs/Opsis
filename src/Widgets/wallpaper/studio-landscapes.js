// Additional animated wallpapers. The shared optics and terrain functions keep their fragment shaders small.
(function (root) {
    const optics = `
float disc(vec2 d,float r){return 1.-smoothstep(r-.0015,r+.0015,length(d));}
float strands(vec2 p,float f){return abs(sin(p.x*f+2.1*sin(p.y*f*.71)));}
float bank(vec2 p,float t,float seed){
 vec2 q=p*vec2(3.0,13.)+vec2(-t*.032,seed);
 float core=fb(q*.45+vec2(seed,3.));
 float curl=noise(q*.67+vec2(9.7,core));
 return smoothstep(.34,.71,fb(q+vec2(core*1.9,curl*1.15))+noise(q*2.7)*.034);
}
vec3 skyLight(vec2 p,float t,vec2 sun,vec3 low,vec3 high){
 vec3 c=ramp(low,high,smoothstep(-.13,.50,p.y));
 float r=length(p-sun);
 float halo=band(r,.16);
 c+=vec3(.64,.30,.13)*halo*.48;
 float s=disc(p-sun,.038);
 c=mix(c,vec3(1.,.92,.71),s);
 float cloud=bank(p,t,4.7)*band(p.y-.14,.18);
 float cloudShadow=bank(p+vec2(.02,.023),t,4.7);
 return mix(c,vec3(.18,.22,.32)+vec3(.74,.48,.31)*(.31+halo*.6)*(1.-cloudShadow*.78),cloud*.79);
}
vec3 lunar(vec2 d,float r,float phase){
 vec2 q=d/r;
 float rr=length(q);
 float maria=smoothstep(.38,.73,fb(q*2.5+vec2(7.,3.)));
 float mountain=fb(q*14.+9.);
 float limb=sqrt(max(0.,1.-rr*rr));
 float terminator=smoothstep(-.15,.12, q.x+phase*limb);
 float crater=band(length(q-vec2(-.31,-.26))-.115,.022)+band(length(q-vec2(.31,.43))-.15,.021);
 float albedo=.62+.42*mountain-.40*maria+.13*crater;
 return vec3(.97,.93,.86)*albedo*(.69+.31*limb)*terminator + vec3(.09,.12,.19)*(1.-terminator)*limb;
}
float dryRidge(float x,float seed){
 return .52*fb(vec2(x,seed))+.3*(1.-abs(2.*fb(vec2(x*1.8+9.,seed*1.51))-1.))+.18*noise(vec2(x*4.,seed+7.));
}
float spruce(vec2 q,float h,float seed){
 float v=q.y/h;
 float trunk=step(abs(q.x+.007*v),h*.055)*step(-.06,v)*step(v,1.);
 float skirt=abs(q.x+.025*sin(v*3.+seed)*h);
 float layer=fract(v*5.7+seed*.23), margin=h*(1.-v)*(.24+.30*pow(1.-layer,1.7));
 return max(trunk,step(skirt,margin)*step(.05,v)*step(v,1.)*smoothstep(.18,.51,noise(vec2(q.y*270.,q.x*130.+seed))));
}
vec3 waterBands(vec2 p,float y,float t,vec2 light,vec3 base,vec3 tint){
 float z=.42/(.055-y);
 vec2 q=vec2(p.x*z,z);
 float ripple=sin(q.y*20.-t*1.17+sin(q.x*6.+t*.46)+fb(q*3.)*4.);
 float fine=sin(q.y*48.+q.x*7.-t*1.81);
 float sparkle=pow(max(0.,ripple*.72+fine*.28),10.);
 float track=band(p.x-light.x+sin(q.y*4.+t)*.025,.038-y*.32);
 return base+tint*(.13+fb(q*1.2)*.43)+tint*.37*ripple+vec3(1.,.72,.45)*sparkle*track*.70;
}
float finFish(vec2 d,float t){
 vec2 q=d/vec2(.029,.008);
 float spine=sin(q.x*2.3+t)*.15-.09*q.x;
 float body=1.-smoothstep(.96,1.07,length(vec2(q.x,(q.y-spine)*(1.12-.32*q.x))));
 float tailX=-q.x-.83, fork=abs(q.y-spine-sin(t)*.26);
 float tail=step(0.,tailX)*step(tailX,.72)
   * (1.-smoothstep(.022,.10,max(fork-tailX*1.13,tailX*.93-.30-fork)));
 float dorsal=step(.31,q.y)*(1.-smoothstep(.94,1.02,q.y+1.66*abs(q.x+.03)));
 float ventral=step(q.y,-.34)*(1.-smoothstep(.79,.94,-q.y+2.7*abs(q.x-.29)));
 return max(body,max(tail,max(dorsal,ventral)));
}
float soaring(vec2 d,float phase){
 float x=abs(d.x)/.030;
 float beat=sin(phase+(d.x<0.?-.17:.17));
 float wing=.006*sin(x*2.77)+.011*beat*x-.003*x*x;
 float width=(.0026-.0014*x)*(.83+.17*cos(phase));
 float feathers=(.00062+.00026*cos(x*44.))*smoothstep(.55,.96,x);
 float flight=(1.-smoothstep(width,width+.0015,abs(d.y-wing+.0014*x)-feathers))
   *(1.-smoothstep(.89,1.04,x));
 float body=(1.-smoothstep(.78,1.14,length(vec2(d.x/.0031,(d.y+.0009)/.0051))));
 float tail=(1.-smoothstep(.0010,.0035,abs(d.x)))
   *smoothstep(-.0088,-.0058,d.y)*(1.-smoothstep(-.0047,-.0024,d.y));
 return max(flight,max(body,tail));
}
`;

    const scenes = {
        'Studio Alpine': `
vec3 scene(vec2 p,vec2 u,float t){
 vec3 c=skyLight(p,t,vec2(-.47,.29),vec3(.76,.71,.58),vec3(.18,.41,.63));
 float shore=.24;
 if(u.y<shore){
  c=waterBands(p,u.y-shore,t,vec2(-.47,.29),vec3(.037,.12,.15),vec3(.13,.36,.37));
 }
 for(int i=0;i<4;i++){
  float k=float(i),x=p.x+k*.065;
  float y=shore+.09-k*.037+dryRidge(x*(2.1+k*.66)+k*5.,24.+k)*(.43-k*.064);
  float yy=u.y<shore? 2.*shore-u.y:u.y;
  if(u.y<shore) x+=sin(u.y*150.+t*1.8)*.012;
  float grain=fb(vec2(x*34.,yy*81.));
  float snow=smoothstep(.11,.23, yy-shore-.094+grain*.12-k*.036);
  vec3 rock=ramp(vec3(.32,.42,.48),vec3(.095,.17,.21),k*.3)*(.58+.82*grain);
  vec3 crust=mix(rock,vec3(.82,.87,.89),snow*.8);
  float opaque=(1.-smoothstep(y-.002,y+.002,yy))*(u.y<shore?.63:1.);
  c=mix(c,crust,opaque);
 }
 for(int i=0;i<3;i++){float k=float(i);c=mix(c,vec3(.08,.11,.15),soaring(p-vec2(fract(.2+k*.11+t*.025)-.5,.20+.021*k),t*2.3+k));}
 return c;
}`,
        'Studio Cedar': `
vec3 scene(vec2 p,vec2 u,float t){
 vec3 c=skyLight(p,t,vec2(.30,.27),vec3(.86,.64,.44),vec3(.16,.40,.45));
 for(int i=0;i<6;i++){
  float k=float(i),z=10.+k*2.3,x=p.x+k*.74;
  float y=.45-k*.067+dryRidge(x*3.,4.+k)*.15;
  float id=floor(x*z), seed=hash(vec2(id,k));
  float offset=fract(x*z)-.5;
  float tree=spruce(vec2(offset/z,u.y-y),.055+seed*.16+k*.009,seed);
  vec3 leaf=ramp(vec3(.26,.41,.38),vec3(.027,.11,.096),k*.2)*(.65+fb(p*vec2(38.,61.))*.67);
  c=mix(c,leaf,max(1.-smoothstep(y-.002,y+.002,u.y),tree));
  c=mix(c,vec3(.80,.78,.63),band(u.y-y-.01,.026)*bank(p,t,2.+k)*(.45-k*.062));
 }
 c+=vec3(.72,.42,.13)*stars(p+vec2(-t*.022,t*.009),t,39.)*.52;
 return c;
}`,
        'Studio Caldera': `
vec3 scene(vec2 p,vec2 u,float t){
 vec3 c=skyLight(p,t,vec2(.57,.13),vec3(.79,.35,.22),vec3(.19,.31,.49));
 float y=u.y-.34;
 if(y<0.)c=waterBands(p,y,t,vec2(.57,.13),vec3(.035,.085,.14),vec3(.16,.33,.42));
 float left=1.-smoothstep(-.45,.14,p.x+.45*u.y), terrain=.15+dryRidge(p.x*3.8,31.)*.53;
 float h=(1.-smoothstep(terrain,terrain+.002,u.y))*left;
 float rock=fb(p*vec2(36.,61.)+dryRidge(p.x*9.,1.)*4.);
 vec3 land=ramp(vec3(.16,.095,.09),vec3(.60,.29,.17),rock);
 float roof=.48+.12*noise(vec2(floor(p.x*36.),7.));
 vec2 block=fract(vec2(p.x*36.,u.y*41.));
 float houses=step(.72,hash(floor(vec2(p.x*36.,u.y*41.))))*step(.12,block.x)*step(.17,block.y)*step(roof,u.y);
 c=mix(c,land+houses*vec3(.48,.47,.41),h);
 float boat=band(p.x-(fract(.17+t*.018)*.77),.025)*band(y+.087,.002);
 c+=vec3(1.,.65,.36)*boat*.91;
 return c;
}`,
        'Studio Fjord': `
vec3 scene(vec2 p,vec2 u,float t){
 vec3 c=skyLight(p,t,vec2(-.12,.14),vec3(.79,.75,.67),vec3(.24,.35,.49));
 float horizon=.265,y=u.y-horizon;
 if(y<0.) c=waterBands(p,y,t,vec2(.09,.12),vec3(.022,.079,.11),vec3(.12,.37,.41));
 float valley=pow(clamp(abs(p.x-.03)*1.07,0.,1.),.9);
 for(int i=0;i<3;i++){
  float k=float(i);float height=horizon+valley*(.37+k*.13)+dryRidge(p.x*(3.+k),k+77.)*.17-k*.041;
  float h=1.-smoothstep(height,height+.003,u.y);
  float pattern=fb(vec2(p.x*21.,p.y*39.));
  vec3 rock=mix(vec3(.35,.47,.51),vec3(.14,.22,.22),k*.49)*(.62+pattern*.57);
  float frost=smoothstep(.42,.68,u.y+pattern*.135);
  c=mix(c,mix(rock,vec3(.82,.84,.80),frost*.89),h*smoothstep(.06,.24,valley+k*.09));
 }
 c=mix(c,vec3(.74,.79,.79),band(y-.025,.036)*bank(p,t,10.)*.62);
 return c;
}`,
        'Studio Lightning': `
vec3 scene(vec2 p,vec2 u,float t){
 float y=u.y-.32;
 float flicker=pow(max(0.,sin(t*.53+1.2)),45.)*(.65+.35*sin(t*31.));
 float cloud=bank(p,t,3.)*.68+bank(p*.8+vec2(.2,.11),t,1.3)*.34;
 vec3 c=ramp(vec3(.19,.22,.34),vec3(.015,.036,.09),smoothstep(-.18,.44,p.y))+vec3(.34,.36,.57)*cloud*(.23+.79*flicker);
 float path=.29+.12*(p.y-.2)+.030*sin(31.*p.y)+.021*sin(79.*p.y)+.010*sin(187.*p.y);
 float lightning=band(p.x-path,.0025)+band(p.x-path,.024)*.26;
 float split=band(p.x-path+.40*(.19-p.y),.0016)*smoothstep(-.04,.11,p.y)*(1.-smoothstep(.14,.21,p.y));
 c+=vec3(.78,.79,1.)*(lightning+split*.61)*flicker*smoothstep(-.16,-.09,p.y)*(1.-smoothstep(.32,.42,p.y));
 if(y<0.)c=waterBands(p,y,t,vec2(path,.10),vec3(.013,.021,.052),vec3(.075,.12,.21)) + c*flicker*.14;
 for(int i=0;i<3;i++){float k=float(i);vec2 q=rotate(-.26)*p;float rain=band(fract(q.x*141.+k*.573)-.5,.017)*smoothstep(.81,.98,noise(vec2(q.x*145.,(q.y+t*.75)*49.+k)));c+=vec3(.42,.50,.66)*rain*.14;}
 return c;
}`,
        'Studio Kelp': `
vec3 scene(vec2 p,vec2 u,float t){
 float cloud=fb(p*3.+vec2(t*.042,0.));
 float light=pow(max(0.,sin(17.*(p.x+.23*u.y)/(1.17-u.y)+cloud*3.+t*.2)),6.);
 vec3 c=ramp(vec3(.005,.032,.053),vec3(.11,.53,.50),u.y*u.y)+vec3(.40,.70,.51)*light*u.y*.31;
 for(int i=0;i<9;i++){
  float k=float(i), x=(hash(vec2(k,3.))-.5)*1.8, h=.25+hash(vec2(k,8.))*.57;
  float bend=(sin(t*.44+k+u.y*5.)*.072+sin(u.y*9.+k)*.046)*u.y;
  float stem=band(p.x-x-bend,.0025)*step(u.y,h);
  float leaf=abs(p.x-x-bend-.025*sin(u.y*48.+k));
  float frond=step(leaf,.029*pow(abs(sin(u.y*48.+k)),2.))*(1.-smoothstep(h-.05,h,u.y));
  c=mix(c,vec3(.023,.15,.105)+vec3(.044,.24,.102)*hash(vec2(k,3.)), max(stem,frond)*.90);
 }
 for(int i=0;i<8;i++){float k=float(i);vec2 f=vec2(fract(t*.034+k*.13)*1.96-.98,.61+.03*sin(t*.62+k)+k*.016);vec2 d=p-f; c=mix(c,vec3(.13,.38,.41)+vec3(.45,.52,.33)*band(d.y,.001),finFish(d,t*4.+k)*.86);}
 c+=vec3(.21,.47,.43)*stars(p+vec2(.02*t,-.045*t),t,63.)*1.1;
 return c;
}`,
        'Studio Atoll': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 q=rotate(.34)*p;
 float warp=fb(q*4.+1.)+.41*fb(q*13.+6.);
 float sand=smoothstep(-.03,.02,q.y+.18*warp+.2*sin(q.x*3.)-.22);
 float shallows=band(q.y+.18*warp+.2*sin(q.x*3.)-.22,.13);
 vec3 c=mix(vec3(.018,.20,.33),vec3(.055,.68,.63),clamp(shallows*.86+warp*.13,0.,1.));
 float caustic=band(sin(q.x*38.+sin(q.y*34.+t*.36))+sin(q.y*39.+sin(q.x*31.-t*.4)),.13);
 c+=vec3(.39,.52,.33)*caustic*pow(shallows,.55)*.33;
 float foam=band(q.y+.18*warp+.2*sin(q.x*3.)-.22+.020*sin(t*1.1-q.x*7.),.014)*(.52+.48*noise(q*113.+t));
 c=mix(c,vec3(.86,.75,.55)+vec3(.16,.10,.032)*noise(q*285.),sand);
 c=mix(c,vec3(.83,.96,.94),foam*.85);
 return c;
}`,
        'Studio Medusa': `
vec3 scene(vec2 p,vec2 u,float t){
 float haze=fb(p*5.+vec2(0.,t*.051));
 vec3 c=ramp(vec3(.016,.018,.082),vec3(.06,.14,.29),u.y)+vec3(.15,.08,.25)*haze*.44;
 for(int i=0;i<4;i++){
  float k=float(i), radius=.048+.038*k;
  vec2 q=p-vec2(-.51+.32*k+.036*sin(t*.47+k),.06+.098*sin(k*2.+t*.4));
  q.x*=1.25+.12*cos(t*1.55+k);
  float capY=max(0.,q.y*1.65);
  float r=length(vec2(q.x,capY));float cap=disc(vec2(q.x,capY),radius)*smoothstep(-.008,.015,q.y);
  vec3 hue=.57+.43*cos(vec3(.0,2.1,4.2)+k+1.1);
  c+=hue*(cap*(.11+.17*noise(q*51.))+band(r-radius,.003)*smoothstep(-.002,.01,q.y)*.54);
  for(int j=0;j<6;j++){
   float a=float(j), tendril=q.x- (a-2.5)*radius*.23 +.013*sin(q.y*38.+t*1.48+a);
   c+=hue*band(tendril,.0017)*step(q.y,0.)*exp(q.y*(9.+a))*smoothstep(-.28,-.06,q.y)*.58;
  }
 }
 c+=vec3(.32,.37,.68)*stars(p+vec2(-t*.012,-t*.021),t,70.);
 return c;
}`,
        'Studio Reef': `
vec3 scene(vec2 p,vec2 u,float t){
 vec3 c=ramp(vec3(.01,.06,.13),vec3(.07,.38,.53),u.y);
 for(int i=0;i<3;i++){
  float k=float(i), ground=.32-k*.082+dryRidge(p.x*5.+k,k)*.10;
  float z=12.+k*6., id=floor(p.x*z), seed=hash(vec2(id,k));
  float branches=sin(p.x*z*3.6+u.y*34.+seed*11.)*sin(u.y*39.-p.x*z*.6);
  float fan=(1.-smoothstep(.03+seed*.13,.055+seed*.13,u.y-ground))*step(0.,u.y-ground)*step(.24,branches);
  vec3 coral=ramp(vec3(.17,.22,.33),vec3(.7,.28,.23),k*.47)*(.52+.65*fb(p*32.));
  c=mix(c,coral,max(1.-smoothstep(ground,ground+.002,u.y),fan));
 }
 for(int i=0;i<12;i++){
  float k=float(i),seed=hash(vec2(k,44.));vec2 q=p-vec2(fract(seed+t*.034)*1.95-.975,.02+.25*seed+.019*sin(t*1.1+k));
  q*=.84+seed*.46;float mask=finFish(q,t*6.1+k);
  vec3 fish=ramp(vec3(.86,.67,.24),vec3(.33,.82,.62),seed)*(0.66+.34*cos(q.x*335.));
  c=mix(c,fish,mask*.87);
 }
 c+=vec3(.55,.75,.70)*stars(p+vec2(0.,-t*.026),t,74.)*.64;
 return c;
}`,
        'Studio Lunar': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 d=p-vec2(-.19+.018*sin(t*.09),.09);
 float r=length(d), mask=disc(d,.205);
 vec3 c=vec3(.018,.035,.09)+vec3(.27,.42,.73)*stars(p,t,88.)*1.4;
 c+=vec3(.24,.34,.50)*band(r-.208,.066)*.24;
 c=mix(c,lunar(d,.205,-.36+.16*sin(t*.15)),mask);
 vec2 sky=rotate(.23)*p;
 float dust=bank(sky,t,11.)*band(sky.y+.19,.11);
 c=mix(c,vec3(.09,.15,.29),dust*.39);
 float horizon=.15+.10*dryRidge(p.x*4.,84.);
 c=mix(c,vec3(.025,.036,.067)+.057*fb(p*41.),1.-smoothstep(horizon,horizon+.003,u.y));
 return c;
}`,
        'Studio Saturn': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 q=rotate(.30)*(p-vec2(.04,.044));
 float rad=length(q), r=.194;
 vec2 ringQ=q/vec2(1.,.29);
 float rr=length(ringQ), rim=smoothstep(.23,.242,rr)*(1.-smoothstep(.43,.438,rr));
 float grains=sin(rr*175.+noise(ringQ*7.)*1.41)+.55*sin(rr*381.);
 vec3 c=vec3(.016,.026,.061)+vec3(.38,.52,.79)*stars(p,t,81.);
 vec3 ring=mix(vec3(.42,.27,.19),vec3(.91,.75,.51),.56+.27*grains)*(.70+.30*sin(t*.28+ringQ.x*5.));
 c=mix(c,ring,rim*.8);
 float latitude=q.y+noise(q*14.+vec2(t*.04,0.))*.013;
 float lit=smoothstep(-.19,.12,-q.x+q.y*.42)*sqrt(max(0.,1.-rad/r*.58));
 c=mix(c,vec3(.53,.39,.24)*(lit+.18)+lit*vec3(.37,.24,.09)*(sin(latitude*101.)*.14+sin(latitude*47.)*.18),disc(q,r));
 if(q.y<0.) c=mix(c,ring,rim*.93);
 return c;
}`,
        'Studio Meteor': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 q=rotate(.44)*p;
 float veil=fb(q*8.+vec2(t*.015,0.));
 vec3 c=vec3(.014,.024,.059)+vec3(.28,.31,.61)*band(q.y+.08,.20)*pow(veil,2.)*1.27;
 c+=vec3(.82,.86,1.)*(stars(p,t,111.)+stars(p+2.7,t,62.))*.82;
 for(int i=0;i<5;i++){
  float k=float(i),a=fract(t*(.045+k*.003)+k*.273);
  vec2 d=rotate(-.35)*(p-vec2(1.16-2.3*a,-.11+.44*a+.058*k));
  float tail=band(d.y,.0019+k*.00012)*smoothstep(-.18,0.,d.x)*(1.-smoothstep(.009,.026,d.x));
  float flash=band(length(d),.007);
  c+=vec3(.45,.73,1.)*(tail*.63+flash)*sin(a*PI);
 }
 float hill=.16+dryRidge(p.x*3.4,5.)*.19;
 c=mix(c,vec3(.022,.051,.069),1.-smoothstep(hill-.002,hill+.002,u.y));
 return c;
}`,
        'Studio Corona': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 q=p-vec2(.02,.023);
 float r=length(q),angle=atan(q.y,q.x);
 float ridges=fb(vec2(14.*cos(angle),14.*sin(angle))+r*15.+t*.08);
 float mask=disc(q,.156);
 vec2 solar=rotate(t*.026)*q;
 float granule=noise(solar*286.+4.)*.46+fb(solar*63.+vec2(t*.15,0.))*.63;
 vec3 heat=ramp(vec3(.64,.094,.020),vec3(1.,.81,.35),clamp(granule*.9+.07,0.,1.));
 heat*=.63+.37*sqrt(max(0.,1.-r*r/.025));
 float flare=pow(max(0.,sin(angle*5.+ridges+t*.19)),6.)*band(r-.161,.091)*ridges;
 vec3 c=vec3(.025,.014,.041)+vec3(.56,.21,.11)*(band(r-.165,.068)*ridges*1.5+flare*1.31);
 c+=vec3(1.,.23,.07)*band(r-.158,.004)*(1.-mask);
 return mix(c,heat,mask);
}`,
        'Studio Opal': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 q=rotate(.33+t*.014)*p;
 float ripple=fb(q*7.+t*.052)+.18*sin(q.x*10.+q.y*8.);
 float f=.5+.5*sin(13.*q.y+9.*ripple);
 vec3 spectrum=.58+.42*cos(vec3(.02,2.1,4.2)+f*4.2+ripple*7.);
 vec3 c=ramp(vec3(.05,.09,.15),vec3(.11,.21,.27),ripple);
 vec2 a=q*vec2(1.3,1.)+vec2(0.,.075);
 float shape=1.-smoothstep(.33,.338,length(a)+.038*sin(9.*atan(a.y,a.x)));
 float plates=smoothstep(.34,.72,fb(q*19.+f*2.))*.8;
 c=mix(c,spectrum*(.28+.68*plates)+vec3(.18,.19,.23),shape);
 c+=vec3(.92,.85,.80)*band(f-.58,.041)*shape*.35;
 c+=vec3(.44,.72,.69)*band(length(a)-.34,.053)*.34;
 return c;
}`,
        'Studio Obsidian': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 q=rotate(.45)*p;
 float drift=fb(q*3.4+vec2(t*.036,-t*.033));
 float stone=fb(q*11.+drift*2.4);
 float ridges=sin(14.*(q.y+stone*.3)+drift*5.);
 float gold=band(ridges-.46,.045)*smoothstep(.3,.7,drift);
 float chips=smoothstep(.43,.72,noise(q*93.+7.*drift))*.065;
 vec3 c=ramp(vec3(.038,.041,.060),vec3(.16,.17,.198),stone)+chips;
 c+=vec3(.70,.45,.14)*(gold*.87+band(ridges-.52,.10)*.10);
 c+=vec3(.30,.33,.36)*band(q.x+.4*q.y-.27*sin(t*.31),.17)*(.05+.5*gold);
 return c;
}`,
        'Studio Silk': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 q=rotate(.50+.09*sin(t*.24))*p;
 float wave=q.y+.16*sin(q.x*3.6+t*.42)+.072*cos(q.x*7.5-t*.51)+fb(q*3.)*.06;
 float folds=sin(wave*29.+q.x*.7), y=cos(wave*28.+.5*sin(q.x*4.));
 float fibres=.5+.5*sin((q.x*180.+wave*28.)+fb(q*95.)*.81);
 float sheen=pow(max(0.,y),8.)*(.29+.71*band(q.x-.22*sin(t*.41),.39));
 vec3 c=ramp(vec3(.063,.059,.18),vec3(.41,.15,.38),.5+.5*folds);
 c+=vec3(.33,.45,.59)*sheen*.67+c*fibres*.13;
 return c;
}`,
        'Studio Petals': `
vec3 scene(vec2 p,vec2 u,float t){
 vec3 c=ramp(vec3(.13,.09,.24),vec3(.36,.22,.38),u.y)+vec3(.12,.19,.23)*fb(p*7.+vec2(t*.034,0.));
 for(int i=0;i<19;i++){
  float k=float(i),h=hash(vec2(k,5.1)),z=.4+h*.7;
  vec2 center=vec2((hash(vec2(k,2.))-.5)*1.85+.065*sin(t*.71+k),fract(h-t*(.027+h*.022))-.5);
  vec2 d=rotate(k+t*(.55+h))*((p-center)/(.033*z));
  d.y+=.23*sin(d.x*2.);
  float l=length(d*vec2(.7,1.)),m=1.-smoothstep(.93,1.,l);
  float vein=band(d.y-.08*sin(d.x*3.),.05)*.20;
  vec3 petal=ramp(vec3(.67,.23,.42),vec3(1.,.67,.77),.5+.5*cos(d.x-d.y));
  c=mix(c,petal*(.78+vein),m*(.50+.50*z));
 }
 return c;
}`,
        'Studio Canyon': `
vec3 scene(vec2 p,vec2 u,float t){
 float current=p.x-.19*sin(u.y*3.+t*.03);
 vec3 c=skyLight(p,t,vec2(.38,.23),vec3(.91,.66,.48),vec3(.39,.51,.63));
 for(int i=0;i<5;i++){
  float k=float(i), dist=abs(current+.024*k);
  float crest=.61+dryRidge(p.x*(5.+k)+k*5.,8.+k)*.25-k*.040;
  float canyon=smoothstep(.14+k*.024,.22+k*.025,dist);
  float ridgeLines=sin(u.y*(111.+k*7.)+.9*sin(p.x*7.+k)+4.*fb(p*10.));
  vec3 base=mix(vec3(.54,.29,.24),vec3(.34,.105,.080),k*.23);
  c=mix(c,base*(.53+.64*fb(vec2(p.x*34.,u.y*12.))+ridgeLines*.095),canyon*(1.-smoothstep(crest,crest+.002,u.y)));
 }
 float bed=.16+.023*sin(p.x*9.);
 if(u.y<bed)c=mix(c,waterBands(p,u.y-bed,t,vec2(.19,.02),vec3(.13,.20,.20),vec3(.38,.43,.30)),(1.-smoothstep(.03,.27,abs(current)))*.85);
 return c;
}`,
        'Studio Snowfall': `
vec3 scene(vec2 p,vec2 u,float t){
 vec3 c=skyLight(p,t,vec2(-.58,.19),vec3(.82,.80,.79),vec3(.32,.49,.63));
 for(int i=0;i<4;i++){
  float k=float(i),z=10.+k*11.,x=p.x+k*.9;
  float ground=.60-k*.128+.13*dryRidge(x*4.,k+77.);
  float h=.065+.12*hash(vec2(floor(x*z),k))+k*.005;
  float tree=spruce(vec2((fract(x*z)-.5)/z,u.y-ground),h,k*.711);
  vec3 ice=ramp(vec3(.69,.76,.80),vec3(.31,.46,.58),k*.33);
  c=mix(c,ice*(.87+.24*noise(p*46.)),max(tree,1.-smoothstep(ground,ground+.002,u.y)));
  c+=vec3(.92,.95,1.)*stars(p+vec2(t*(.016+k*.008),t*(.025+k*.022)),t,68.-k*14.)*(.38+k*.12);
 }
 return c;
}`,
        'Studio Fireflies': `
vec3 scene(vec2 p,vec2 u,float t){
 vec2 m=p-vec2(.27,.255);
 vec3 c=ramp(vec3(.12,.21,.26),vec3(.019,.048,.10),u.y);
 c+=vec3(.51,.55,.64)*band(length(m),.102)*.35;
 c=mix(c,lunar(m,.070,.53),disc(m,.070));
 for(int i=0;i<4;i++){
  float k=float(i),z=10.+k*9.,x=p.x+k*.61;
  float ground=.54-k*.095+.15*dryRidge(x*2.,4.+k);
  float tree=spruce(vec2((fract(x*z)-.5)/z,u.y-ground),.08+.12*hash(vec2(floor(z*x),k)),k*.71);
  c=mix(c,vec3(.07,.19,.16)/(1.+k*.52),max(tree,1.-smoothstep(ground,ground+.002,u.y)));
 }
 for(int i=0;i<18;i++){
  float k=float(i), h=hash(vec2(k,9.));vec2 d=p-vec2(hash(vec2(k,3.))*1.7-.85+.05*sin(t*.44+k),-.37+h*.39+.04*sin(t*.73+k));
  float glow=.17+.83*pow(.5+.5*sin(t*(.9+h)+k),3.);
  c+=vec3(1.,.76,.29)*glow*(band(length(d),.024)*.23+band(length(d),.004)*.97);
 }
 return c;
}`
    };
    const library = { optics, scenes };
    if (typeof module !== 'undefined' && module.exports) module.exports = library;
    else root.OpsisStudioLandscapes = library;
})(globalThis);
