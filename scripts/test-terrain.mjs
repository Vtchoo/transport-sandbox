import assert from 'node:assert/strict';
import { test } from 'node:test';
import { transformSync } from 'esbuild';
import { readFile } from 'node:fs/promises';

// Load pure TS modules without a test framework or Node-specific application imports.
const source = await readFile(new URL('../src/terrain/generator.ts', import.meta.url), 'utf8');
const code = transformSync(source, { loader: 'ts', format: 'esm' }).code;
const moduleUrl = `data:text/javascript;base64,${Buffer.from(code).toString('base64')}`;
const { TerrainGenerator, DEFAULT_SETTINGS, CHUNK_SIZE, buildChunk } = await import(moduleUrl);
const lodSource = (await readFile(new URL('../src/terrain/lod.ts', import.meta.url), 'utf8')).replace("'./generator'", JSON.stringify(moduleUrl));
const lodCode = transformSync(lodSource, { loader: 'ts', format: 'esm' }).code;
const { wantedTiles, VIEW_RADIUS } = await import(`data:text/javascript;base64,${Buffer.from(lodCode).toString('base64')}`);
const generator = new TerrainGenerator(DEFAULT_SETTINGS);
const chunk = (x, z, segments) => buildChunk(generator, { id: 1, x, z, segments, settings: DEFAULT_SETTINGS });

test('seed reproduces terrain independently of sample order', () => {
  const other = new TerrainGenerator(DEFAULT_SETTINGS);
  const points = [[600, 900], [-537.5, -1024.5], [12345678, -8765432], [0, 0]];
  const samples = points.map(([x, z]) => generator.height(x, z));
  points.toReversed().forEach(([x, z], i) => assert.equal(other.height(x, z), samples[samples.length - 1 - i]));
  const changed = new TerrainGenerator({ ...DEFAULT_SETTINGS, seed: 'a-different-world' });
  assert.ok(points.some(([x, z], i) => changed.height(x, z) !== samples[i]));
  // Intentional landform changes should increment GENERATOR_VERSION.
  assert.ok(Math.abs(samples[0] - 769.7641622596988) < 1e-8);
});
test('mixed LOD chunk borders align at negative and distant coordinates', () => {
  for (const [x, z] of [[0, 0], [-2, -1], [100000, -100000]]) {
    const fine = chunk(x, z, 128), east = chunk(x + 1, z, 32), south = chunk(x, z + 1, 64);
    for (let i = 0; i <= 32; i++) assert.equal(fine.positions[(i * 4 * 129 + 128) * 3 + 1], east.positions[i * 33 * 3 + 1]);
    for (let i = 0; i <= 64; i++) assert.equal(fine.positions[(128 * 129 + i * 2) * 3 + 1], south.positions[i * 3 + 1]);
    assert.equal(fine.positions[128 * 3], CHUNK_SIZE);
  }
});
test('meshes have finite values, valid indices, upward winding, unit normals and skirts', () => {
  for (const segments of [16, 32, 64, 128]) {
    const data = chunk(-1, 2, segments), vertices = data.positions.length / 3;
    assert.equal(vertices, (segments + 1) ** 2 + 4 * (segments + 1));
    for (const values of [data.positions, data.colors, data.normals]) assert.ok(values.every(Number.isFinite));
    assert.ok(data.indices.every(index => index < vertices));
    for (let i = 0; i < data.normals.length; i += 3) assert.ok(Math.abs(Math.hypot(...data.normals.subarray(i, i + 3)) - 1) < 1e-6);
    const [a, b, c] = data.indices;
    const abx = data.positions[b * 3] - data.positions[a * 3], abz = data.positions[b * 3 + 2] - data.positions[a * 3 + 2];
    const acx = data.positions[c * 3] - data.positions[a * 3], acz = data.positions[c * 3 + 2] - data.positions[a * 3 + 2];
    assert.ok(abz * acx - abx * acz > 0);
    assert.ok(Math.abs(data.positions[(segments + 1) ** 2 * 3 + 1] - (data.positions[1] - 180)) < 0.001);
  }
});
test('LOD allocation is bounded, follows the camera and reduces detail at altitude', () => {
  const tiles = wantedTiles(0, 0), distant = wantedTiles(-10000000, 10000000);
  assert.equal(tiles.length, (VIEW_RADIUS * 2 + 1) ** 2);
  assert.equal(distant.length, tiles.length);
  assert.equal(new Set(tiles.map(t => `${t.x},${t.z}`)).size, tiles.length);
  assert.equal(tiles.filter(t => t.segments === 128).length, 9);
  assert.equal(tiles[0].x, 0); assert.equal(tiles[0].z, 0);
  assert.ok(distant.every(t => Math.abs(t.x - Math.floor(-10000000 / CHUNK_SIZE)) <= VIEW_RADIUS));
  assert.ok(wantedTiles(0, 0, 4000).every(t => t.segments === 16));
  const budget = tiles.reduce((sum, t) => sum + t.segments ** 2 * 2 + t.segments * 8, 0);
  assert.ok(budget < 1_500_000, `triangle budget exceeded: ${budget}`);
});
test('settings control scale and relief; terrain includes ocean and high mountains', () => {
  const taller = new TerrainGenerator({ ...DEFAULT_SETTINGS, mountainHeight: 3000 });
  assert.ok(taller.height(600, 900) > generator.height(600, 900));
  const wider = new TerrainGenerator({ ...DEFAULT_SETTINGS, horizontalScale: 2 });
  assert.equal(wider.height(1200, 1800), generator.height(600, 900));
  let min = Infinity, max = -Infinity;
  for (let z = -10000; z <= 10000; z += 500) for (let x = -10000; x <= 10000; x += 500) {
    const h = generator.height(x, z); assert.ok(Number.isFinite(h)); min = Math.min(h, min); max = Math.max(h, max);
  }
  assert.ok(min < -100 && max > 1500);
});
