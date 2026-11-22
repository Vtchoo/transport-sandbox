/** Godot-only Dash 9 import with authored bogies and wheelset hierarchy. */
import { readFile, writeFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import { DOMParser } from 'linkedom';
import * as THREE from 'three';
import { ColladaLoader } from 'three/addons/loaders/ColladaLoader.js';

const source = process.argv.find(a => a.startsWith('--source='))?.slice(9) ?? '.files/dash-9/uv-paint-only/v5/dash9-v5-uv.dae';
const texture = process.argv.find(a => a.startsWith('--texture='))?.slice(10) ?? '.files/dash-9/dash9 v5 santa fe texture.png';
const target = 'godot/assets/vehicles/ge-dash9-1435.glb';
const base = await readFile(target);
const jsonLength = base.readUInt32LE(12);
const gltf = JSON.parse(base.subarray(20, 20 + jsonLength));
assert.ok(!gltf.animations?.length && !gltf.skins?.length, 'Static Dash 9 model expected');
const blocks = [base.subarray(28 + jsonLength)];
let length = blocks[0].length;
function view(bytes, target) {
  const padding = (4 - length % 4) % 4;
  if (padding) { blocks.push(Buffer.alloc(padding)); length += padding; }
  const index = gltf.bufferViews.length;
  gltf.bufferViews.push({ buffer: 0, byteOffset: length, byteLength: bytes.length, ...(target ? { target } : {}) });
  blocks.push(bytes); length += bytes.length;
  return index;
}
function attribute(values, size) {
  const data = new Float32Array(values);
  const entry = { bufferView: view(Buffer.from(data.buffer), 34962), componentType: 5126, count: data.length / size, type: size === 3 ? 'VEC3' : 'VEC2' };
  if (size === 3) {
    entry.min = Array.from({ length: 3 }, (_, axis) => Math.min(...values.filter((_, i) => i % 3 === axis)));
    entry.max = Array.from({ length: 3 }, (_, axis) => Math.max(...values.filter((_, i) => i % 3 === axis)));
  }
  gltf.accessors.push(entry); return gltf.accessors.length - 1;
}
const png = await readFile(texture);
assert.equal(png.subarray(1, 4).toString(), 'PNG');
gltf.images = [{ bufferView: view(png), mimeType: 'image/png', name: 'Dash 9 Santa Fe paint' }];
gltf.samplers = [{ magFilter: 9729, minFilter: 9987, wrapS: 33071, wrapT: 33071 }];
gltf.textures = [{ sampler: 0, source: 0 }];
const previous = globalThis.DOMParser;
let scene;
try {
  globalThis.DOMParser = DOMParser;
  scene = new ColladaLoader().parse(await readFile(source, 'utf8'), '').scene;
} finally {
  if (previous === undefined) delete globalThis.DOMParser; else globalThis.DOMParser = previous;
}
scene.updateMatrixWorld(true);
const sourceUnit = Number(new DOMParser().parseFromString(await readFile(source, 'utf8'), 'text/xml').querySelector('asset unit').getAttribute('meter'));
const materialIds = new Map();
let paintedTriangles = 0, solidTriangles = 0;
const semantic = name => (name ?? '').toLowerCase().replaceAll('_', '-');
for (const [part, nativeName] of [['body', 'body'], ['bogie-front', 'bogie_0'], ['bogie-rear', 'bogie_1']]) {
  const matches = [];
  scene.traverse(node => { if (semantic(node.name) === part) matches.push(node); });
  assert.equal(matches.length, 1, `One ${part} component required`);
  const component = matches[0], origin = component.getWorldPosition(new THREE.Vector3());
  assert.ok(Math.abs(origin.x) < .01 && Math.abs(origin.y) < .001, 'Bogie pivot must remain near the rail centerline');
  if (part === 'body') assert.ok(origin.length() < .001);
  else assert.ok(Math.abs(origin.z - (part === 'bogie-front' ? 7.0485 : -7.0485)) < .001);
  const native = gltf.nodes.find(node => node.name === nativeName);
  assert.ok(native, `Missing native ${nativeName}`);
  // All running gear now comes from the authored model, including the wheels.
  native.children = [];
  const datum = new THREE.Vector3(0, 0, origin.z);
  const wheelsets = new Map();
  component.traverse(object => {
    if (!/^wheelset-\d{2}$/.test(semantic(object.name))) return;
    const position = new THREE.Vector3(), rotation = new THREE.Quaternion(), scale = new THREE.Vector3();
    object.matrixWorld.decompose(position, rotation, scale);
    const world = new THREE.Matrix4().compose(position, rotation, scale.divideScalar(sourceUnit));
    const local = new THREE.Matrix4().makeTranslation(-datum.x, -datum.y, -datum.z).multiply(world);
    const node = { name: semantic(object.name), matrix: local.toArray(), children: [] };
    const index = gltf.nodes.length; gltf.nodes.push(node); native.children.push(index);
    wheelsets.set(object, { node, world });
  });
  if (part !== 'body') assert.equal(wheelsets.size, 3, `${part} must contain three authored wheelsets`);
  component.traverse(object => {
    if (!object.isMesh) return;
    const geometry = object.geometry.index ? object.geometry.toNonIndexed() : object.geometry.clone();
    let ancestor = object.parent;
    while (ancestor && !wheelsets.has(ancestor) && ancestor !== component) ancestor = ancestor.parent;
    const wheelset = wheelsets.get(ancestor);
    const transform = wheelset ? wheelset.world.clone().invert().multiply(object.matrixWorld)
      : new THREE.Matrix4().makeTranslation(-datum.x, -datum.y, -datum.z).multiply(object.matrixWorld);
    geometry.applyMatrix4(transform);
    const available = Array.isArray(object.material) ? object.material : [object.material];
    const groups = geometry.groups.length ? geometry.groups : [{ start: 0, count: geometry.attributes.position.count, materialIndex: 0 }];
    for (const group of groups) {
      const material = available[group.materialIndex], painted = /livery/i.test(material.name);
      const positions = [], normals = [], uvs = [];
      for (let start = group.start; start < group.start + group.count; start += 3) {
        const order = transform.determinant() < 0 ? [0, 2, 1] : [0, 1, 2];
        const vertices = order.map(i => new THREE.Vector3().fromBufferAttribute(geometry.attributes.position, start + i));
        const normal = new THREE.Vector3().fromBufferAttribute(geometry.attributes.normal, start + order[0]);
        const area = vertices[1].clone().sub(vertices[0]).cross(vertices[2].clone().sub(vertices[0]));
        const alignment = area.dot(normal);
        if (area.lengthSq() < 1e-20 || Math.abs(alignment) <= area.length() * 1e-8) continue;
        if (alignment < 0) [order[1], order[2]] = [order[2], order[1]];
        for (const offset of order) {
          const i = start + offset;
          positions.push(...new THREE.Vector3().fromBufferAttribute(geometry.attributes.position, i).toArray());
          normals.push(...new THREE.Vector3().fromBufferAttribute(geometry.attributes.normal, i).toArray());
          if (painted) {
            assert.ok(geometry.attributes.uv, 'Painted faces require UVs');
            // COLLADA uses bottom-left UVs; glTF addresses the PNG from its top-left.
            uvs.push(geometry.attributes.uv.getX(i), 1 - geometry.attributes.uv.getY(i));
          }
        }
      }
      if (!positions.length) continue;
      if (!materialIds.has(material)) {
        materialIds.set(material, gltf.materials.length);
        gltf.materials.push({ name: painted ? 'Santa Fe paintable panels' : material.name, pbrMetallicRoughness: {
          baseColorFactor: [...(painted ? [1, 1, 1] : material.color.toArray()), material.opacity], metallicFactor: .1, roughnessFactor: .65,
          ...(painted ? { baseColorTexture: { index: 0 } } : {}),
        }, ...(material.side === THREE.DoubleSide ? { doubleSided: true } : {}) });
      }
      const attributes = { POSITION: attribute(positions, 3), NORMAL: attribute(normals, 3) };
      if (painted) { attributes.TEXCOORD_0 = attribute(uvs, 2); paintedTriangles += positions.length / 9; }
      else solidTriangles += positions.length / 9;
      (wheelset?.node ?? native).children.push(gltf.nodes.length);
      gltf.nodes.push({ name: `${part}-${painted ? 'paint' : 'solid'}-${gltf.meshes.length}`, mesh: gltf.meshes.length });
      gltf.meshes.push({ primitives: [{ attributes, material: materialIds.get(material), mode: 4 }] });
    }
    geometry.dispose();
  });
}
assert.ok(paintedTriangles > 0 && solidTriangles > paintedTriangles);
// Prune replaced meshes and binary data so repeated imports do not grow the model.
function compact(items, used) {
  const ids = [...new Set(used)], map = new Map(ids.map((id, i) => [id, i]));
  return { items: ids.map(id => items[id]), remap: id => { assert.ok(map.has(id)); return map.get(id); } };
}
const reachable = [];
function walk(i) { if (reachable.includes(i)) return; reachable.push(i); for (const child of gltf.nodes[i].children ?? []) walk(child); }
gltf.scenes.forEach(scene => scene.nodes.forEach(walk));
const nodes = compact(gltf.nodes, reachable); gltf.nodes = nodes.items;
gltf.scenes.forEach(scene => { scene.nodes = scene.nodes.map(nodes.remap); });
gltf.nodes.forEach(node => { if (node.children) node.children = node.children.map(nodes.remap); });
const meshes = compact(gltf.meshes, gltf.nodes.filter(n => n.mesh !== undefined).map(n => n.mesh));
gltf.meshes = meshes.items; gltf.nodes.forEach(n => { if (n.mesh !== undefined) n.mesh = meshes.remap(n.mesh); });
const primitives = gltf.meshes.flatMap(m => m.primitives);
const materials = compact(gltf.materials, primitives.map(p => p.material));
gltf.materials = materials.items; primitives.forEach(p => { p.material = materials.remap(p.material); });
const accessors = compact(gltf.accessors, primitives.flatMap(p => [...Object.values(p.attributes), ...(p.indices !== undefined ? [p.indices] : [])]));
gltf.accessors = accessors.items;
primitives.forEach(p => { for (const key in p.attributes) p.attributes[key] = accessors.remap(p.attributes[key]); if (p.indices !== undefined) p.indices = accessors.remap(p.indices); });
const views = compact(gltf.bufferViews, [...gltf.accessors.map(a => a.bufferView), ...gltf.images.map(i => i.bufferView)]);
gltf.accessors.forEach(a => { a.bufferView = views.remap(a.bufferView); });
gltf.images.forEach(i => { i.bufferView = views.remap(i.bufferView); });
const all = Buffer.concat(blocks), chunks = []; let offset = 0;
gltf.bufferViews = views.items.map(v => {
  const bytes = all.subarray(v.byteOffset ?? 0, (v.byteOffset ?? 0) + v.byteLength);
  const result = { ...v, buffer: 0, byteOffset: offset };
  chunks.push(bytes); offset += bytes.length;
  const padding = (4 - offset % 4) % 4; chunks.push(Buffer.alloc(padding)); offset += padding;
  return result;
});
gltf.buffers = [{ byteLength: offset }];
const binary = Buffer.concat(chunks), json = Buffer.from(JSON.stringify(gltf));
const paddedJson = Buffer.concat([json, Buffer.alloc((4 - json.length % 4) % 4, 32)]);
const header = Buffer.alloc(20), binaryHeader = Buffer.alloc(8);
header.writeUInt32LE(0x46546c67); header.writeUInt32LE(2, 4); header.writeUInt32LE(28 + paddedJson.length + binary.length, 8);
header.writeUInt32LE(paddedJson.length, 12); header.writeUInt32LE(0x4e4f534a, 16);
binaryHeader.writeUInt32LE(binary.length); binaryHeader.writeUInt32LE(0x004e4942, 4);
await writeFile(target, Buffer.concat([header, paddedJson, binaryHeader, binary]));
console.log(`Imported Dash 9 into Godot: ${paintedTriangles} painted triangles, ${solidTriangles} solid triangles; authored bogies and six wheelsets replace temporary running gear.`);
