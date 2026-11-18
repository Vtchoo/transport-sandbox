import './style.css';
import * as THREE from 'three';
import { TerrainWorld } from './terrain/world';
import { DEFAULT_SETTINGS, GENERATOR_VERSION } from './terrain/generator';
import type { TerrainSettings } from './terrain/generator';
import { CameraRig } from './camera';
import type { CameraMode } from './camera';
import { TrackEditor } from './track/editor';
import { TrainEditor } from './train/editor';
import { TrainWindows } from './train/windows';
import { TrainViewports } from './train/viewports';
import { WindowManager } from './ui/windows';
import './ui/windows.css';
import { icon } from './ui/icons';
import { WarningMessages } from './ui/warnings';
import type { PhysicsMode } from './train/physics';
import { MenuScene } from './menu-scene';

const $ = <T extends HTMLElement>(selector: string) => document.querySelector<T>(selector)!;
const canvas = $<HTMLCanvasElement>('#world');
const menu = $('#menu'), game = $('#game'), loading = $('#loading');
const start = $<HTMLButtonElement>('#start');
const seedInput = $<HTMLInputElement>('#seed');
const mountainInput = $<HTMLInputElement>('#mountains');
const scaleInput = $<HTMLSelectElement>('#scale');
const url = new URL(location.href);
let saved: Partial<TerrainSettings> & { physics?: PhysicsMode } = {};
try { saved = JSON.parse(localStorage.getItem('rail-world-v1') || '{}'); } catch { /* Storage is optional. */ }
seedInput.value = url.searchParams.get('seed') ?? saved.seed ?? DEFAULT_SETTINGS.seed;
const relief = Number(url.searchParams.get('relief') ?? saved.mountainHeight ?? DEFAULT_SETTINGS.mountainHeight);
mountainInput.value = String(Number.isFinite(relief) ? Math.max(400, Math.min(3000, relief)) : DEFAULT_SETTINGS.mountainHeight);
const scale = Number(url.searchParams.get('scale') ?? saved.horizontalScale ?? 1);
scaleInput.value = [0.5, 1, 2].includes(scale) ? String(scale) : '1';
const physicsSelect = $<HTMLSelectElement>('#physics-mode');
physicsSelect.value = (url.searchParams.get('physics') ?? saved.physics) === 'realistic' ? 'realistic' : 'ho';
if (['seed', 'relief', 'scale', 'physics'].some(key => url.searchParams.has(key))) $<HTMLInputElement>('input[name="setup"][value="advanced"]').checked = true;
function setupUI() {
  const advanced = $<HTMLInputElement>('input[name="setup"][value="advanced"]').checked;
  const fields = $<HTMLFieldSetElement>('#advanced-setup'); fields.hidden = !advanced; fields.disabled = !advanced;
}
document.querySelectorAll('input[name="setup"]').forEach(radio => radio.addEventListener('change', setupUI)); setupUI();
const updateRelief = () => { $('#mountain-value').textContent = `${Number(mountainInput.value).toLocaleString()} m`; };
mountainInput.addEventListener('input', updateRelief);
updateRelief();

let renderer: THREE.WebGLRenderer;
try {
  renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
} catch {
  start.disabled = true;
  $<HTMLButtonElement>('#new-game-open').disabled = true;
  const message = document.createElement('p'); message.setAttribute('role', 'alert');
  message.textContent = 'WebGL is unavailable. Enable hardware acceleration to load the world.'; menu.append(message);
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
let menuScene: MenuScene | undefined = new MenuScene();
let world: TerrainWorld | undefined;
let trackEditor: TrackEditor | undefined;
let trainEditor: TrainEditor | undefined;
let trainWindows: TrainWindows | undefined;
let skipCaptureClick = false;
const trainViewports = new TrainViewports(renderer, scene);
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
const newGameDialog = $<HTMLDialogElement>('#new-game');
let settingsReturnFocus: HTMLElement | null = null;
const cameraSelect = $<HTMLSelectElement>('#camera-mode');
const contoursCheckbox = $<HTMLInputElement>('#terrain-contours');
const contourIntervalSelect = $<HTMLSelectElement>('#contour-interval');
try {
  const view = JSON.parse(localStorage.getItem('rail-view-v1') || '{}');
  contoursCheckbox.checked = view.contours === true;
  if ([5, 10, 20, 50, 100].includes(view.contourInterval)) contourIntervalSelect.value = String(view.contourInterval);
} catch { /* Storage is optional. */ }
function applyViewOptions() {
  const interval = Number(contourIntervalSelect.value);
  world?.setContours(contoursCheckbox.checked, interval);
  $('#contour-legend').textContent = `Bold contours every ${interval * 5} m, measured from sea level. Trees are hidden while contours are shown.`;
  try { localStorage.setItem('rail-view-v1', JSON.stringify({ contours: contoursCheckbox.checked, contourInterval: interval })); } catch { /* Storage is optional. */ }
}
contoursCheckbox.addEventListener('change', applyViewOptions);
contourIntervalSelect.addEventListener('change', applyViewOptions);
applyViewOptions();
const ground = (x: number, z: number) => world ? Math.max(world.settings.seaLevel, world.groundHeight(x, z)) : 0;
let hudTime = 0, frames = 0, frameTime = 0, fps = 0;

function endDrag() {
  const pointer = dragPointer;
  dragging = null;
  dragPointer = null;
  if (pointer !== null && canvas.hasPointerCapture(pointer)) canvas.releasePointerCapture(pointer);
}
function clearInput() { keys.clear(); endDrag(); trackEditor?.endDrag(); }
function locked() { return document.pointerLockElement === canvas; }
function syncWindowTools(active?: string) {
  clearInput();
  if (locked()) document.exitPointerLock();
  const trackActive = active === 'track';
  if (trackEditor?.enabled !== trackActive) trackEditor?.setEnabled(trackActive, windows.isVisible('track'));
  trainWindows?.activate(active);
}
const windows = new WindowManager($('#window-taskbar'), syncWindowTools);
const warnings = new WarningMessages($('#game-warning'));
windows.register({ id: 'view-options', element: $('#view-options'), titlebar: $('#view-options .settings-heading'), title: 'View options', closeButton: $('#view-options-close'), scrollContent: true, bounds: { x: innerWidth - 340, y: 140, width: 300, height: 260 }, minWidth: 270, minHeight: 200, onClose: clearInput });
$('#view-options-open').addEventListener('click', () => {
  if (windows.isVisible('view-options')) windows.close('view-options');
  else windows.open('view-options');
});
windows.register({ id: 'new-game', element: newGameDialog, titlebar: $('#new-game .settings-heading'), title: 'New game', closeButton: $('#new-game-close'), modal: true, scrollContent: true, bounds: { x: (innerWidth - 460) / 2, y: 100, width: 460, height: 610 }, minWidth: 300, minHeight: 330, onClose: () => { newGameDialog.close(); $('#new-game-open').focus(); } });
$('#new-game-open').addEventListener('click', () => { newGameDialog.showModal(); windows.open('new-game'); $<HTMLInputElement>('input[name="setup"]:checked').focus(); });
newGameDialog.addEventListener('cancel', event => { event.preventDefault(); windows.close('new-game'); });
$('#place-train span').replaceChildren(icon('car'));
$('#show-trains').prepend(icon('train'));
windows.register({ id: 'track', element: $('#track-panel'), titlebar: $('#track-panel .track-panel-heading'), title: 'Build track', closeButton: $('#track-done'), scrollContent: true, bounds: { x: 24, y: 85, width: 330, height: 460 }, minWidth: 280, minHeight: 240, onClose: () => trackEditor?.setEnabled(false) });
windows.register({ id: 'settings', element: settingsDialog, titlebar: $('#settings .settings-heading'), title: 'Settings', closeButton: $('#settings-close'), modal: true, scrollContent: true, bounds: { x: (innerWidth - 440) / 2, y: 120, width: 440, height: 360 }, minWidth: 300, minHeight: 240, onClose: () => { settingsDialog.close(); clearInput(); settingsReturnFocus?.focus(); } });
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
  trackEditor?.suspend();
  trainEditor?.suspend();
  if (locked()) document.exitPointerLock();
  $('#settings-root').hidden = false;
  $('#gameplay-settings').hidden = true;
  settingsDialog.showModal();
  windows.open('settings');
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
  windows.close('settings');
}
$('#settings-close').addEventListener('click', closeSettings);
settingsDialog.addEventListener('cancel', event => { event.preventDefault(); closeSettings(); });
cameraUI();
function menuView() {
  rig.stopFollowing();
  warnings.hide();
  windows.closeAll();
  trainWindows?.dispose(); trainWindows = undefined;
  trainViewports.clear();
  playing = false; ready = false;
  menuScene ??= new MenuScene();
  debugOverlay.hidden = true;
  clearInput();
  if (locked()) document.exitPointerLock();
  trackEditor?.dispose(); trackEditor = undefined;
  trainEditor?.dispose(); trainEditor = undefined;
  if (world) { scene.remove(world.group); world.dispose(); world = undefined; }
  menu.hidden = false; game.hidden = true; loading.hidden = true; crosshair.hidden = true;
  document.body.classList.remove('playing');
  start.disabled = false;
  $('#new-game-open').focus();
}
start.addEventListener('click', () => {
  if (playing) return;
  const advanced = $<HTMLInputElement>('input[name="setup"][value="advanced"]').checked;
  const physics: PhysicsMode = advanced && physicsSelect.value === 'realistic' ? 'realistic' : 'ho';
  const settings: TerrainSettings = advanced ? {
    seed: seedInput.value.trim() || DEFAULT_SETTINGS.seed,
    mountainHeight: Number(mountainInput.value), horizontalScale: Number(scaleInput.value), seaLevel: 0,
  } : { ...DEFAULT_SETTINGS };
  seedInput.value = settings.seed;
  try { localStorage.setItem('rail-world-v1', JSON.stringify({ ...settings, physics })); } catch { /* Private browsing is supported. */ }
  const shareUrl = new URL(location.href);
  shareUrl.searchParams.set('seed', settings.seed);
  shareUrl.searchParams.set('relief', String(settings.mountainHeight));
  shareUrl.searchParams.set('scale', String(settings.horizontalScale));
  shareUrl.searchParams.set('physics', physics);
  history.replaceState(null, '', shareUrl);
  start.disabled = true;
  windows.close('new-game');
  menu.hidden = true; game.hidden = false; loading.hidden = false;
  document.body.classList.add('playing');
  playing = true; ready = false;
  menuScene?.dispose(); menuScene = undefined;
  world = new TerrainWorld(settings);
  applyViewOptions();
  scene.add(world.group);
  trackEditor = new TrackEditor(scene, camera, canvas, world, windows);
  trainEditor = new TrainEditor(scene, camera, canvas, world, trackEditor.network);
  trainEditor.physicsMode = physics;
  trainEditor.onWarning = text => warnings.show(text);
  trainWindows = new TrainWindows(trainEditor, windows, {
    get current() { return rig.following; },
    toggle(id) {
      if (rig.following === id) { rig.stopFollowing(); return; }
      const focus = trainEditor?.trainFocus(id, true); if (!focus) return;
      clearInput(); if (locked()) document.exitPointerLock(); rig.followTrain(id, focus.center, ground);
    },
  });
  trackEditor.onClose = () => windows.close('track');
  $<HTMLButtonElement>('#build-track').disabled = true;
  $<HTMLButtonElement>('#place-train').disabled = true;
  $<HTMLButtonElement>('#show-trains').disabled = true;
  rig.reset(600, 900, ground);
  world.update(rig.position.x, rig.position.z, rig.position.y);
  $('#world-name').textContent = settings.seed;
  $('#loading-message').textContent = 'Generating the surrounding terrain…';
  $<HTMLProgressElement>('#load-progress').value = 0;
  cameraUI();
});
$('#back').addEventListener('click', menuView);
$('#build-track').addEventListener('click', () => {
  if (!playing || !ready || !trackEditor) return;
  clearInput();
  if (locked()) document.exitPointerLock();
  if (trackEditor.enabled) windows.close('track'); else windows.open('track');
});
$('#place-train').addEventListener('click', () => {
  if (!playing || !ready || !trainEditor) return;
  clearInput(); if (locked()) document.exitPointerLock();
  trainWindows?.toggleCatalog();
});
$('#show-trains').addEventListener('click', () => { if (playing && ready) trainWindows?.openFleet(); });
$('#random-seed').addEventListener('click', () => {
  const values = crypto.getRandomValues(new Uint32Array(2));
  seedInput.value = `${values[0].toString(36)}-${values[1].toString(36)}`;
});
$('#share').addEventListener('click', async () => {
  try { await navigator.clipboard.writeText(location.href); $('#share').textContent = 'Link copied'; }
  catch { $('#share').textContent = 'Copy the URL to share'; }
});
async function captureMouse() {
  if (!playing || !ready || settingsDialog.open || rig.mode !== 'free' || trackEditor?.enabled || trainEditor?.enabled) return;
  try { await canvas.requestPointerLock(); }
  catch { $('#capture').textContent = 'Drag the landscape to look around'; }
}
$('#capture').addEventListener('click', captureMouse);
canvas.addEventListener('click', () => { if (!skipCaptureClick && !dragging) void captureMouse(); skipCaptureClick = false; });
canvas.addEventListener('pointerdown', (event) => {
  if (!playing || !ready || settingsDialog.open || locked()) return;
  canvas.focus({ preventScroll: true });
  skipCaptureClick = false;
  if (trainEditor?.pointerDown(event)) { skipCaptureClick = true; event.preventDefault(); keys.clear(); return; }
  if (trackEditor?.openStationAt(event)) { skipCaptureClick = true; event.preventDefault(); clearInput(); return; }
  if (event.button === 0) {
    const id = trainEditor?.pickTrain(event);
    if (id !== undefined) { skipCaptureClick = true; trainWindows?.openTrain(id, 'viewport'); event.preventDefault(); clearInput(); return; }
  }
  if (trackEditor?.pointerDown(event)) { event.preventDefault(); keys.clear(); return; }
  if (dragPointer !== null || ![0, 1, 2].includes(event.button)) return;
  if (rig.mode === 'free' && event.button !== (trackEditor?.enabled || trainEditor?.enabled ? 2 : 0)) return;
  event.preventDefault();
  dragging = rig.mode === 'free' || event.button === 1 ? 'orbit' : 'pan';
  dragPointer = event.pointerId;
  dragButton = event.button === 1 ? 4 : event.button === 2 ? 2 : 1;
  dragX = event.clientX;
  dragY = event.clientY;
  canvas.setPointerCapture(event.pointerId);
});
canvas.addEventListener('pointermove', event => {
  if (playing && ready && !settingsDialog.open && !dragging) trainEditor?.pointerMove(event);
  if (playing && ready && !settingsDialog.open && !dragging && trackEditor?.pointerMove(event)) { event.preventDefault(); return; }
  if (!dragging || event.pointerId !== dragPointer || locked()) return;
  if (!(event.buttons & dragButton) || !playing || !ready || settingsDialog.open) { endDrag(); return; }
  event.preventDefault();
  // Use client coordinates while dragging: movementX/Y are only needed for pointer lock.
  const dx = event.clientX - dragX, dy = event.clientY - dragY;
  dragX = event.clientX; dragY = event.clientY;
  if (dragging === 'pan' && rig.mode === 'overlook') rig.pan(dx, dy, ground);
  else rig.look(dx, dy, ground);
});
canvas.addEventListener('pointerup', event => { trackEditor?.pointerUp(event); if (event.pointerId === dragPointer && !(event.buttons & dragButton)) endDrag(); });
canvas.addEventListener('pointercancel', event => { trackEditor?.pointerUp(event); if (event.pointerId === dragPointer) endDrag(); });
canvas.addEventListener('lostpointercapture', event => { trackEditor?.pointerUp(event); if (event.pointerId === dragPointer) endDrag(); });
canvas.addEventListener('pointerleave', event => { trackEditor?.leave(event.relatedTarget); trainEditor?.leave(); });
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
  if (!playing || settingsDialog.open || (event.target instanceof HTMLElement && (/INPUT|TEXTAREA/.test(event.target.tagName) || event.target.isContentEditable))) return;
  if (event.code === 'KeyZ' && !event.shiftKey && (event.ctrlKey || event.metaKey) && trainWindows?.hasActiveWindow) { event.preventDefault(); if (!event.repeat) trainEditor?.undo(); return; }
  if (event.target instanceof HTMLSelectElement) return;
  if (event.code === 'Escape') { if (!trainWindows?.escape()) trackEditor?.escape(); clearInput(); return; }
  if (event.code === 'F3') {
    event.preventDefault();
    if (!event.repeat) debugOverlay.hidden = !debugOverlay.hidden;
    return;
  }
  if (event.code === 'KeyC') { event.preventDefault(); if (!event.repeat) toggleCamera(); return; }
  if (event.code === 'KeyG' && trackEditor?.enabled) { event.preventDefault(); if (!event.repeat) trackEditor.toggleGrid(); return; }
  if (event.code === 'KeyR' && trainEditor?.enabled) { event.preventDefault(); if (!event.repeat) trainEditor.reversePreview(); return; }
  if ((event.code === 'KeyZ' || event.code === 'KeyX') && !event.ctrlKey && !event.metaKey && !event.altKey && trackEditor?.enabled) {
    event.preventDefault(); trackEditor.rotateEnd(event.code === 'KeyZ' ? -1 : 1); return;
  }
  if (event.key === 'Shift') trackEditor?.modifiersChanged(event.shiftKey);
  if (['KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE', 'ShiftLeft', 'ShiftRight', 'Space', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.code)) {
    event.preventDefault(); keys.add(event.code);
  }
});
window.addEventListener('keyup', event => {
  keys.delete(event.code);
  if (!settingsDialog.open && event.key === 'Shift') trackEditor?.modifiersChanged(event.shiftKey);
});
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
  if (document.hidden) return;
  if (!playing) {
    if (menuScene) { menuScene.update(time, innerWidth / innerHeight); renderer.render(menuScene.scene, menuScene.camera); }
    return;
  }
  if (!world) return;
  frames++; frameTime += delta;
  if (frameTime >= 0.5) { fps = Math.round(frames / frameTime); frames = 0; frameTime = 0; }
  const stats = world.stats;
  if (world.error) {
    loading.hidden = false; ready = false;
    $('#loading-message').textContent = world.error;
  } else if (!ready) {
    $<HTMLProgressElement>('#load-progress').value = stats.nearby / 9;
    if (stats.nearby >= 9) { ready = true; loading.hidden = true; $<HTMLButtonElement>('#build-track').disabled = false; $<HTMLButtonElement>('#place-train').disabled = false; $<HTMLButtonElement>('#show-trains').disabled = false; }
  }
  if (ready && !settingsDialog.open && !trackEditor?.isDragging) rig.update(keys, delta, ground, (x, z) => world!.hasTerrainAt(x, z));
  if (rig.following !== undefined) {
    const focus = trainEditor?.trainFocus(rig.following, true);
    if (focus) rig.updateFollow(focus.center, ground); else rig.stopFollowing();
  }
  world.update(rig.position.x, rig.position.z, rig.position.y);
  camera.position.set(rig.position.x - world.origin.x, rig.position.y, rig.position.z - world.origin.y);
  camera.rotation.set(rig.pitch, rig.yaw, 0);
  trackEditor?.update(time);
  trainEditor?.update(time);
  if (trainWindows) trainViewports.update(time, trainWindows);
  sky.position.copy(camera.position);
  trackEditor?.stations.updateViewports(time, renderer, scene);
  renderer.render(scene, camera);
  hudTime += delta;
  if (hudTime > 0.2) {
    hudTime = 0;
    $('#coordinates').textContent = `X ${Math.round(rig.position.x).toLocaleString()} m   Z ${Math.round(rig.position.z).toLocaleString()} m`;
    $('#altitude').textContent = `${Math.round(camera.position.y).toLocaleString()} m ASL`;
    $('#speed').textContent = rig.mode === 'overlook' ? `${Math.round(rig.distance).toLocaleString()} m zoom distance`
      : `${Math.round(rig.speed)} m/s${keys.has('ShiftLeft') || keys.has('ShiftRight') ? ' × 5' : ''}`;
    $('#streaming').textContent = `${stats.chunks} / ${stats.total} chunks · ${(stats.triangles / 1000000).toFixed(2)}M terrain triangles · ${stats.trees.toLocaleString()} trees · ${fps} fps`;
    $('#range').textContent = `${(stats.radius / 1000).toFixed(1)} km radius · generator v${GENERATOR_VERSION}`;
    $('#track-earthworks').hidden = stats.earthworksPending === 0;
  }
});
window.addEventListener('pagehide', () => { menuScene?.dispose(); warnings.dispose(); trainWindows?.dispose(); trainViewports.dispose(); windows.dispose(); trainEditor?.dispose(); trackEditor?.dispose(); world?.dispose(); renderer.dispose(); });
