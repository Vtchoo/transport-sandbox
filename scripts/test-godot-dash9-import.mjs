/** Verify the authored v5 geometry survives the Godot import, including rotated trucks. */
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { DOMParser } from 'linkedom';
import * as THREE from 'three';
import { ColladaLoader } from 'three/addons/loaders/ColladaLoader.js';

const source = '.files/dash-9/uv-paint-only/v5/dash9-v5-uv.dae';
const model = 'godot/assets/vehicles/ge-dash9-1435.glb';
const previous = globalThis.DOMParser;
let scene;
try { globalThis.DOMParser = DOMParser; scene = new ColladaLoader().parse(fs.readFileSync(source, 'utf8'), '').scene; }
finally { if (previous === undefined) delete globalThis.DOMParser; else globalThis.DOMParser = previous; }
scene.updateMatrixWorld(true);
const signature = points => points.map(p => p.toArray().map(n => Math.round(n * 10000)).join(',')).sort().join('|');
const expected = [];
scene.traverse(object => {
  if (!object.isMesh) return;
  const geometry = object.geometry.index ? object.geometry.toNonIndexed() : object.geometry.clone();
  geometry.applyMatrix4(object.matrixWorld);
  for (let i = 0; i < geometry.attributes.position.count; i += 3) {
    const p = [0, 1, 2].map(j => new THREE.Vector3().fromBufferAttribute(geometry.attributes.position, i + j));
    const n = new THREE.Vector3().fromBufferAttribute(geometry.attributes.normal, i);
    const area = p[1].clone().sub(p[0]).cross(p[2].clone().sub(p[0]));
    if (area.lengthSq() < 1e-20 || Math.abs(area.dot(n)) <= area.length() * 1e-8) continue;
    expected.push(signature(p));
  }
  geometry.dispose();
});
const bytes = fs.readFileSync(model), jsonLength = bytes.readUInt32LE(12);
const gltf = JSON.parse(bytes.subarray(20, 20 + jsonLength)), binary = bytes.subarray(28 + jsonLength);
function values(index, width) {
  const a = gltf.accessors[index], view = gltf.bufferViews[a.bufferView];
  assert.equal(a.componentType, 5126);
  const result = [];
  for (let i = 0; i < a.count; i++) for (let j = 0; j < width; j++)
    result.push(binary.readFloatLE((view.byteOffset ?? 0) + (a.byteOffset ?? 0) + i * (view.byteStride ?? width * 4) + j * 4));
  return result;
}
const actual = [], parents = new Map();
function walk(index, parent) {
  const node = gltf.nodes[index];
  const local = node.matrix ? new THREE.Matrix4().fromArray(node.matrix) : new THREE.Matrix4().compose(
    new THREE.Vector3().fromArray(node.translation ?? [0, 0, 0]), new THREE.Quaternion().fromArray(node.rotation ?? [0, 0, 0, 1]),
    new THREE.Vector3().fromArray(node.scale ?? [1, 1, 1]));
  const world = parent.clone().multiply(local);
  if (node.mesh !== undefined) for (const primitive of gltf.meshes[node.mesh].primitives) {
    assert.equal(primitive.indices, undefined, 'Authored import uses explicit triangle vertices');
    const data = values(primitive.attributes.POSITION, 3);
    for (let i = 0; i < data.length; i += 9)
      actual.push(signature([0, 3, 6].map(offset => new THREE.Vector3().fromArray(data, i + offset).applyMatrix4(world))));
  }
  for (const child of node.children ?? []) { parents.set(child, index); walk(child, world); }
}
gltf.scenes[gltf.scene ?? 0].nodes.forEach(index => walk(index, new THREE.Matrix4()));
assert.deepEqual(actual.sort(), expected.sort(), 'Authored body, frame and wheel geometry must retain its world placement to 0.1 mm');
for (const name of ['bogie_0', 'bogie_1']) {
  const index = gltf.nodes.findIndex(n => n.name === name);
  const wheels = gltf.nodes.map((n, i) => [n, i]).filter(([n, i]) => parents.get(i) === index && /^wheelset-\d{2}$/.test(n.name));
  assert.deepEqual(wheels.map(([n]) => n.name).sort(), ['wheelset-01', 'wheelset-02', 'wheelset-03']);
}
assert.equal(gltf.nodes.filter(n => n.name === 'wheel').length, 0, 'No temporary procedural wheels remain');
const image = gltf.bufferViews[gltf.images[0].bufferView];
assert.deepEqual(binary.subarray(image.byteOffset, image.byteOffset + image.byteLength), fs.readFileSync('.files/dash-9/dash9 v5 santa fe texture.png'));
console.log(`PASS: ${actual.length} authored triangles preserve placement; six named wheelsets, no temporary wheels, Santa Fe PNG retained.`);
