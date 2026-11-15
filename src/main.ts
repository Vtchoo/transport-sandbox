import './style.css';
import * as THREE from 'three';
import { TerrainWorld } from './terrain/world';
import { DEFAULT_SETTINGS, GENERATOR_VERSION } from './terrain/generator';
import type { TerrainSettings } from './terrain/generator';
import { CameraRig } from './camera';
import type { CameraMode } from './camera';

const $ = <T extends HTMLElement>(selector: string) => document.querySelector<T>(selector)!;
const canvas = $<HTMLCanvasElement>('#world');
const menu = $('#menu'), game = $('#game'), loading = $('#loading');
const start = $<HTMLButtonElement>('#start');
const seedInput = $<HTMLInputElement>('#seed');
const mountainInput = $<HTMLInputElement>('#mountains');
const scaleInput = $<HTMLSelectElement>('#scale');
const url = new URL(location.href);
let saved: Partial<TerrainSettings> = {};
try { saved = JSON.parse(localStorage.getItem('rail-world-v1') || '{}'); } catch { /* Storage is optional. */ }
seedInput.value = url.searchParams.get('seed') ?? saved.seed ?? DEFAULT_SETTINGS.seed;
const relief = Number(url.searchParams.get('relief') ?? saved.mountainHeight ?? DEFAULT_SETTINGS.mountainHeight);
mountainInput.value = String(Number.isFinite(relief) ? Math.max(400, Math.min(3000, relief)) : DEFAULT_SETTINGS.mountainHeight);
const scale = Number(url.searchParams.get('scale') ?? saved.horizontalScale ?? 1);
scaleInput.value = [0.5, 1, 2].includes(scale) ? String(scale) : '1';
const updateRelief = () => { $('#mountain-value').textContent = `${Number(mountainInput.value).toLocaleString()} m`; };
mountainInput.addEventListener('input', updateRelief);
updateRelief();

let renderer: THREE.WebGLRenderer;
try {
  renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
} catch {
  start.disabled = true;
  $('.menu-note').textContent = 'WebGL is unavailable. Enable hardware acceleration to load the world.';
  throw new Error('WebGL is unavailable');
}
renderer.setPixelRatio(Math.min(devicePixelRatio, 1.75));
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.05;
const scene = new THREE.Scene();
scene.background = new THREE.Color(0xb5cede);
scene.fog = new THREE.FogExp2(0xb5cede, 0.00034);
const camera = new THREE.PerspectiveCamera(65, 1, 0.5, 14000);
camera.rotation.order = 'YXZ';
scene.add(new THREE.HemisphereLight(0xd6eaff, 0x7b7157, 2.4));
const sun = new THREE.DirectionalLight(0xffefd3, 2.7);
sun.position.set(-0.7, 0.8, -0.4);
scene.add(sun);
const sky = new THREE.Mesh(new THREE.SphereGeometry(12000, 24, 16), new THREE.ShaderMaterial({
  side: THREE.BackSide, depthWrite: false,
  uniforms: { sunDirection: { value: sun.position.clone().normalize() } },
  vertexShader: 'varying vec3 vDirection; void main(){vDirection=position;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0);}',
  fragmentShader: `varying vec3 vDirection; uniform vec3 sunDirection;
    void main(){ vec3 d=normalize(vDirection); float h=pow(max(d.y,0.0),0.45);
      vec3 col=mix(vec3(0.68,0.79,0.86),vec3(0.18,0.39,0.64),h);
      float glow=pow(max(dot(d,sunDirection),0.0),28.0);
      col+=vec3(0.3,0.22,0.1)*glow;
      col+=vec3(3.0,2.5,1.7)*smoothstep(0.99975,0.9999,dot(d,sunDirection));
      gl_FragColor=vec4(col,1.0);
      #include <tonemapping_fragment>
      #include <colorspace_fragment>
    }`,
}));
scene.add(sky);
let world: TerrainWorld | undefined;
let playing = false, ready = false;
let dragging: 'pan' | 'orbit' | null = null;
let dragPointer: number | null = null;
let dragButton = 0;
let dragX = 0, dragY = 0;
const rig = new CameraRig();
try { if (localStorage.getItem('rail-camera-mode') === 'free') rig.mode = 'free'; } catch { /* Storage is optional. */ }
const keys = new Set<string>();
const crosshair = $('#crosshair');
const debugOverlay = $('#debug-overlay');
const settingsDialog = $<HTMLDialogElement>('#settings');
let settingsReturnFocus: HTMLElement | null = null;
const cameraSelect = $<HTMLSelectElement>('#camera-mode');
const ground = (x: number, z: number) => world ? Math.max(world.settings.seaLevel, world.groundHeight(x, z)) : 0;
let hudTime = 0, frames = 0, frameTime = 0, fps = 0;

function endDrag() {
  const pointer = dragPointer;
  dragging = null;
  dragPointer = null;
  if (pointer !== null && canvas.hasPointerCapture(pointer)) canvas.releasePointerCapture(pointer);
}
function clearInput() { keys.clear(); endDrag(); }
function locked() { return document.pointerLockElement === canvas; }
function cameraUI() {
  const overlook = rig.mode === 'overlook';
  $('#camera-label').textContent = overlook ? 'OVERLOOK CAMERA' : 'FREE CAMERA';
  $('#camera-toggle').textContent = `${overlook ? 'Overlook' : 'Free camera'} · C`;
  $('#capture').hidden = overlook;
  $('#capture').textContent = locked() ? 'Mouse captured · Esc to release' : 'Click to look around';
  cameraSelect.value = rig.mode;
  crosshair.hidden = overlook || !locked();
  $('#camera-hint').textContent = overlook
    ? 'WASD / arrows: pan · Drag: pan · Middle drag: orbit / tilt · Q / E: rotate · Scroll: zoom · Shift: faster · C: free camera · F3: debug'
    : 'WASD: move · Mouse / drag: look · E / Q: up / down · Shift: faster · Scroll: speed · C: overlook · Esc: release mouse · F3: debug';
  canvas.setAttribute('aria-label', overlook
    ? 'Terrain overlook. WASD to pan, middle mouse drag to orbit, scroll to zoom.'
    : 'Free camera. WASD to move, mouse to look, E and Q to change altitude.');
}
function setCameraMode(mode: CameraMode) {
  clearInput();
  if (locked()) document.exitPointerLock();
  if (world) rig.setMode(mode, ground); else rig.mode = mode;
  try { localStorage.setItem('rail-camera-mode', mode); } catch { /* Storage is optional. */ }
  cameraUI();
}
function toggleCamera() { setCameraMode(rig.mode === 'overlook' ? 'free' : 'overlook'); }
cameraSelect.addEventListener('change', () => setCameraMode(cameraSelect.value as CameraMode));
$('#camera-toggle').addEventListener('click', toggleCamera);
document.querySelectorAll<HTMLElement>('.settings-open').forEach(button => button.addEventListener('click', () => {
  settingsReturnFocus = button;
  clearInput();
  if (locked()) document.exitPointerLock();
  $('#settings-root').hidden = false;
  $('#gameplay-settings').hidden = true;
  settingsDialog.showModal();
}));
$('#open-gameplay').addEventListener('click', () => {
  $('#settings-root').hidden = true;
  $('#gameplay-settings').hidden = false;
  cameraSelect.focus();
});
$('#settings-back').addEventListener('click', () => {
  $('#settings-root').hidden = false;
  $('#gameplay-settings').hidden = true;
  $('#open-gameplay').focus();
});
function closeSettings() {
  settingsDialog.close();
  clearInput();
  settingsReturnFocus?.focus();
}
$('#settings-close').addEventListener('click', closeSettings);
settingsDialog.addEventListener('cancel', event => { event.preventDefault(); closeSettings(); });
cameraUI();
function menuView() {
  playing = false; ready = false;
  debugOverlay.hidden = true;
  clearInput();
  if (locked()) document.exitPointerLock();
  if (world) { scene.remove(world.group); world.dispose(); world = undefined; }
  menu.hidden = false; game.hidden = true; loading.hidden = true; crosshair.hidden = true;
  document.body.classList.remove('playing');
  start.disabled = false;
  start.focus();
}
start.addEventListener('click', () => {
  if (playing) return;
  const settings: TerrainSettings = {
    seed: seedInput.value.trim() || DEFAULT_SETTINGS.seed,
    mountainHeight: Number(mountainInput.value), horizontalScale: Number(scaleInput.value), seaLevel: 0,
  };
  seedInput.value = settings.seed;
  try { localStorage.setItem('rail-world-v1', JSON.stringify(settings)); } catch { /* Private browsing is supported. */ }
  const shareUrl = new URL(location.href);
  shareUrl.searchParams.set('seed', settings.seed);
  shareUrl.searchParams.set('relief', String(settings.mountainHeight));
  shareUrl.searchParams.set('scale', String(settings.horizontalScale));
  history.replaceState(null, '', shareUrl);
  start.disabled = true;
  menu.hidden = true; game.hidden = false; loading.hidden = false;
  document.body.classList.add('playing');
  playing = true; ready = false;
  world = new TerrainWorld(settings);
  scene.add(world.group);
  rig.reset(600, 900, ground);
  world.update(rig.position.x, rig.position.z, rig.position.y);
  $('#world-name').textContent = settings.seed;
  $('#loading-message').textContent = 'Generating the surrounding terrain…';
  $<HTMLProgressElement>('#load-progress').value = 0;
  cameraUI();
});
$('#back').addEventListener('click', menuView);
$('#random-seed').addEventListener('click', () => {
  const values = crypto.getRandomValues(new Uint32Array(2));
  seedInput.value = `${values[0].toString(36)}-${values[1].toString(36)}`;
});
$('#share').addEventListener('click', async () => {
  try { await navigator.clipboard.writeText(location.href); $('#share').textContent = 'Link copied'; }
  catch { $('#share').textContent = 'Copy the URL to share'; }
});
async function captureMouse() {
  if (!playing || !ready || settingsDialog.open || rig.mode !== 'free') return;
  try { await canvas.requestPointerLock(); }
  catch { $('#capture').textContent = 'Drag the landscape to look around'; }
}
$('#capture').addEventListener('click', captureMouse);
canvas.addEventListener('click', () => { if (!dragging) void captureMouse(); });
canvas.addEventListener('pointerdown', (event) => {
  if (!playing || !ready || settingsDialog.open || locked()) return;
  if (dragPointer !== null || ![0, 1, 2].includes(event.button)) return;
  if (rig.mode === 'free' && event.button !== 0) return;
  event.preventDefault();
  dragging = rig.mode === 'free' || event.button === 1 ? 'orbit' : 'pan';
  dragPointer = event.pointerId;
  dragButton = event.button === 1 ? 4 : event.button === 2 ? 2 : 1;
  dragX = event.clientX;
  dragY = event.clientY;
  canvas.setPointerCapture(event.pointerId);
});
canvas.addEventListener('pointermove', event => {
  if (!dragging || event.pointerId !== dragPointer || locked()) return;
  if (!(event.buttons & dragButton) || !playing || !ready || settingsDialog.open) { endDrag(); return; }
  event.preventDefault();
  // Use client coordinates while dragging: movementX/Y are only needed for pointer lock.
  const dx = event.clientX - dragX, dy = event.clientY - dragY;
  dragX = event.clientX; dragY = event.clientY;
  if (dragging === 'pan' && rig.mode === 'overlook') rig.pan(dx, dy, ground);
  else rig.look(dx, dy, ground);
});
canvas.addEventListener('pointerup', event => { if (event.pointerId === dragPointer && !(event.buttons & dragButton)) endDrag(); });
canvas.addEventListener('pointercancel', event => { if (event.pointerId === dragPointer) endDrag(); });
canvas.addEventListener('lostpointercapture', event => { if (event.pointerId === dragPointer) endDrag(); });
canvas.addEventListener('auxclick', event => { if (event.button === 1) event.preventDefault(); });
canvas.addEventListener('contextmenu', event => event.preventDefault());
window.addEventListener('mousemove', event => {
  if (!playing || !ready || settingsDialog.open || !locked() || rig.mode !== 'free') return;
  rig.look(event.movementX, event.movementY, ground);
});
document.addEventListener('pointerlockchange', () => {
  if (locked() && (rig.mode !== 'free' || settingsDialog.open || !playing)) document.exitPointerLock();
  clearInput(); cameraUI();
});
document.addEventListener('pointerlockerror', () => { $('#capture').textContent = 'Drag the landscape to look around'; });
window.addEventListener('keydown', event => {
  if (!playing || settingsDialog.open || (event.target instanceof HTMLElement && /INPUT|SELECT|TEXTAREA/.test(event.target.tagName))) return;
  if (event.code === 'Escape') { clearInput(); return; }
  if (event.code === 'F3') {
    event.preventDefault();
    if (!event.repeat) debugOverlay.hidden = !debugOverlay.hidden;
    return;
  }
  if (event.code === 'KeyC') { event.preventDefault(); if (!event.repeat) toggleCamera(); return; }
  if (['KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE', 'ShiftLeft', 'ShiftRight', 'Space', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.code)) {
    event.preventDefault(); keys.add(event.code);
  }
});
window.addEventListener('keyup', event => keys.delete(event.code));
window.addEventListener('blur', clearInput);
document.addEventListener('visibilitychange', () => { if (document.hidden) clearInput(); });
canvas.addEventListener('wheel', event => {
  if (!playing || !ready || settingsDialog.open) return;
  event.preventDefault();
  const pixels = event.deltaY * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? innerHeight : 1);
  rig.scroll(pixels, ground);
}, { passive: false });

function resize() {
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(innerWidth, innerHeight);
}
window.addEventListener('resize', resize);
resize();
let previousTime = 0;
renderer.setAnimationLoop(time => {
  const delta = Math.min((time - previousTime) / 1000, 0.05);
  previousTime = time;
  if (document.hidden || !playing || !world) return;
  frames++; frameTime += delta;
  if (frameTime >= 0.5) { fps = Math.round(frames / frameTime); frames = 0; frameTime = 0; }
  const stats = world.stats;
  if (world.error) {
    loading.hidden = false; ready = false;
    $('#loading-message').textContent = world.error;
  } else if (!ready) {
    $<HTMLProgressElement>('#load-progress').value = stats.nearby / 9;
    if (stats.nearby >= 9) { ready = true; loading.hidden = true; }
  }
  if (ready && !settingsDialog.open) rig.update(keys, delta, ground, (x, z) => world!.hasTerrainAt(x, z));
  world.update(rig.position.x, rig.position.z, rig.position.y);
  camera.position.set(rig.position.x - world.origin.x, rig.position.y, rig.position.z - world.origin.y);
  camera.rotation.set(rig.pitch, rig.yaw, 0);
  sky.position.copy(camera.position);
  renderer.render(scene, camera);
  hudTime += delta;
  if (hudTime > 0.2) {
    hudTime = 0;
    $('#coordinates').textContent = `X ${Math.round(rig.position.x).toLocaleString()} m   Z ${Math.round(rig.position.z).toLocaleString()} m`;
    $('#altitude').textContent = `${Math.round(camera.position.y).toLocaleString()} m ASL`;
    $('#speed').textContent = rig.mode === 'overlook' ? `${Math.round(rig.distance).toLocaleString()} m zoom distance`
      : `${Math.round(rig.speed)} m/s${keys.has('ShiftLeft') || keys.has('ShiftRight') ? ' × 5' : ''}`;
    $('#streaming').textContent = `${stats.chunks} / ${stats.total} chunks · ${(stats.triangles / 1000000).toFixed(2)}M triangles · ${fps} fps`;
    $('#range').textContent = `${(stats.radius / 1000).toFixed(1)} km radius · generator v${GENERATOR_VERSION}`;
  }
});
window.addEventListener('pagehide', () => { world?.dispose(); renderer.dispose(); });
