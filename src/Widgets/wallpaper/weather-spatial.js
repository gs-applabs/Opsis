// Opsis Weather Spatial Layer — standalone optional WebGL2 atmospheric particles.
// Does not replace the existing Canvas Weather scene; can fail or be disabled safely.
// Real 3D perspective transform for wind-driven rain / snow / haze particles.
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
    float halo = smoothstep(0.5, 0.02, length(q));
    color = vec4(vec3(0.91, 0.94, 1.0), alpha * halo);
  } else {
    float streak = smoothstep(0.09, 0.01, abs(q.x)) * smoothstep(0.48, 0.02, abs(q.y));
    color = vec4(vec3(0.72, 0.83, 1.0), alpha * streak);
  }
}
`;
let gl = null, canvas = null, program = null, buffer = null, count = 0;
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
function init() {
  if (gl || blocked) return !!gl;
  canvas = document.createElement('canvas');
  canvas.id = 'opsis-weather-spatial';
  canvas.setAttribute('aria-hidden', 'true');
  canvas.style.cssText = 'position:fixed;inset:0;width:100%;height:100%;pointer-events:none;z-index:1;display:none';
  // DOM appended after #waves and before #wxInfo, no input interception.
  const waves = document.getElementById('waves');
  waves.parentNode.insertBefore(canvas, waves.nextSibling);
  try {
    gl = canvas.getContext('webgl2', { alpha:true, premultipliedAlpha:false, antialias:false, depth:false, preserveDrawingBuffer:false, powerPreference:'low-power' });
    if (!gl) throw new Error('WebGL2 unavailable');
    const vs = compile(gl.VERTEX_SHADER, vertex), fs = compile(gl.FRAGMENT_SHADER, fragment);
    program = gl.createProgram(); gl.attachShader(program, vs); gl.attachShader(program, fs); gl.linkProgram(program);
    gl.deleteShader(vs); gl.deleteShader(fs);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error('Weather shader link failure');
    // Fixed deterministic particle cloud, allocated only once per quality change.
    buffer = gl.createBuffer();
    canvas.addEventListener('webglcontextlost', e => { e.preventDefault(); gl=null; program=null; buffer=null; blocked=true; canvas.style.display='none'; });
    return true;
  } catch (e) {
    blocked = true; gl=null; program=null; buffer=null; canvas.remove(); canvas=null;
    return false;
  }
}
function resize() {
  if (!canvas || !gl) return;
  const dpr = Math.min(window.devicePixelRatio || 1, 1.5);
  const w = Math.max(1, Math.round(window.innerWidth * dpr));
  const h = Math.max(1, Math.round(window.innerHeight * dpr));
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
  if (canvas) canvas.style.display = mode === '3D' && !reduced ? 'block' : 'none';
}
function update(dt, weather, quality, noMotion) {
  reduced = !!noMotion;
  if (mode !== '3D' || reduced || quality === 'Battery' || quality === 'Low Power') {
    if (canvas) canvas.style.display = 'none';
    return;
  }
  const rain = Math.max(0, Math.min(1, Number(weather?.rain) || 0));
  const snow = Math.max(0, Math.min(1, Number(weather?.snow) || 0));
  const intensity = Math.max(rain, snow);
  if (intensity < 0.035) {
    if (canvas) canvas.style.display = 'none';
    return;
  }
  if (!init()) return;
  canvas.style.display = 'block';
  resize();
  buildParticles(quality);
  gl.useProgram(program);
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  gl.enableVertexAttribArray(0);
  gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 0, 0);
  gl.uniform1f(gl.getUniformLocation(program,'t'), Math.max(0, Number(weather?.seconds) || 0));
  gl.uniform1f(gl.getUniformLocation(program,'wind'), Math.max(-5, Math.min(5, Number(weather?.windX) || 0)));
  gl.uniform1f(gl.getUniformLocation(program,'intensity'), intensity);
  gl.uniform1f(gl.getUniformLocation(program,'particleSize'), snow > rain ? 7.0 : 4.0);
  gl.uniform1f(gl.getUniformLocation(program,'snow'), snow > rain ? 1.0 : 0.0);
  gl.uniform1f(gl.getUniformLocation(program,'aspect'), canvas.width / Math.max(1,canvas.height));
  gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
  gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT);
  gl.drawArrays(gl.POINTS, 0, count);
}
function dispose() {
  if (gl) {
    if (buffer) gl.deleteBuffer(buffer);
    if (program) gl.deleteProgram(program);
  }
  if (canvas) canvas.remove();
  gl=null; canvas=null; program=null; buffer=null; count=0; blocked=false;
}
root.OpsisWeatherSpatial = { setMode, update, dispose, supported: () => !blocked, mode:() => mode };
})(window);
