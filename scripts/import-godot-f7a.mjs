/** Import the authored F7A and finished paint atlas as a separate native vehicle. */
import { readFile, writeFile, mkdir, copyFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';
import { updateArrayEntry } from './lib/update-json-entry.mjs';
import { materialRole } from './lib/material-role.mjs';

const root = new URL('../', import.meta.url);
const require = createRequire(new URL('web/package.json', root));
const THREE = await import(pathToFileURL(require.resolve('three')).href);
const { DOMParser } = require('linkedom');
const { ColladaLoader } = await import(pathToFileURL(require.resolve('three/addons/loaders/ColladaLoader.js')).href);
const version = process.argv.find(a => a.startsWith('--version='))?.slice(10) ?? 'v5';
assert.match(version, /^v\d+$/, 'Version must be v followed by digits');
const kit = new URL(`.files/f7a/uv-paint-only/${version}/`, root);
const source = new URL(`f7a-${version}-uv.dae`, kit);
const texture = new URL(`.files/f7a/f7a ${version} santa fe.png`, root);
const target = new URL('godot/assets/vehicles/emd-f7a-1435.glb', root);
const bytes = await readFile(source), png = await readFile(texture);
assert.equal(png.subarray(1, 4).toString(), 'PNG');
assert.equal(png.readUInt32BE(16), png.readUInt32BE(20), 'Square paint atlas expected');
const xml = new DOMParser().parseFromString(bytes.toString(), 'text/xml');
const unit = Number(xml.querySelector('asset unit').getAttribute('meter'));
let scene;
const previous = globalThis.DOMParser;
try { globalThis.DOMParser = DOMParser; scene = new ColladaLoader().parse(bytes.toString(), '').scene; }
finally { if (previous === undefined) delete globalThis.DOMParser; else globalThis.DOMParser = previous; }
scene.updateMatrixWorld(true);

const gltf = { asset: { version: '2.0', generator: 'Transport Sandbox F7A importer' }, scene: 0,
  scenes: [{ nodes: [0] }], nodes: [{ name: 'F7A', children: [] }],
  meshes: [], materials: [], accessors: [], bufferViews: [],
  samplers: [{ magFilter: 9729, minFilter: 9987, wrapS: 33071, wrapT: 33071 }], textures: [{ sampler: 0, source: 0 }] };
const blocks = []; let length = 0;
function view(buffer, target) {
  const padding = (4 - length % 4) % 4;
  if (padding) { blocks.push(Buffer.alloc(padding)); length += padding; }
  const id = gltf.bufferViews.length;
  gltf.bufferViews.push({ buffer: 0, byteOffset: length, byteLength: buffer.length, ...(target ? { target } : {}) });
  blocks.push(buffer); length += buffer.length; return id;
}
function attribute(values, size) {
  const data = new Float32Array(values);
  const accessor = { bufferView: view(Buffer.from(data.buffer), 34962), componentType: 5126, count: data.length / size, type: size === 3 ? 'VEC3' : 'VEC2' };
  if (size === 3) {
    accessor.min = [Infinity, Infinity, Infinity]; accessor.max = [-Infinity, -Infinity, -Infinity];
    for (let i = 0; i < data.length; i++) {
      accessor.min[i % 3] = Math.min(accessor.min[i % 3], data[i]);
      accessor.max[i % 3] = Math.max(accessor.max[i % 3], data[i]);
    }
  }
  gltf.accessors.push(accessor); return gltf.accessors.length - 1;
}
gltf.images = [{ bufferView: view(png), mimeType: 'image/png', name: 'F7A Santa Fe paint' }];
const semantic = name => (name ?? '').toLowerCase().replaceAll('_', '-');
const materials = new Map(), pivots = {}, bounds = new THREE.Box3();
let paintedTriangles = 0, solidTriangles = 0, wheelsetCount = 0, degenerateTriangles = 0;
for (const [part, nativeName, station] of [['body', 'body', 0], ['bogie-front', 'bogie_0', 4.572], ['bogie-rear', 'bogie_1', -4.572]]) {
  const matches = [];
  scene.traverse(node => { if (semantic(node.name) === part) matches.push(node); });
  assert.equal(matches.length, 1, `One authored ${part} is required`);
  const component = matches[0], origin = component.getWorldPosition(new THREE.Vector3());
  assert.ok(Math.abs(origin.x) < .01 && Math.abs(origin.y) < .01 && Math.abs(origin.z - station) < .01, `${part}: preserve rail-level authored pivot`);
  pivots[part] = origin.toArray();
  const native = { name: nativeName, translation: [0, 0, origin.z], children: [] };
  gltf.nodes[0].children.push(gltf.nodes.length); gltf.nodes.push(native);
  const wheels = new Map(), authored = [];
  component.traverse(node => { if (/^wheelset(?:-\d{2})?$/.test(semantic(node.name))) authored.push(node); });
  authored.sort((a, b) => b.getWorldPosition(new THREE.Vector3()).z - a.getWorldPosition(new THREE.Vector3()).z);
  if (part !== 'body') assert.equal(authored.length, 2, `${part}: two authored wheelsets required`);
  for (const [index, node] of authored.entries()) {
    const position = new THREE.Vector3(), rotation = new THREE.Quaternion(), scale = new THREE.Vector3();
    node.matrixWorld.decompose(position, rotation, scale);
    const world = new THREE.Matrix4().compose(position, rotation, scale.divideScalar(unit));
    const local = new THREE.Matrix4().makeTranslation(0, 0, -origin.z).multiply(world);
    const wheel = { name: `wheelset-${String(index + 1).padStart(2, '0')}`, matrix: local.toArray(), children: [] };
    native.children.push(gltf.nodes.length); gltf.nodes.push(wheel); wheels.set(node, { native: wheel, world }); wheelsetCount++;
  }
  component.traverse(object => {
    if (!object.isMesh) return;
    const geometry = object.geometry.index ? object.geometry.toNonIndexed() : object.geometry.clone();
    if (!geometry.attributes.normal) geometry.computeVertexNormals();
    let ancestor = object.parent;
    while (ancestor && ancestor !== component && !wheels.has(ancestor)) ancestor = ancestor.parent;
    const wheel = wheels.get(ancestor);
    const transform = wheel ? wheel.world.clone().invert().multiply(object.matrixWorld)
      : new THREE.Matrix4().makeTranslation(0, 0, -origin.z).multiply(object.matrixWorld);
    geometry.applyMatrix4(transform);
    const available = Array.isArray(object.material) ? object.material : [object.material];
    const groups = geometry.groups.length ? geometry.groups : [{ start: 0, count: geometry.attributes.position.count, materialIndex: 0 }];
    for (const group of groups) {
      const material = available[group.materialIndex], painted = /livery/i.test(material.name);
      const positions = [], normals = [], uv = [];
      for (let start = group.start; start < group.start + group.count; start += 3) {
        const order = transform.determinant() < 0 ? [0, 2, 1] : [0, 1, 2];
        const vertices = order.map(i => new THREE.Vector3().fromBufferAttribute(geometry.attributes.position, start + i));
        const area = vertices[1].clone().sub(vertices[0]).cross(vertices[2].clone().sub(vertices[0]));
        if (area.lengthSq() < 1e-20) { degenerateTriangles++; continue; }
        const normal = new THREE.Vector3().fromBufferAttribute(geometry.attributes.normal, start + order[0]);
        if (area.dot(normal) < 0) [order[1], order[2]] = [order[2], order[1]];
        for (const offset of order) {
          const i = start + offset, p = new THREE.Vector3().fromBufferAttribute(geometry.attributes.position, i);
          positions.push(...p.toArray());
          normals.push(...new THREE.Vector3().fromBufferAttribute(geometry.attributes.normal, i).toArray());
          const world = wheel ? p.clone().applyMatrix4(wheel.world) : p.clone().add(new THREE.Vector3(0, 0, origin.z));
          bounds.expandByPoint(world);
          if (painted) {
            assert.ok(geometry.attributes.uv, 'Painted faces require UVs');
            const u = geometry.attributes.uv.getX(i), v = geometry.attributes.uv.getY(i);
            assert.ok(Number.isFinite(u) && Number.isFinite(v) && u >= 0 && u <= 1 && v >= 0 && v <= 1);
            uv.push(u, 1 - v); // COLLADA bottom-left to glTF image coordinates.
          }
        }
      }
      if (!positions.length) continue;
      const nativeMaterial = { name: painted ? 'F7A paintable panels' : materialRole(material.name),
          pbrMetallicRoughness: { baseColorFactor: [...(painted ? [1, 1, 1] : material.color.toArray()), material.opacity],
            metallicFactor: /stainless/i.test(material.name) ? .5 : .1, roughnessFactor: .65,
            ...(painted ? { baseColorTexture: { index: 0 } } : {}) },
          ...(material.opacity < .999 ? { alphaMode: 'BLEND' } : {}),
          ...(material.side === THREE.DoubleSide ? { doubleSided: true } : {}) };
      // Authoring groups have served their purpose once UVs are attached. Share
      // identical runtime materials instead of retaining one per UV group.
      const materialKey = JSON.stringify(nativeMaterial);
      if (!materials.has(materialKey)) {
        materials.set(materialKey, gltf.materials.length);
        gltf.materials.push(nativeMaterial);
      }
      const attributes = { POSITION: attribute(positions, 3), NORMAL: attribute(normals, 3) };
      if (painted) { attributes.TEXCOORD_0 = attribute(uv, 2); paintedTriangles += positions.length / 9; }
      else solidTriangles += positions.length / 9;
      (wheel?.native ?? native).children.push(gltf.nodes.length);
      gltf.nodes.push({ name: `${part}-${painted ? 'paint' : 'solid'}-${gltf.meshes.length}`, mesh: gltf.meshes.length });
      gltf.meshes.push({ primitives: [{ attributes, material: materials.get(materialKey), mode: 4 }] });
    }
    geometry.dispose();
  });
}
assert.equal(wheelsetCount, 4);
const uvReport = JSON.parse(await readFile(new URL('uv-report.json', kit)));
assert.equal(paintedTriangles, uvReport.paintedTriangles, 'All marked paint faces must survive import');
assert.equal(paintedTriangles + solidTriangles + degenerateTriangles, uvReport.triangles, 'Every source triangle must be accounted for');
const padding = (4 - length % 4) % 4;
if (padding) { blocks.push(Buffer.alloc(padding)); length += padding; }
gltf.buffers = [{ byteLength: length }];
const binary = Buffer.concat(blocks), json = Buffer.from(JSON.stringify(gltf));
const paddedJson = Buffer.concat([json, Buffer.alloc((4 - json.length % 4) % 4, 32)]);
const header = Buffer.alloc(20), binHeader = Buffer.alloc(8);
header.writeUInt32LE(0x46546c67); header.writeUInt32LE(2, 4); header.writeUInt32LE(28 + paddedJson.length + binary.length, 8);
header.writeUInt32LE(paddedJson.length, 12); header.writeUInt32LE(0x4e4f534a, 16);
binHeader.writeUInt32LE(binary.length); binHeader.writeUInt32LE(0x004e4942, 4);
const catalogPath = new URL('godot/assets/vehicles/catalog.json', root);
const catalogRaw = await readFile(catalogPath, 'utf8');
const catalog = JSON.parse(catalogRaw), original = catalog.find(v => v.id === 'atsf-f7a');
assert.ok(original, 'Keep the original F7A alongside the authored replacement');
const size = bounds.getSize(new THREE.Vector3());
const definition = { ...structuredClone(original), id: 'emd-f7a', name: 'F7A', width: size.x, height: bounds.max.y,
  bodyLength: size.z, bogieCenters: pivots['bogie-front'][2] - pivots['bogie-rear'][2],
  models: { '1435': 'res://assets/vehicles/emd-f7a-1435.glb' },
  thumbnail: 'res://assets/vehicles/thumbnails/emd-f7a.svg',
  liveryMaterial: 'F7A paintable panels', liveries: [{ id: 'santa-fe', name: 'Santa Fe', texture: 'res://assets/vehicles/liveries/f7a/santa-fe.png' }],
  exhausts: [{ position: [0, 4.49, -.702], kind: 'diesel', idleRate: 1.2 }, { position: [0, 4.49, -2.922], kind: 'diesel', idleRate: 1.2 }] };
definition.exhaust = { ...definition.exhausts[0] };
const soundsPath = new URL('godot/assets/audio/trains/sounds.json', root), soundsRaw = await readFile(soundsPath, 'utf8');
const sounds = JSON.parse(soundsRaw);
const soundEntry = `    "${definition.id}": ${JSON.stringify(sounds.vehicles['atsf-f7a'])}`;
const soundPattern = new RegExp(`^    "${definition.id}": \\{[^\\n]+\\},?`, 'm');
const updatedSounds = soundPattern.test(soundsRaw) ? soundsRaw.replace(soundPattern, soundEntry + ',')
  : soundsRaw.replace(/^(    "atsf-f7a": [^\n]+)$/m, '$1\n' + soundEntry + ',');
await mkdir(new URL('godot/assets/vehicles/liveries/f7a/', root), { recursive: true });
await writeFile(target, Buffer.concat([header, paddedJson, binHeader, binary]));
await copyFile(texture, new URL('godot/assets/vehicles/liveries/f7a/santa-fe.png', root));
await writeFile(catalogPath, updateArrayEntry(catalogRaw, definition.id, definition, original.id));
assert.ok(JSON.parse(updatedSounds).vehicles[definition.id]);
await writeFile(soundsPath, updatedSounds);
const report = { id: definition.id, name: definition.name, source: source.pathname, sourceSha256: createHash('sha256').update(bytes).digest('hex'),
  textureSha256: createHash('sha256').update(png).digest('hex'), paintedTriangles, solidTriangles, degenerateTriangles, wheelsetCount,
  bounds: { minimum: bounds.min.toArray(), maximum: bounds.max.toArray(), size: size.toArray() }, pivots,
  placementLength: definition.length, bogieCenters: definition.bogieCenters, textureSize: [png.readUInt32BE(16), png.readUInt32BE(20)] };
await writeFile(new URL('godot-import-report.json', kit), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
