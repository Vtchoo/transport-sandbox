import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as THREE from 'three';
import { buildSync } from 'esbuild';
const { outputFiles } = buildSync({ entryPoints: ['src/terrain/trees.ts', 'src/terrain/vegetation.ts', 'src/terrain/generator.ts', 'src/track/earthworks.ts', 'src/terrain/tree-data.ts', 'src/terrain/tree-streaming.ts'], outdir: 'unused', bundle: true, write: false, format: 'esm', platform: 'node', external: ['three'] });
const modules = await Promise.all(outputFiles.map(file => import('data:text/javascript;base64,' + Buffer.from(file.text.replace(/"three(?:\/[^\"]*)?"/g, spec => JSON.stringify(import.meta.resolve(JSON.parse(spec))))).toString('base64'))));
const { Trees } = modules[0], { generateTrees, TREE_STRIDE } = modules[1], { TerrainGenerator, DEFAULT_SETTINGS, CHUNK_SIZE } = modules[2], { EarthworkField } = modules[3];
const { prepareTrees } = modules[4], { TreeStreaming } = modules[5];
const records = data => Array.from({ length: data.length / TREE_STRIDE }, (_, i) => Array.from(data.subarray(i * TREE_STRIDE, (i + 1) * TREE_STRIDE)));
const flat = (height = 100) => { const generator = new TerrainGenerator(DEFAULT_SETTINGS); generator.baseHeight = () => height; return generator; };

test('forests reproduce across chunk order, negative coordinates and seed changes', () => {
  const generator = new TerrainGenerator(DEFAULT_SETTINGS), other = new TerrainGenerator(DEFAULT_SETTINGS);
  const points = [[0, 0], [-1, -2], [1, 1]];
  const samples = points.map(([x, z]) => generateTrees(generator, x, z));
  points.toReversed().forEach(([x, z], i) => assert.deepEqual(generateTrees(other, x, z), samples[samples.length - i - 1]));
  assert.ok(samples.some(data => data.length > 0));
  const changed = new TerrainGenerator({ ...DEFAULT_SETTINGS, seed: 'another-forest' });
  assert.notDeepEqual(generateTrees(changed, 0, 0), samples[0]);
  for (const data of samples) for (const [x, y, z, height, radius, yaw, species] of records(data)) {
    assert.ok(x > 0 && x < CHUNK_SIZE && z > 0 && z < CHUNK_SIZE);
    assert.ok(y > DEFAULT_SETTINGS.seaLevel + 4 && y < 1550);
    assert.ok(height >= 9 && height <= 26 && radius > 1 && radius < 8);
    assert.ok(Number.isFinite(yaw) && (species === 0 || species === 1));
  }
});

test('trees leave water, high mountains and steep slopes bare, with varied forest density', () => {
  assert.equal(generateTrees(flat(-10), 0, 0).length, 0);
  assert.equal(generateTrees(flat(1700), 0, 0).length, 0);
  const steep = flat(); steep.baseHeight = x => 300 + x;
  assert.equal(generateTrees(steep, 0, 0).length, 0);
  const generator = flat();
  const counts = Array.from({ length: 9 }, (_, x) => generateTrees(generator, x - 4, 0).length / TREE_STRIDE);
  assert.ok(Math.max(...counts) > Math.min(...counts) * 1.5);
  assert.ok(Math.min(...counts) > 0 && Math.max(...counts) < 4096);
});

test('earthworks clear their full footprint and preserve trees elsewhere; removal restores trees', () => {
  const generator = flat(), original = generateTrees(generator, 0, 0);
  generator.earthworks = new EarthworkField([{ points: [{ x: 0, y: 100.65, z: 256, nx: 0, nz: 1, left: 40, right: 40 }, { x: 512, y: 100.65, z: 256, nx: 0, nz: 1, left: 40, right: 40 }] }]);
  const cleared = records(generateTrees(generator, 0, 0));
  assert.ok(cleared.length < original.length / TREE_STRIDE);
  assert.deepEqual(cleared, records(original).filter(tree => Math.abs(tree[2] - 256) >= 44));
  generator.earthworks = undefined;
  assert.deepEqual(generateTrees(generator, 0, 0), original);
});

test('tree batches share geometry, reduce distant geometry, and release instance buffers independently', () => {
  const renderer = new Trees(), data = generateTrees(flat(), 0, 0);
  const a = renderer.create(data), b = renderer.create(data);
  assert.equal(a.count, data.length / TREE_STRIDE);
  assert.ok(a.meshes.length <= 4 && a.count > 50);
  assert.equal(a.meshes[0].geometry, b.meshes[0].geometry);
  const triangles = chunk => chunk.meshes.filter(mesh => mesh.visible).reduce((sum, mesh) => sum + mesh.count * (mesh.geometry.index?.count ?? mesh.geometry.attributes.position.count) / 3, 0);
  const near = triangles(a);
  renderer.update(a, 1600);
  assert.ok(triangles(a) < near / 4);
  renderer.update(a, 200);
  assert.equal(triangles(a), near);
  assert.ok(a.meshes.every(mesh => mesh.boundingSphere && Number.isFinite(mesh.boundingSphere.radius)));
  let instancesDisposed = 0, geometriesDisposed = 0;
  a.meshes.forEach(mesh => mesh.addEventListener('dispose', () => instancesDisposed++));
  a.meshes[0].geometry.addEventListener('dispose', () => geometriesDisposed++);
  renderer.release(a);
  assert.equal(instancesDisposed, a.meshes.length);
  assert.equal(geometriesDisposed, 0);
  renderer.release(b); renderer.dispose();
  assert.equal(geometriesDisposed, 1);
});


test('worker-prepared matrices, colors and both LOD bounds exactly match per-instance Three.js calculations', () => {
  const renderer = new Trees(), data = generateTrees(flat(), 0, 0), prepared = prepareTrees(data);
  const chunk = renderer.createPrepared(prepared), transform = new THREE.Object3D(), color = new THREE.Color();
  for (const [index, batch] of prepared.batches.entries()) {
    renderer.update(chunk, 0);
    const indices = records(data).filter(tree => tree[6] === batch.species);
    const trunk = new THREE.InstancedMesh(chunk.meshes[index * 2].geometry, undefined, indices.length);
    const crown = new THREE.InstancedMesh(chunk.meshes[index * 2 + 1].geometry, undefined, indices.length);
    indices.forEach(([x, y, z, height, radius, yaw], i) => {
      transform.position.set(x, y - .8, z); transform.rotation.set(0, yaw, 0);
      transform.scale.set(height, height, height); transform.updateMatrix(); trunk.setMatrixAt(i, transform.matrix);
      transform.scale.set(radius, height, radius); transform.updateMatrix(); crown.setMatrixAt(i, transform.matrix);
      color.setHSL((batch.species ? .32 : .28) + Math.sin(yaw) * .018, .42 + Math.cos(yaw) * .06, batch.species ? .14 : .19);
      crown.setColorAt(i, color);
    });
    assert.deepEqual(batch.trunks, trunk.instanceMatrix.array);
    assert.deepEqual(batch.crowns, crown.instanceMatrix.array);
    assert.deepEqual(batch.colors, crown.instanceColor.array);
    const compareBounds = (reference, actual) => {
      reference.computeBoundingSphere(); reference.computeBoundingBox();
      assert.deepEqual(actual.boundingSphere, reference.boundingSphere);
      assert.deepEqual(actual.boundingBox, reference.boundingBox);
    };
    renderer.update(chunk, 0);
    compareBounds(trunk, chunk.meshes[index * 2]); compareBounds(crown, chunk.meshes[index * 2 + 1]);
    renderer.update(chunk, 900); crown.geometry = chunk.meshes[index * 2 + 1].geometry;
    compareBounds(crown, chunk.meshes[index * 2 + 1]);
    const sphere = chunk.meshes[index * 2 + 1].boundingSphere;
    for (let i = 0; i < 100; i++) renderer.update(chunk, 900);
    assert.equal(chunk.meshes[index * 2 + 1].boundingSphere, sphere);
    trunk.dispose(); crown.dispose(); trunk.material.dispose(); crown.material.dispose();
  }
  renderer.release(chunk); renderer.dispose();
});

test('streamed trees reuse batches and buffers across LODs, retain immutable cached data, and defer disposal', () => {
  const renderer = new Trees(), stream = new TreeStreaming(renderer), holder = new THREE.Group();
  const data = generateTrees(flat(), 0, 0), first = prepareTrees(data);
  const original = first.batches[0].crowns.slice();
  const visual = stream.activate('0,0', holder, first, false);
  const mesh = visual.meshes[1], attribute = mesh.instanceMatrix;
  stream.deactivate('0,0');
  assert.equal(stream.activate('0,0', holder, first, true), visual);
  assert.equal(visual.group.visible, false); assert.equal(stream.uploads, 1);
  const moved = data.slice(); for (let i = 1; i < moved.length; i += TREE_STRIDE) moved[i] += 3;
  const second = prepareTrees(moved);
  assert.equal(stream.activate('0,0', new THREE.Group(), second, false), visual);
  assert.equal(visual.group.visible, true); assert.equal(visual.meshes[1], mesh);
  assert.equal(mesh.instanceMatrix, attribute); assert.deepEqual(first.batches[0].crowns, original);
  assert.deepEqual(mesh.instanceMatrix.array, second.batches[0].crowns);
  stream.activate('0,0', holder, first, false);
  assert.deepEqual(mesh.instanceMatrix.array, original);
  let disposed = 0; mesh.addEventListener('dispose', () => disposed++);
  stream.budgetBytes = 0; stream.deactivate('0,0');
  assert.equal(disposed, 0); assert.equal(stream.cachedBytes, 0); assert.equal(stream.pendingDisposals, 1);
  stream.releaseOne(); assert.equal(disposed, 1);
  stream.dispose(); renderer.dispose();
});

test('terrain worker transfers prepared buffers safely and preserves LOD-specific root seating', async () => {
  const previous = globalThis.self; let result;
  globalThis.self = { postMessage(data, options) { result = structuredClone(data, options); } };
  try {
    const code = buildSync({entryPoints:['src/terrain/terrain.worker.ts'],bundle:true,write:false,format:'esm',platform:'node',external:['three','three/*']}).outputFiles[0].text
      .replace(/"three(?:\/[^\"]*)?"/g, spec => JSON.stringify(import.meta.resolve(JSON.parse(spec))));
    await import('data:text/javascript;base64,' + Buffer.from(code).toString('base64'));
    const send = segments => { self.onmessage({data:{id:segments,x:0,z:0,segments,settings:DEFAULT_SETTINGS,revision:0}}); return result; };
    const first = send(64), detailed = send(128), returned = send(64);
    assert.ok(first.preparedTrees.count > 0);
    assert.deepEqual(returned.trees, first.trees); assert.deepEqual(returned.preparedTrees, first.preparedTrees);
    assert.equal(detailed.trees.length, first.trees.length);
    const n = detailed.segments, side = n + 1;
    for (let i = 0; i < detailed.trees.length; i += TREE_STRIDE) {
      const gx = detailed.trees[i] / CHUNK_SIZE * n, gz = detailed.trees[i + 2] / CHUNK_SIZE * n;
      const x = Math.floor(gx), z = Math.floor(gz), u = gx - x, v = gz - z;
      const a = detailed.positions[(z * side + x) * 3 + 1], b = detailed.positions[(z * side + x + 1) * 3 + 1];
      const c = detailed.positions[((z + 1) * side + x) * 3 + 1], d = detailed.positions[((z + 1) * side + x + 1) * 3 + 1];
      assert.equal(detailed.trees[i + 1], Math.fround(u + v <= 1 ? a + (b-a)*u + (c-a)*v : d + (c-d)*(1-u) + (b-d)*(1-v)));
    }
    assert.deepEqual(detailed.preparedTrees, prepareTrees(detailed.trees));
  } finally { globalThis.self = previous; }
});
