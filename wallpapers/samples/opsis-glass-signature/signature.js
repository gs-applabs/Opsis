'use strict';
(() => {
 const canvas=document.getElementById('fx'), image=document.getElementById('scene'), g=canvas.getContext('2d');
 image.src="scene.webp";
 const mask=new Image(); mask.src='reflection-mask.webp';
 const light=document.createElement('canvas'), l=light.getContext('2d');
 const refraction=document.createElement('canvas'), rctx=refraction.getContext('2d');
 const edgeMask=document.createElement('canvas'), em=edgeMask.getContext('2d');
 const scratch=document.createElement('canvas'), scratchCtx=scratch.getContext('2d');
 let maskKey='', effectW=1,effectH=1, disposed=false, zoneKey='', zoneGradients=[];
 // CPU effects are intentionally rendered at at most 960x540, and composed over the untouched master.
 // Preserve the entire central glass emblem, even on portrait or cropped displays.
 const media=matchMedia('(prefers-reduced-motion: reduce)');
 let w=0,h=0,raf=0,wake=0,last=0,previous=0,elapsed=0,playing=true,manual=false,rendered=0;
 const profiles={Eco:{fps:15,max:1280},Balanced:{fps:30,max:1920},High:{fps:30,max:3840}};
 let quality='Balanced',strength=1,speed=1,waves=.8,glints=true,readyState=false;
 let rendererPreference='Auto', adaptive=true, depth=.65, floor=.65, layerDepth=.7;
 // Profiles tint only the animated illumination; the approved master stays untouched.
 const palettes={Original:[156,228,255],Ice:[203,244,255],Sapphire:[111,160,255],Amethyst:[192,150,255],Gold:[255,212,146]};
 let palette='Original', bloom=.45, dispersion=.2, staticView=false;
 function tint(){return palettes[palette];}
 // Soft shoulder for bright specular contributions (SDR display mapping, not HDR10).
 function shoulder(x){x=Math.max(0,x);return x<=.65?x:.65+.35*(1-Math.exp(-(x-.65)/.35));}
 let hostPaused=false, hostPolicy={targetFps:30,renderScale:1,pixelBudget:8294400,reducedMotion:false};
 const effectiveFps=()=>Math.min(profiles[quality].fps,hostPolicy.targetFps);
 const canAnimate=()=>!disposed&&readyState&&!document.hidden&&!media.matches&&!hostPolicy.reducedMotion&&!hostPaused&&playing&&!manual&&!staticView&&strength>0;
 function applyHostPolicy(p){
   const bounded=(v,f,min,max)=>Number.isFinite(v)?Math.min(max,Math.max(min,v)):f;
   const next={targetFps:bounded(p.targetFps,30,1,30),renderScale:bounded(p.renderScale,1,.15,1),pixelBudget:bounded(p.pixelBudget,8294400,76800,8294400),reducedMotion:p.reducedMotion===true};
   if(Object.keys(next).every(k=>next[k]===hostPolicy[k]))return;
   hostPolicy=next;governor.reset();dimensions();frame(elapsed);sync();
 }
 const governor=window.OpsisAdaptiveQuality(); let gpuW=1,gpuH=1, measuredCost=0, measuredGap=0;
 const gpu=document.getElementById('gpu'); let gl=null, program=null, uniforms=null, textures=[], vertexBuffer=null, gpuReady=false, fallbackReason='initializing';
 const vertex=`attribute vec2 position; varying vec2 uv; void main(){uv=position*.5+.5; gl_Position=vec4(position,0.,1.);}`;
 const fragment=`precision highp float; varying vec2 uv; uniform sampler2D scene, mask; uniform vec2 coverScale; uniform float phase, intensity, waveStrength, glintStrength, depthStrength, floorStrength, layerSeparation, bloomStrength, dispersionStrength; uniform vec3 lightTint;
 float bell(float x){return exp(-x*x);}
 float shoulder(float x){return x<=.65?max(0.,x):.65+.35*(1.-exp(-(x-.65)/.35));}
 void main(){vec2 q=(uv-.5)*coverScale+.5;
 // Outside the central orb only: tiny continuous ripples across the ribbons.
 // The emblem's image coordinates remain invariant.
 vec2 d=q-vec2(.493,.55); float r=length(d*vec2(1.78,1.));
 float outside=smoothstep(.46,.58,r);
 float edge=smoothstep(.005,.055,q.x)*smoothstep(.005,.055,1.-q.x)*smoothstep(.005,.055,q.y)*smoothstep(.005,.055,1.-q.y);
 float band=smoothstep(.06,.22,q.y)*(1.-smoothstep(.43,.68,q.y));
 float p=phase;
 float pSide=p+layerSeparation*mix(-.8,.8,smoothstep(.35,.65,q.x));
 float a=sin(q.x*15.+q.y*9.+pSide), b=sin(q.x*22.-q.y*12.-2.*pSide);
 vec2 shift=vec2(.0016*a,.0042*(.66*a+.34*b))*outside*band*edge*waveStrength;
 float floorMask=(1.-smoothstep(.095,.138,q.y))*smoothstep(.002,.045,q.y)*edge;
 float ripple=sin(q.y*110.+p)*.65+sin(q.y*57.-2.*p)*.35;
 shift.x+=.008*ripple*floorMask*floorStrength*intensity;
 vec2 sampleUV=clamp(q+shift,vec2(0.),vec2(1.));
 vec3 color=texture2D(scene,sampleUV).rgb;
 float m=texture2D(mask,q).a;
 float sweep=bell((q.x-(.50+.34*sin(p))+.24*(q.y-.5))/.115);
 float rim=bell((r-.413)/.025);
 float angle=atan(d.y,d.x*1.78);
 float movingRim=(.75*pow(max(0.,.5+.5*cos(angle-p)),18.)+.25*pow(max(0.,.5+.5*cos(angle+2.*p+1.2)),12.))*rim;
 float floorGlow=bell((q.y-.095)/.075)*bell((q.x-.50-.11*sin(p))/.30);
 float ribbonGlow=outside*band*pow(.5+.5*sin(q.x*15.+q.y*9.+p),7.);
 float light=intensity*m*(.13*sweep+.34*movingRim*glintStrength+.095*ribbonGlow*waveStrength);
 color=1.-(1.-color)*(1.-lightTint*shoulder(light));
 color=1.-(1.-color)*(1.-lightTint*vec3(.36,.59,.82)*floorGlow*.10*intensity);
 // A narrow travelling caustic follows the inner glass, without moving the artwork.
 float inner=bell((r-.34)/.038)*pow(max(0.,.5+.5*cos(angle+1.2-p)),10.);
 float shadow=bell((q.y-.073)/.035)*bell((q.x-.51+.025*sin(p))/.20);
 color*=1.-shadow*.11*depthStrength*intensity;
 float reflected=bell((q.x-.49-.14*sin(p+.8))/.13)*floorMask;
 color=1.-(1.-color)*(1.-lightTint*vec3(.44,.77,.95)*reflected*.17*floorStrength*intensity);
 color=1.-(1.-color)*(1.-lightTint*vec3(.60,.89,1.)*inner*m*.16*depthStrength*intensity);
 // Masked soft bloom and subtle spectral edge separation: no movement of emblem pixels.
 float halo=bell((r-.413)/.055)*pow(max(0.,.5+.5*cos(angle-p)),8.);
 float blueEdge=bell((r-.421)/.010)*pow(max(0.,.5+.5*cos(angle-p)),16.);
 float warmEdge=bell((r-.405)/.010)*pow(max(0.,.5+.5*cos(angle-p)),16.);
 vec3 spec=lightTint*halo*bloomStrength*.24+dispersionStrength*.13*(vec3(.2,.65,1.)*blueEdge+vec3(1.,.68,.35)*warmEdge);
 color=1.-(1.-color)*(1.-clamp(spec*m*intensity,0.,.85));
 gl_FragColor=vec4(clamp(color,0.,1.),1.);}`;
 function releaseGPU(){
   if(gl && !gl.isContextLost()) { for(const t of textures)gl.deleteTexture(t); if(program)gl.deleteProgram(program); if(vertexBuffer)gl.deleteBuffer(vertexBuffer); }
   textures=[];program=null;vertexBuffer=null;gpuReady=false;
   if(gpu.width!==1||gpu.height!==1){gpu.width=1;gpu.height=1;}
 }
 function initGPU(){
   releaseGPU();
   let vs=null,fs=null;
   try {gl=gpu.getContext('webgl',{alpha:false,antialias:false,preserveDrawingBuffer:false,powerPreference:'low-power'}); if(!gl)throw Error('WebGL unavailable');
   function shader(type,code){const sh=gl.createShader(type);gl.shaderSource(sh,code);gl.compileShader(sh);if(!gl.getShaderParameter(sh,gl.COMPILE_STATUS)){const err=gl.getShaderInfoLog(sh);gl.deleteShader(sh);throw Error(err);}return sh;}
   program=gl.createProgram();vs=shader(gl.VERTEX_SHADER,vertex);fs=shader(gl.FRAGMENT_SHADER,gl.getShaderPrecisionFormat(gl.FRAGMENT_SHADER,gl.HIGH_FLOAT)?.precision ? fragment : fragment.replace("precision highp float","precision mediump float"));gl.attachShader(program,vs);gl.attachShader(program,fs);gl.linkProgram(program);if(!gl.getProgramParameter(program,gl.LINK_STATUS))throw Error(gl.getProgramInfoLog(program));gl.useProgram(program);gl.deleteShader(vs);gl.deleteShader(fs);vs=fs=null;
   const buffer=vertexBuffer=gl.createBuffer();gl.bindBuffer(gl.ARRAY_BUFFER,buffer);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array([-1,-1,1,-1,-1,1,1,1]),gl.STATIC_DRAW);const pos=gl.getAttribLocation(program,'position');gl.enableVertexAttribArray(pos);gl.vertexAttribPointer(pos,2,gl.FLOAT,false,0,0);
   uniforms=Object.fromEntries(['scene','mask','coverScale','phase','intensity','waveStrength','glintStrength','depthStrength','floorStrength','layerSeparation','lightTint','bloomStrength','dispersionStrength'].map(k=>[k,gl.getUniformLocation(program,k)]));
   gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL,true);
   [image,mask].forEach((im,i)=>{const t=gl.createTexture();textures.push(t);gl.activeTexture(gl.TEXTURE0+i);gl.bindTexture(gl.TEXTURE_2D,t);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.LINEAR);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,gl.RGBA,gl.UNSIGNED_BYTE,im);});
   if(gl.getError()!==gl.NO_ERROR)throw Error('Texture upload failed');gl.uniform1i(uniforms.scene,0);gl.uniform1i(uniforms.mask,1);gpuReady=true;gpu.width=gpuW;gpu.height=gpuH;fallbackReason='';
   }catch(e){if(gl&&!gl.isContextLost()){if(vs)gl.deleteShader(vs);if(fs)gl.deleteShader(fs);}releaseGPU();fallbackReason=String(e);gpu.style.display='none';}
 }
 function gpuFrame(sec){if(rendererPreference==='Canvas2D'||!gpuReady||gl.isContextLost())return false;
   const ratio=(w/h)/(image.naturalWidth/image.naturalHeight);gl.viewport(0,0,gpuW,gpuH);gl.uniform2f(uniforms.coverScale,Math.min(1,ratio),Math.min(1,1/ratio));gl.uniform1f(uniforms.phase,((sec%period+period)%period)/period*TAU);gl.uniform1f(uniforms.intensity,strength);gl.uniform1f(uniforms.waveStrength,waves*strength);gl.uniform1f(uniforms.glintStrength,glints?1:0);gl.uniform1f(uniforms.depthStrength,depth);gl.uniform1f(uniforms.floorStrength,floor);gl.uniform1f(uniforms.layerSeparation,layerDepth);gl.uniform3f(uniforms.lightTint,...tint().map(v=>v/255));gl.uniform1f(uniforms.bloomStrength,bloom);gl.uniform1f(uniforms.dispersionStrength,dispersion);gl.drawArrays(gl.TRIANGLE_STRIP,0,4);gpu.style.display='block';canvas.style.display='none';return true;
 }
 gpu.addEventListener('webglcontextlost',e=>{e.preventDefault();gpuReady=false;textures=[];program=null;vertexBuffer=null;uniforms=null;fallbackReason='context lost';gpu.style.display='none';frame(elapsed);});
 gpu.addEventListener('webglcontextrestored',()=>{if(!disposed&&readyState && rendererPreference!=='Canvas2D'){initGPU();dimensions();frame(elapsed);}});
 const TAU=2*Math.PI,period=18;
 function dimensions(){
   const dpr=Math.max(1,Math.min(2,devicePixelRatio||1));
   const scale=Math.min(dpr*hostPolicy.renderScale,profiles[quality].max/Math.max(innerWidth,innerHeight),Math.sqrt(hostPolicy.pixelBudget/Math.max(1,innerWidth*innerHeight)));
   w=Math.max(1,Math.floor(innerWidth*scale));h=Math.max(1,Math.floor(innerHeight*scale));
   const factor=adaptive?governor.state().scale:1;
   const viewport=gl&&!gl.isContextLost()?gl.getParameter(gl.MAX_VIEWPORT_DIMS):[3840,3840];
   const gpuScale=Math.min(factor,viewport[0]/w,viewport[1]/h);
   gpuW=Math.max(1,Math.floor(w*gpuScale));gpuH=Math.max(1,Math.floor(h*gpuScale));
   const fxScale=Math.min(1,960/Math.max(w,h),Math.sqrt(518400/(w*h)))*factor;
   effectW=Math.max(1,Math.round(w*fxScale));effectH=Math.max(1,Math.round(h*fxScale));
   if(canvas.width!==effectW||canvas.height!==effectH){
     for(const c of [canvas,light,refraction,edgeMask,scratch]){c.width=effectW;c.height=effectH;}
     maskKey='';
   }
   if(gpuReady&&(gpu.width!==gpuW||gpu.height!==gpuH)){gpu.width=gpuW;gpu.height=gpuH;}
 }
 function bounds(img,width=effectW,height=effectH){const s=Math.max(width/img.naturalWidth,height/img.naturalHeight),iw=img.naturalWidth*s,ih=img.naturalHeight*s;return {x:(width-iw)/2,y:(height-ih)/2,w:iw,h:ih};}
 function cover(ctx,img){const b=bounds(img);ctx.drawImage(img,b.x,b.y,b.w,b.h);}
 function smooth(x){x=Math.max(0,Math.min(1,x));return x*x*(3-2*x);}
 function prepareEdgeMask(b){
   const key=[effectW,effectH,b.x,b.y,b.w,b.h].join('|');if(maskKey===key)return;
   const pixels=em.createImageData(effectW,effectH), data=pixels.data;
   for(let y=0;y<effectH;y++)for(let x=0;x<effectW;x++){
     const u=(x+.5-b.x)/b.w,v=(y+.5-b.y)/b.h;
     const r=Math.hypot((u-.493)*1.78,v-.45);
     const outer=smooth((r-.46)/.12);
     const sides=smooth(u/.055)*smooth((1-u)/.055)*smooth(v/.055)*smooth((1-v)/.055);
     const band=smooth((v-.31)/.20)*(1-smooth((v-.83)/.12));
     data[(y*effectW+x)*4+3]=Math.round(255*outer*sides*band);
   }
   em.putImageData(pixels,0,0);maskKey=key;
 }
 function bendRibbons(b,p){
   if(waves<=0||strength<=0)return;
   prepareEdgeMask(b);
   rctx.clearRect(0,0,effectW,effectH);
   // Overlapping feathered displacement zones: partition-of-unity alpha weights
   // avoid the hard seams of a strip-based approximation.
   const zones=quality==='Eco'?4:6, zk=effectW+'|'+effectH+'|'+zones;
   if(zoneKey!==zk){
     zoneGradients=[];zoneKey=zk;
     for(let i=0;i<zones;i++){
       const center=i/(zones-1), step=1/(zones-1);
       const grad=scratchCtx.createLinearGradient(0,0,effectW,0);
       if(i>0){grad.addColorStop(Math.max(0,center-step),'rgba(0,0,0,0)');
         for(let j=1;j<8;j++)grad.addColorStop(center-step+step*j/8,'rgba(0,0,0,'+smooth(j/8)+')');}
       grad.addColorStop(center,'rgba(0,0,0,1)');
       if(i<zones-1){for(let j=1;j<8;j++)grad.addColorStop(center+step*j/8,'rgba(0,0,0,'+(1-smooth(j/8))+')');
         grad.addColorStop(Math.min(1,center+step),'rgba(0,0,0,0)');}
       zoneGradients.push(grad);
     }
   }
   rctx.globalCompositeOperation='lighter';
   for(let i=0;i<zones;i++){
     const center=i/(zones-1), u=(center*effectW-b.x)/b.w;
     const sidePhase=p+layerDepth*(2*smooth((u-.35)/.30)-1)*.8;
     const dy=(Math.sin(u*15+sidePhase)*.66+Math.sin(u*22-2*sidePhase)*.34)*b.h*.008*waves*Math.min(1,strength);
     // Only paint this zone and its two overlapping neighbors' edges.
     const step=effectW/(zones-1), left=Math.max(0,Math.floor((i-1)*step)),right=Math.min(effectW,Math.ceil((i+1)*step));
     const bw=right-left;
     scratchCtx.clearRect(left,0,bw,effectH);
     scratchCtx.save();scratchCtx.beginPath();scratchCtx.rect(left,0,bw,effectH);scratchCtx.clip();
     scratchCtx.drawImage(image,b.x,b.y+dy,b.w,b.h);
     scratchCtx.globalCompositeOperation='destination-in';scratchCtx.fillStyle=zoneGradients[i];scratchCtx.fillRect(left,0,bw,effectH);
     scratchCtx.restore();rctx.drawImage(scratch,left,0,bw,effectH,left,0,bw,effectH);
   }
   rctx.globalCompositeOperation='destination-in';rctx.drawImage(edgeMask,0,0);rctx.globalCompositeOperation='source-over';
   g.drawImage(refraction,0,0);
 }
 // Separate reflection plane, reusing existing buffers and feathered bands.
 let floorKey='',floorBands=[],floorFade=null;
 function bendFloor(b,p){
   if(floor<=0||strength<=0)return;
   const start=b.y+b.h*.862,end=b.y+b.h;
   if(end<=0||start>=effectH)return;
   const n=quality==='Eco'?4:8,step=(end-start)/(n-1),key=[effectW,effectH,b.x,b.y,b.w,b.h,n].join('|');
   if(floorKey!==key){
     floorKey=key;floorBands=[];
     for(let i=0;i<n;i++){
       const c=i/(n-1),d=1/(n-1),grad=scratchCtx.createLinearGradient(0,start,0,end);
       if(i>0)grad.addColorStop(Math.max(0,c-d),'transparent');
       grad.addColorStop(c,'black');
       if(i<n-1)grad.addColorStop(Math.min(1,c+d),'transparent');
       floorBands.push(grad);
     }
     floorFade=rctx.createLinearGradient(0,start,0,end);
     floorFade.addColorStop(0,'transparent');floorFade.addColorStop(.30,'black');floorFade.addColorStop(.72,'black');floorFade.addColorStop(1,'transparent');
   }
   rctx.clearRect(0,0,effectW,effectH);rctx.globalCompositeOperation='lighter';
   for(let i=0;i<n;i++){
     const y=start+i*step,v=(y-b.y)/b.h;
     const dx=b.w*.009*floor*Math.min(1,strength)*(.65*Math.sin((1-v)*110+p)+.35*Math.sin((1-v)*57-2*p));
     const top=Math.max(0,Math.floor(y-step)),bottom=Math.min(effectH,Math.ceil(y+step)),bh=bottom-top;
     if(bh<=0)continue;
     scratchCtx.clearRect(0,top,effectW,bh);scratchCtx.save();scratchCtx.beginPath();scratchCtx.rect(0,top,effectW,bh);scratchCtx.clip();
     scratchCtx.drawImage(image,b.x,b.y,b.w,b.h);scratchCtx.drawImage(image,b.x+dx,b.y,b.w,b.h);
     scratchCtx.globalCompositeOperation='destination-in';scratchCtx.fillStyle=floorBands[i];scratchCtx.fillRect(0,top,effectW,bh);scratchCtx.restore();
     rctx.drawImage(scratch,0,top,effectW,bh,0,top,effectW,bh);
   }
   rctx.globalCompositeOperation='destination-in';rctx.fillStyle=floorFade;rctx.fillRect(0,0,effectW,effectH);rctx.globalCompositeOperation='source-over';
   g.drawImage(refraction,0,0);
 }
 function grad(ctx,x,y,rx,ry,a,rgb=tint()){
   a=shoulder(a); if(a<=0)return;
   ctx.save();ctx.translate(x,y);ctx.scale(rx,ry);
   const p=ctx.createRadialGradient(0,0,0,0,0,1), mid=rgb.map((v,i)=>Math.round(v*[.44,.80,1][i]));
   p.addColorStop(0,`rgba(${rgb.join(',')},${a})`);p.addColorStop(.22,`rgba(${mid.join(',')},${a*.55})`);p.addColorStop(1,`rgba(${mid.join(',')},0)`);
   ctx.fillStyle=p;ctx.fillRect(-1,-1,2,2);ctx.restore();
 }
 function frame(sec){
  if(!readyState||disposed)return;
  rendered++;
  const p=((sec%period+period)%period)/period*TAU;if(gpuFrame(sec))return;gpu.style.display="none";canvas.style.display="block";g.clearRect(0,0,effectW,effectH);l.clearRect(0,0,effectW,effectH);
  if(!mask.complete||!mask.naturalWidth)return;
  // Specular highlights travel along the existing photographed glass contours.
  const b=bounds(image), X=t=>b.x+b.w*t, Y=t=>b.y+b.h*t;
  bendRibbons(b,p);
  bendFloor(b,p);
  const z=.5+.5*Math.sin(p),x=X(.24+.52*z),y=Y(.40+.16*Math.cos(p));
  grad(l,x,y,b.w*.18,b.h*.32,.40*strength);
  grad(l,X(.5+.18*Math.cos(p)),Y(.49+.34*Math.sin(p)),b.w*.10,b.h*.075,.48*strength);
  grad(l,X(.50+.22*Math.sin(p+.9)),Y(.86),b.w*.23,b.h*.075,.49*strength);
  // Two coherent curved highlight paths travel across the base art, clipped to the glass mask.
  l.save();l.globalCompositeOperation='screen';
  for(let i=0;i<(glints?2:0);i++){
    const t=p+i*Math.PI; const cx=X(.50+.19*Math.sin(t)),cy=Y(.47+.24*Math.cos(t));
    grad(l,cx,cy,b.w*.10,b.h*.024,.24*strength);
  }
  // Independent moving highlights on the external ribbons, not on the logo.
  for(let i=0;i<3;i++){
    const phase=p+i*TAU/3, u=.5+.5*Math.sin(phase);
    const lx=.035+.19*u, rx=.76+.20*u;
    grad(l,X(lx),Y(.48+.26*Math.sin(lx*Math.PI*1.9)),b.w*.075,b.h*.032,.37*strength*waves);
    grad(l,X(rx),Y(.65-.32*Math.sin((rx-.76)*Math.PI*2.1)),b.w*.085,b.h*.038,.37*strength*waves);
  }
  // Two lobe highlights, restricted to the existing reflective surface by the mask below.
  if(bloom>0||dispersion>0){
    const a=p+1.2, cx=.493+.212*Math.cos(a), cy=.45+.375*Math.sin(a);
    grad(l,X(cx),Y(cy),b.w*.095,b.h*.065,.32*bloom*strength);
    const dx=.004*Math.cos(a),dy=.007*Math.sin(a);
    grad(l,X(cx+dx),Y(cy+dy),b.w*.040,b.h*.017,.20*dispersion*strength,[100,190,255]);
    grad(l,X(cx-dx),Y(cy-dy),b.w*.040,b.h*.017,.17*dispersion*strength,[255,195,112]);
  }
  l.restore();
  // Keep all of the animated light on actual highlights in the original art.
  l.globalCompositeOperation='destination-in';cover(l,mask);l.globalCompositeOperation='source-over';
  g.globalCompositeOperation='screen';g.drawImage(light,0,0);
  // Soft caustic on the inner glass and grounded contact shadow: separate controls.
  if(depth>0&&strength>0){
    l.clearRect(0,0,effectW,effectH);
    const a=p+1.2;
    grad(l,X(.493+.17*Math.cos(a)),Y(.45+.28*Math.sin(a)),b.w*.075,b.h*.027,.20*depth*strength);
    l.globalCompositeOperation='destination-in';cover(l,mask);l.globalCompositeOperation='source-over';
    g.drawImage(light,0,0);
    g.globalCompositeOperation='source-over';g.save();g.translate(X(.51-.025*Math.sin(p)),Y(.927));g.scale(b.w*.20,b.h*.035);
    const shade=g.createRadialGradient(0,0,0,0,0,1);shade.addColorStop(0,`rgba(0,5,17,${.10*depth*strength})`);shade.addColorStop(1,'rgba(0,5,17,0)');g.fillStyle=shade;g.fillRect(-1,-1,2,2);g.restore();g.globalCompositeOperation='screen';
  }
  // Quiet, soft reflected light under the orb; no flashes, no moving geometry.
  grad(g,X(.5+.025*Math.sin(p)),Y(.89),b.w*.23,b.h*.10,(.045+.055*(.5+.5*Math.cos(p)))*strength);
  if(floor>0&&strength>0){
    g.save();g.beginPath();g.rect(0,Y(.865),effectW,effectH);g.clip();
    grad(g,X(.49+.14*Math.sin(p+.8)),Y(.945),b.w*.13,b.h*.052,.18*floor*strength);g.restore();
  }
  g.globalCompositeOperation='source-over';
 }
 function stop(){if(raf)cancelAnimationFrame(raf);if(wake)clearTimeout(wake);raf=wake=0;previous=0;last=0;}
 function schedule(delay=0){if(delay>3){wake=setTimeout(()=>{wake=0;raf=requestAnimationFrame(tick);},delay);}else raf=requestAnimationFrame(tick);}
 function tick(t){raf=0;if(!canAnimate())return;
   if(previous)elapsed=(elapsed+Math.min(.25,(t-previous)/1000)*speed)%period;
   previous=t;
   const interval=1000/effectiveFps();
   if(!last||t-last>=interval-1){
     const start=performance.now(),gap=last?t-last:interval;
     frame(elapsed);const cost=performance.now()-start;
     measuredCost=cost;measuredGap=gap;last=t;
     if(adaptive&&gap<1000&&governor.sample(cost,gap,interval)){dimensions();frame(elapsed);}
   }
   schedule(Math.max(0,interval-(performance.now()-last)-2));
 }
 function sync(){stop();if(!canAnimate())return;schedule();}
 let resizeTimer=0;
 addEventListener('resize',()=>{clearTimeout(resizeTimer);resizeTimer=setTimeout(()=>{if(disposed)return;dimensions();frame(elapsed);},100);});
 document.addEventListener('visibilitychange',sync);media.addEventListener('change',()=>{sync();if(media.matches){frame(elapsed);}});
 addEventListener('pagehide',()=>{disposed=true;stop();clearTimeout(resizeTimer);releaseGPU();});
 addEventListener('pageshow',e=>{if(e.persisted&&readyState){disposed=false;if(rendererPreference!=='Canvas2D')initGPU();frame(elapsed);sync();}});
 // Local API used by the standalone test and available to the host adapter. No network or privileged host calls.
 window.OpsisGlassSignature={
   pause(){playing=false;sync();},resume(){manual=false;playing=true;sync();},
   renderAt(seconds){manual=true;stop();elapsed=((Number(seconds)||0)%period+period)%period;frame(elapsed);},
   configure(options={}){
      if(Object.hasOwn(profiles,options.quality)&&options.quality!==quality){quality=options.quality;governor.reset();dimensions();}
      if(Number.isFinite(options.strength))strength=Math.max(0,Math.min(1.5,options.strength));
      if(Number.isFinite(options.speed))speed=Math.max(.25,Math.min(2,options.speed));
      if(Number.isFinite(options.waves))waves=Math.max(0,Math.min(1,options.waves));
      if(typeof options.adaptive==='boolean'&&options.adaptive!==adaptive){adaptive=options.adaptive;governor.reset();dimensions();}
      if(Number.isFinite(options.depth))depth=Math.max(0,Math.min(1,options.depth));
      if(Number.isFinite(options.floor))floor=Math.max(0,Math.min(1,options.floor));
      if(Number.isFinite(options.layerDepth))layerDepth=Math.max(0,Math.min(1,options.layerDepth));
      if(Object.hasOwn(palettes,options.palette))palette=options.palette;
      if(Number.isFinite(options.bloom))bloom=Math.max(0,Math.min(1,options.bloom));
      if(Number.isFinite(options.dispersion))dispersion=Math.max(0,Math.min(1,options.dispersion));
      if(typeof options.glints==='boolean')glints=options.glints;
      if(typeof options.staticView==='boolean')staticView=options.staticView;
      if(['Auto','Canvas2D'].includes(options.renderer)&&options.renderer!==rendererPreference){
         rendererPreference=options.renderer;
         if(rendererPreference==='Canvas2D'){releaseGPU();fallbackReason='Canvas2D selected';}
         else if(readyState)initGPU();
      }
      frame(elapsed);sync();
   },
   status(){return {renderer:gpuReady&&rendererPreference!=="Canvas2D"?"WebGL":"Canvas2D",fallbackReason,width:w,height:h,effectWidth:effectW,effectHeight:effectH,quality,strength,speed,waves,glints,rendererPreference,adaptive,depth,floor,layerDepth,palette,bloom,dispersion,staticView,adaptiveTier:governor.state().tier,renderScale:adaptive?governor.state().scale:1,gpuWidth:gpuW,gpuHeight:gpuH,lastFrameCpuMs:measuredCost,lastFrameGapMs:measuredGap,ready:readyState,phase:elapsed,playing:canAnimate(),hostPaused,hostPolicy:{...hostPolicy},fps:effectiveFps(),renderedFrames:rendered};}
 };
 let initialized=false;
 function ready(){if(initialized||!image.naturalWidth||!mask.naturalWidth)return;initialized=true;readyState=true;dimensions();if(rendererPreference!=='Canvas2D')initGPU();frame(elapsed);sync();}
 // The installed Opsis host delivers typed manifest properties through its local iframe API.
 // Only the actual parent frame can change these options; no native or network access.
 addEventListener('message',e=>{
   const d=e.data;
   if(window.parent===window || e.source!==window.parent || !d || d.opsis!==true)return;
   if(d.type==='hello' && d.version===1){window.parent.postMessage({opsis:'signature-capabilities',version:1},'*');return;}
   if(d.type==='host-policy' && d.policy && typeof d.policy==='object'){applyHostPolicy(d.policy);return;}
   if(d.type==='pause' && typeof d.paused==='boolean'){
     hostPaused=d.paused;sync();
     if(hostPaused && Number.isSafeInteger(d.seq))window.parent.postMessage({opsis:'signature-paused',seq:d.seq},'*');
     return;
   }
   if(d.type!=='props' || !d.props || typeof d.props!=='object')return;
   const x=d.props;
   window.OpsisGlassSignature.configure({quality:x.quality,strength:x.strength,speed:x.speed,waves:x.waves,glints:x.glints,renderer:x.renderer,adaptive:x.adaptive,depth:x.depth,floor:x.floor,layerDepth:x.layerDepth,palette:x.palette,bloom:x.bloom,dispersion:x.dispersion,staticView:x.staticView});
 });
 mask.addEventListener('load',ready);image.addEventListener('load',ready);dimensions();ready();
})();