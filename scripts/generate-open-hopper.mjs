/** Native Godot starter hopper: original metre-scale geometry, not a specific prototype. */
import { createRequire } from 'node:module';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
// Three.js is used only as an offline geometry authoring library, never by the game.
let THREE;
try { THREE = createRequire(import.meta.url)('three'); }
catch (error) { if (error.code !== 'MODULE_NOT_FOUND') throw error; THREE = createRequire(new URL('../web/package.json', import.meta.url))('three'); }

const root = new URL('../godot/assets/vehicles/', import.meta.url);
const gltf = { asset: { version: '2.0', generator: 'Transport Sandbox open hopper' }, scene: 0,
  scenes: [{ nodes: [0] }], nodes: [{ name: 'open_hopper', children: [1, 2, 3] },
    { name: 'body', children: [] }, { name: 'bogie_0', translation: [0, 0, 4.8], children: [] },
    { name: 'bogie_1', translation: [0, 0, -4.8], children: [] }],
  meshes: [], accessors: [], bufferViews: [], materials: [] };
const materials = [
  ['Oxide red steel', '#81463b', .3, .78], ['Inside steel', '#503b34', .3, .88],
  ['Underframe', '#292d2c', .5, .7], ['Wheel steel', '#747a7b', .7, .4],
  ['Weathered rims', '#a46c53', .35, .75], ['Brake wheel', '#b58245', .4, .6],
];
for (const [name, color, metallicFactor, roughnessFactor] of materials) gltf.materials.push({ name,
  pbrMetallicRoughness: { baseColorFactor: [...new THREE.Color(color).toArray(), 1], metallicFactor, roughnessFactor }, doubleSided: true });
const blocks = []; let length = 0;
function accessor(values, size, index = false) {
  const data = index ? new Uint32Array(values) : new Float32Array(values);
  const bytes = Buffer.from(data.buffer);
  const view = gltf.bufferViews.length;
  gltf.bufferViews.push({ buffer: 0, byteOffset: length, byteLength: bytes.length, target: index ? 34963 : 34962 });
  blocks.push(bytes); length += bytes.length;
  const a = { bufferView: view, componentType: index ? 5125 : 5126, count: data.length / size, type: size === 3 ? 'VEC3' : size === 2 ? 'VEC2' : 'SCALAR' };
  if (size === 3) {
    a.min = [Infinity, Infinity, Infinity]; a.max = [-Infinity, -Infinity, -Infinity];
    for (let i = 0; i < data.length; i++) { const c = i % 3; a.min[c] = Math.min(a.min[c], data[i]); a.max[c] = Math.max(a.max[c], data[i]); }
  }
  gltf.accessors.push(a); return gltf.accessors.length - 1;
}
function mesh(parent, name, geometry, material, position = [0, 0, 0], rotation = [0, 0, 0]) {
  geometry.applyMatrix4(new THREE.Matrix4().makeRotationFromEuler(new THREE.Euler(...rotation)));
  geometry.translate(...position);
  const attributes = { POSITION: accessor(geometry.attributes.position.array, 3), NORMAL: accessor(geometry.attributes.normal.array, 3) };
  if (geometry.attributes.uv) attributes.TEXCOORD_0 = accessor(geometry.attributes.uv.array, 2);
  const primitive = { attributes, material, mode: 4 };
  if (geometry.index) primitive.indices = accessor(geometry.index.array, 1, true);
  gltf.nodes[parent].children.push(gltf.nodes.length);
  gltf.nodes.push({ name, mesh: gltf.meshes.length }); gltf.meshes.push({ name, primitives: [primitive] });
  geometry.dispose();
}
function box(parent, name, size, position, material = 0) { mesh(parent, name, new THREE.BoxGeometry(...size), material, position); }
function panel(name, points, material) {
  const vertices = points.flatMap(p => p);
  const g = new THREE.BufferGeometry().setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3));
  g.setIndex([0, 1, 2, 0, 2, 3]); g.computeVertexNormals(); mesh(1, name, g, material);
}
function beam(parent, name, a, b, width, depth, material) {
  const start = new THREE.Vector3(...a), end = new THREE.Vector3(...b), delta = end.clone().sub(start);
  const g = new THREE.BoxGeometry(width, delta.length(), depth);
  g.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), delta.normalize()));
  mesh(parent, name, g, material, start.add(end).multiplyScalar(.5).toArray());
}
// Empty, open-topped steel shell. The inside floor and inner walls are separately visible.
for (const side of [-1, 1]) {
  panel(`side_${side}`, [[side * .66, 1.83, -5.9], [side * 1.52, 3.78, -5.9], [side * 1.52, 3.78, 5.9], [side * .66, 1.83, 5.9]], 0);
  panel(`inside_${side}`, [[side * .64, 1.88, 5.84], [side * 1.45, 3.74, 5.84], [side * 1.45, 3.74, -5.84], [side * .64, 1.88, -5.84]], 1);
  box(1, `rim_${side}`, [.13, .13, 12], [side * 1.51, 3.82, 0], 4);
  box(1, `sill_${side}`, [.18, .25, 12.9], [side * 1.25, 1.22, 0], 2);
  for (let i = 0; i <= 10; i++) {
    const z = -5.8 + i * 1.16;
    beam(1, `rib_${side}_${i}`, [side * .7, 1.82, z], [side * 1.57, 3.76, z], .11, .12, 4);
  }
}
for (const end of [-1, 1]) {
  panel(`end_${end}`, [[-.66, 1.83, end * 5.9], [.66, 1.83, end * 5.9], [1.52, 3.78, end * 5.9], [-1.52, 3.78, end * 5.9]], 0);
  panel(`inside_end_${end}`, [[-1.45, 3.74, end * 5.84], [1.45, 3.74, end * 5.84], [.64, 1.88, end * 5.84], [-.64, 1.88, end * 5.84]], 1);
  box(1, `end_rim_${end}`, [3.12, .13, .13], [0, 3.82, end * 5.9], 4);
  box(1, `end_deck_${end}`, [2.8, .12, .85], [0, 1.32, end * 6.38], 2);
  box(1, `coupler_shank_${end}`, [.22, .24, .65], [0, 1.03, end * 6.85], 2);
  box(1, `coupler_${end}`, [.36, .3, .32], [0, 1.03, end * 7.09], 2);
  for (const x of [.92, 1.31]) beam(1, `ladder_rail_${end}_${x}`, [x, 1.32, end * 6.05], [x, 3.83, end * 6.05], .045, .045, 2);
  for (let i = 0; i < 9; i++) box(1, `rung_${end}_${i}`, [.43, .04, .045], [1.115, 1.45 + i * .28, end * 6.05], 2);
}
box(1, 'inside_floor', [1.3, .08, 11.8], [0, 1.85, 0], 1);
box(1, 'center_sill', [.42, .3, 13.4], [0, 1.08, 0], 2);
// Two discharge funnels, gate doors and actuating cross-shafts beneath the body.
for (const z of [-2.85, 2.85]) {
  for (const side of [-1, 1]) panel(`discharge_side_${z}_${side}`, [[side * .65, 1.83, z - 2.75], [side * .65, 1.83, z + 2.75], [side * .4, 1.08, z + .7], [side * .4, 1.08, z - .7]], 0);
  for (const end of [-1, 1]) panel(`discharge_end_${z}_${end}`, [[-.65, 1.83, z + end * 2.75], [.65, 1.83, z + end * 2.75], [.4, 1.08, z + end * .7], [-.4, 1.08, z + end * .7]], 0);
  box(1, `gate_${z}`, [.95, .1, 1.55], [0, 1.03, z], 2);
  box(1, `gate_shaft_${z}`, [2.25, .07, .07], [0, 1.1, z], 3);
}
mesh(1, 'brake_handwheel', new THREE.TorusGeometry(.23, .025, 6, 16), 5, [-.85, 2.15, 6.25]);
for (let i = 0; i < 4; i++) { const a = i * Math.PI / 2; beam(1, `brake_spoke_${i}`, [-.85, 2.15, 6.25], [-.85 + Math.cos(a) * .23, 2.15 + Math.sin(a) * .23, 6.25], .02, .025, 5); }
for (const bogie of [2, 3]) {
  box(bogie, 'bolster', [2.15, .22, .45], [0, .9, 0], 2);
  for (const side of [-1, 1]) {
    box(bogie, `sideframe_${side}`, [.18, .35, 2.45], [side * 1.02, .65, 0], 2);
    box(bogie, `spring_block_${side}`, [.2, .27, .5], [side * 1.05, .5, 0], 3);
  }
  for (const z of [-.9, .9]) {
    mesh(bogie, `axle_${z}`, new THREE.CylinderGeometry(.085, .085, 2.1, 8), 3, [0, .46, z], [0, 0, Math.PI / 2]);
    for (const side of [-1, 1]) {
      mesh(bogie, `wheel_${z}_${side}`, new THREE.CylinderGeometry(.46, .46, .15, 20), 3, [side * .7925, .46, z], [0, 0, Math.PI / 2]);
      mesh(bogie, `flange_${z}_${side}`, new THREE.CylinderGeometry(.49, .49, .025, 20), 2, [side * .73, .46, z], [0, 0, Math.PI / 2]);
      box(bogie, `bearing_${z}_${side}`, [.22, .28, .32], [side * 1.04, .46, z], 2);
    }
  }
}
gltf.buffers = [{ byteLength: length }];
const bin = Buffer.concat(blocks), json = Buffer.from(JSON.stringify(gltf));
const padded = Buffer.concat([json, Buffer.alloc((4 - json.length % 4) % 4, 32)]);
const header = Buffer.alloc(20), chunk = Buffer.alloc(8);
header.writeUInt32LE(0x46546c67); header.writeUInt32LE(2, 4); header.writeUInt32LE(28 + padded.length + bin.length, 8);
header.writeUInt32LE(padded.length, 12); header.writeUInt32LE(0x4e4f534a, 16);
chunk.writeUInt32LE(bin.length); chunk.writeUInt32LE(0x004e4942, 4);
await writeFile(new URL('open-hopper-1435.glb', root), Buffer.concat([header, padded, chunk, bin]));
const catalogPath = new URL('catalog.json', root), catalog = JSON.parse(await readFile(catalogPath, 'utf8'));
const entry = { id: 'open-hopper', name: 'Open hopper', kind: 'freight', length: 14.5, width: 3.25, height: 3.9, bogieCenters: 9.6,
  physics: { massKg: 23000, powerW: 0, tractiveEffortN: 0, maxSpeedMps: 27.777777778 },
  gauges: [1435], models: { '1435': 'res://assets/vehicles/open-hopper-1435.glb' }, exhausts: [],
  cargo: { capacities: { coal: 70, 'iron-ore': 70 }, visual: { bottomY: 1.92, topY: 3.66, bottomWidth: 1.24, topWidth: 2.78, length: 11.6 } }, thumbnail: 'res://assets/vehicles/thumbnails/open-hopper.svg' };
const at = catalog.findIndex(v => v.id === entry.id); if (at < 0) catalog.push(entry); else catalog[at] = entry;
await writeFile(catalogPath, JSON.stringify(catalog, null, 2) + '\n');
await mkdir(new URL('thumbnails/', root), { recursive: true });
await writeFile(new URL('thumbnails/open-hopper.svg', root), `<svg xmlns="http://www.w3.org/2000/svg" width="180" height="64" viewBox="0 0 180 64"><g stroke="#292d2c" stroke-width="2"><path d="M14 10h152v29H14z" fill="#81463b"/><path d="M30 39l14 11h28l10-11m17 0 10 11h28l14-11" fill="#633c32"/><path d="M12 10h156M12 40h156" stroke="#b28568" stroke-width="4"/><path d="M28 12v26m20-26v26m20-26v26m20-26v26m20-26v26m20-26v26m20-26v26" stroke="#a46c53"/><path d="M8 46h164" stroke-width="5"/><path d="M27 48h33m60 0h33" stroke-width="8"/><g fill="#555e5c"><circle cx="30" cy="53" r="7"/><circle cx="56" cy="53" r="7"/><circle cx="124" cy="53" r="7"/><circle cx="150" cy="53" r="7"/></g></g></svg>\n`);
console.log(`Generated open hopper: ${gltf.meshes.length} mesh parts, 14.5 m, 1435 mm, 70 t payload.`);
