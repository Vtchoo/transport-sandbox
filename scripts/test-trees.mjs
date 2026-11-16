import assert from 'node:assert/strict';
import { test } from 'node:test';
import { buildSync } from 'esbuild';
const { outputFiles } = buildSync({ entryPoints: ['src/terrain/trees.ts', 'src/terrain/vegetation.ts', 'src/terrain/generator.ts', 'src/track/earthworks.ts'], outdir: 'unused', bundle: true, write: false, format: 'esm', platform: 'node', external: ['three'] });
const modules = await Promise.all(outputFiles.map(file => import('data:text/javascript;base64,' + Buffer.from(file.text.replace(/"three(?:\/[^\"]*)?"/g, spec => JSON.stringify(import.meta.resolve(JSON.parse(spec))))).toString('base64'))));
const { Trees } = modules[0], { generateTrees, TREE_STRIDE } = modules[1], { TerrainGenerator, DEFAULT_SETTINGS, CHUNK_SIZE } = modules[2], { EarthworkField } = modules[3];
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
