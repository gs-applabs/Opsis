// Optional WebGL2 landscape and atmospheric particles for the Weather wallpaper.
(function(root) {
'use strict';
const vertex = `#version 300 es
precision highp float;
layout(location=0) in vec3 position;
uniform float t;
uniform float wind;
uniform float intensity;
uniform float particleSize;
uniform float aspect;
out float alpha;
void main() {
  float speed = mix(0.12, 0.55, intensity);
  float z = -0.55 - mod(position.z + t * speed, 4.8);
  float drift = wind * t * .032;
  float x = mod(position.x + drift + 7.0, 7.0) - 3.5;
  float y = mod(position.y - t * speed * 2.6 + 4.0, 4.0) - 2.0;
  float scale = 1.0 / max(0.5, -z);
  gl_Position = vec4(x * scale / max(0.5, aspect), y * scale, 0.0, 1.0);
  gl_PointSize = clamp(particleSize * 2.2 * scale, 1.0, 20.0);
  alpha = clamp((5.35 + z) * 0.15, 0.0, 0.65) * intensity;
}
`;
const fragment = `#version 300 es
precision highp float;
in float alpha;
uniform float snow;
out vec4 color;
void main() {
  vec2 q = gl_PointCoord - 0.5;
  if (snow > 0.5) {
    float halo = (1.0-smoothstep(0.02, 0.5, length(q)));
    color = vec4(vec3(0.91, 0.94, 1.0), alpha * halo);
  } else {
    float streak = (1.0-smoothstep(0.01, 0.09, abs(q.x))) * (1.0-smoothstep(0.02, 0.48, abs(q.y)));
    color = vec4(vec3(0.72, 0.83, 1.0), alpha * streak);
  }
}
`;
const landscapeVertex = `#version 300 es
layout(location=0) in vec3 pos;
layout(location=1) in vec3 material;
uniform float aspect, angle, distanceToScene;
out vec3 surface, world;
out float depth;
void main() {
  world=pos;
  surface=material;
  float x=cos(angle)*pos.x-sin(angle)*pos.z;
  depth=distanceToScene-sin(angle)*pos.x-cos(angle)*pos.z;
  gl_Position=vec4(1.65*x/aspect,1.65*(pos.y-2.65)+.14*depth,
                   1.002*depth-.22,depth);
}`;
const landscapeFragment = `#version 300 es
precision highp float;
in vec3 surface, world;
in float depth;
uniform float ambient, haze, lightSide, seconds, snowCover;
uniform vec3 horizon;
out vec4 color;
void main() {
  vec3 normal=normalize(cross(dFdx(world),dFdy(world)));
  float lighting=.72+.28*dot(normal,normalize(vec3(lightSide,.9,.45)));
  vec3 lit=surface*lighting*mix(vec3(.18,.23,.35),vec3(1.12,1.08,1.),ambient);
  if (surface.b>surface.r*2. && surface.g>surface.r*2.) {
    float ripple=sin(11.*world.z+.9*seconds+2.*sin(2.5*world.x))*.5+.5;
    lit+=vec3(.09,.11,.12)*ripple*ripple*ambient*exp(-.045*depth);
  } else {
    float grain=sin(world.x*46.+world.z*19.)*sin(world.y*41.+world.z*31.);
    lit*=1.+.032*grain*exp(-.035*depth);
    lit=mix(lit,vec3(.76,.81,.86)*(.25+.75*ambient),snowCover*max(0.,normal.y)*.8);
  }
  color=vec4(mix(lit,horizon,1.-exp(-depth*(.004+.047*haze))),1.);
}`;
let landscapeUniforms, particleUniforms;
let meshProgram=null, meshBuffer=null, meshCount=0, meshKey='';
let gl = null, canvas = null, program = null, buffer = null, count = 0;
let failureReason = '';
let mode = 'Off', blocked = false, reduced = false;
let seed = 0x4f505349;
function random() { seed = (Math.imul(seed, 1664525) + 1013904223) | 0; return (seed >>> 0) / 4294967296; }
function compile(type, source) {
  const sh = gl.createShader(type);
  gl.shaderSource(sh, source);
  gl.compileShader(sh);
  if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) {
    const reason = gl.getShaderInfoLog(sh);
    gl.deleteShader(sh); throw new Error(reason || 'Weather GLSL compilation failed');
  }
  return sh;
}
function link(v,f) {
  const vs=compile(gl.VERTEX_SHADER,v), fs=compile(gl.FRAGMENT_SHADER,f);
  const result=gl.createProgram();
  gl.attachShader(result,vs); gl.attachShader(result,fs); gl.linkProgram(result);
  gl.deleteShader(vs); gl.deleteShader(fs);
  if (!gl.getProgramParameter(result,gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(result));
  return result;
}
function locations(shader, names) {
  return Object.fromEntries(names.map(name => [name, gl.getUniformLocation(shader,name)]));
}
function init() {
  if (blocked || gl) return !blocked && !!gl;
  canvas = document.createElement('canvas');
  canvas.id = 'opsis-weather-spatial';
  canvas.setAttribute('aria-hidden', 'true');
  canvas.style.cssText = 'position:fixed;inset:0;width:100%;height:100%;pointer-events:none;z-index:1;display:none';
  // DOM appended after #waves and before #wxInfo, no input interception.
  const waves = document.getElementById('waves');
  waves.parentNode.insertBefore(canvas, waves.nextSibling);
  try {
    gl = canvas.getContext('webgl2', { alpha:true, premultipliedAlpha:false, antialias:false, depth:true, preserveDrawingBuffer:false, powerPreference:'low-power' });
    if (!gl) throw new Error('WebGL2 unavailable');
    program = link(vertex,fragment);
    meshProgram = link(landscapeVertex,landscapeFragment);
    landscapeUniforms = locations(meshProgram,['aspect','angle','distanceToScene','ambient','haze','lightSide','horizon','seconds','snowCover']);
    particleUniforms = locations(program,['t','wind','intensity','particleSize','snow','aspect']);
    meshBuffer = gl.createBuffer();
    buffer = gl.createBuffer();
    canvas.addEventListener('webglcontextlost', e => {
      e.preventDefault(); blocked=true; canvas.style.display='none';
    });
    canvas.addEventListener('webglcontextrestored', dispose);
    return true;
  } catch (e) {
    failureReason = e.message;
    blocked = true; gl=null; program=null; buffer=null; canvas.remove(); canvas=null;
    return false;
  }
}
function resize(quality) {
  if (!canvas || !gl) return;
  const dpr = Math.min(window.devicePixelRatio || 1, quality === 'High' ? 1.5 : 1);
  const pixels = Math.max(1, window.innerWidth * window.innerHeight * dpr * dpr);
  const budget = quality === 'High' ? 3840*2160 : 2560*1440;
  const scale = dpr * Math.min(1, Math.sqrt(budget / pixels));
  const w = Math.max(1, Math.floor(window.innerWidth * scale));
  const h = Math.max(1, Math.floor(window.innerHeight * scale));
  if (canvas.width !== w) canvas.width = w;
  if (canvas.height !== h) canvas.height = h;
  gl.viewport(0, 0, w, h);
}
function buildParticles(quality) {
  if (!gl) return;
  const n = quality === 'High' ? 400 : quality === 'Balanced' ? 210 : 90;
  if (count === n) return;
  count = n; seed = 0x4f505349;
  const a = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) { a[i*3]=random()*7-3.5; a[i*3+1]=random()*4-2; a[i*3+2]=random()*4.8; }
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  gl.bufferData(gl.ARRAY_BUFFER, a, gl.STATIC_DRAW);
}
function setMode(value) {
  mode = value === '3D' || value === '2.5D' ? value : 'Off';
  if (canvas && mode !== '3D') dispose();
}
function update(dt, weather, quality, noMotion, scene) {
  reduced = !!noMotion;
  if (mode !== '3D' || reduced || quality === 'Battery' || quality === 'Low Power') {
    if (canvas) dispose();
    return;
  }
  const rain = Math.max(0, Math.min(1, Number(weather?.rain) || 0));
  const snow = Math.max(0, Math.min(1, Number(weather?.snow) || 0));
  const intensity = Math.max(rain, snow);
  if (!scene && intensity < 0.035) {
    if (canvas) canvas.style.display = 'none';
    return;
  }
  if (!init()) return;
  canvas.style.display = scene ? 'none' : 'block';
  resize(quality);
  gl.clearColor(0,0,0,0);
  gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
  if (scene) {
    const high=quality==='High', key=scene.id+'|'+high;
    gl.bindBuffer(gl.ARRAY_BUFFER,meshBuffer);
    if (meshKey!==key) {
      const data=root.OpsisWeatherLandscape.build(scene.id,scene.city,high);
      gl.bufferData(gl.ARRAY_BUFFER,data,gl.STATIC_DRAW);
      meshCount=data.length/6; meshKey=key;
    }
    gl.useProgram(meshProgram);
    gl.enable(gl.DEPTH_TEST); gl.disable(gl.BLEND);
    gl.enableVertexAttribArray(0); gl.enableVertexAttribArray(1);
    gl.vertexAttribPointer(0,3,gl.FLOAT,false,24,0);
    gl.vertexAttribPointer(1,3,gl.FLOAT,false,24,12);
    const uniform=(name,value)=>gl.uniform1f(landscapeUniforms[name],value);
    uniform('aspect',canvas.width/canvas.height);
    uniform('angle', (scene.id==='rioantirrio'?.12:.32)+.035*Math.sin(weather.seconds*.08));
    uniform('distanceToScene',Math.max(scene.id==='rioantirrio'?12:8,
      (scene.id==='rioantirrio'?13:5)*canvas.height/canvas.width));
    uniform('seconds',weather.seconds); uniform('snowCover',scene.snowCover);
    uniform('ambient',scene.ambient); uniform('haze',scene.fog); uniform('lightSide',scene.lightSide);
    gl.uniform3f(landscapeUniforms.horizon,...scene.horizon.map(v=>v/255));
    gl.drawArrays(gl.TRIANGLES,0,meshCount);
    gl.disableVertexAttribArray(1); gl.disable(gl.DEPTH_TEST);
  }
  if (intensity < .035) return canvas;
  buildParticles(quality);
  gl.useProgram(program);
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  gl.enableVertexAttribArray(0);
  gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 0, 0);
  gl.uniform1f(particleUniforms.t, Math.max(0, Number(weather?.seconds) || 0));
  gl.uniform1f(particleUniforms.wind, Math.max(-5, Math.min(5, Number(weather?.windX) || 0)));
  gl.uniform1f(particleUniforms.intensity, intensity);
  gl.uniform1f(particleUniforms.particleSize, snow > rain ? 7.0 : 4.0);
  gl.uniform1f(particleUniforms.snow, snow > rain ? 1.0 : 0.0);
  gl.uniform1f(particleUniforms.aspect, canvas.width / Math.max(1,canvas.height));
  gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);

  gl.drawArrays(gl.POINTS, 0, count);
  return scene ? canvas : null;
}
function dispose() {
  if (gl) {
    if (meshBuffer) gl.deleteBuffer(meshBuffer);
    if (meshProgram) gl.deleteProgram(meshProgram);
    if (buffer) gl.deleteBuffer(buffer);
    if (program) gl.deleteProgram(program);
  }
  landscapeUniforms=particleUniforms=null;
  meshKey=''; meshProgram=meshBuffer=null; meshCount=0;
  if (canvas) canvas.remove();
  gl=null; canvas=null; program=null; buffer=null; count=0; blocked=false; failureReason='';
}
root.OpsisWeatherSpatial = { setMode, update, dispose, failureReason: () => failureReason, supported: () => !blocked, mode:() => mode };
})(window);
