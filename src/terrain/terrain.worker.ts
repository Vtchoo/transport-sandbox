import { CHUNK_SIZE, TerrainGenerator, buildChunk } from './generator';
import type { ChunkRequest } from './generator';
import { EarthworkField } from '../track/earthworks';
import { generateTrees, TREE_STRIDE } from './vegetation';
import { prepareTrees, treeTransfers } from './tree-data';

let generator: TerrainGenerator | undefined;
let signature = '';
let revision = -1;
// Per-worker bounded placement cache. Clone before seating roots or transferring buffers.
const vegetation = new Map<string, Float32Array>();
let vegetationBytes = 0;
self.onmessage = (event: MessageEvent<ChunkRequest>) => {
  const request = event.data;
  const nextSignature = JSON.stringify(request.settings);
  if (!generator || signature !== nextSignature) {
    generator = new TerrainGenerator(request.settings);
    signature = nextSignature;
    revision = -1;
    vegetation.clear(); vegetationBytes = 0;
  }
  if (revision !== (request.revision ?? 0)) {
    generator.earthworks = request.earthPaths?.length ? new EarthworkField(request.earthPaths) : undefined;
    revision = request.revision ?? 0;
    vegetation.clear(); vegetationBytes = 0;
  }
  const result = buildChunk(generator, request);
  // Vegetation work stays off the render thread and is bounded to detailed nearby tiles.
  const key = `${request.x},${request.z}`;
  let natural = vegetation.get(key);
  if (request.segments >= 64 && !natural) {
    natural = generateTrees(generator, request.x, request.z);
    vegetation.set(key, natural); vegetationBytes += natural.byteLength;
    while (vegetation.size > 128 || vegetationBytes > 8 * 1024 * 1024) {
      const [oldKey, old] = vegetation.entries().next().value!;
      vegetation.delete(oldKey); vegetationBytes -= old.byteLength;
    }
  }
  result.trees = request.segments >= 64 ? natural!.slice() : new Float32Array();
  const n = request.segments, side = n + 1;
  if (result.positions.length === (side * side + side * 4) * 3) {
    // Seat roots on rendered triangles rather than the analytic heightfield.
    for (let i = 0; i < result.trees.length; i += TREE_STRIDE) {
      const gx = result.trees[i] / CHUNK_SIZE * n, gz = result.trees[i + 2] / CHUNK_SIZE * n;
      const x = Math.floor(gx), z = Math.floor(gz), u = gx - x, v = gz - z;
      const a = result.positions[(z * side + x) * 3 + 1], b = result.positions[(z * side + x + 1) * 3 + 1];
      const c = result.positions[((z + 1) * side + x) * 3 + 1], d = result.positions[((z + 1) * side + x + 1) * 3 + 1];
      result.trees[i + 1] = u + v <= 1 ? a + (b - a) * u + (c - a) * v : d + (c - d) * (1 - u) + (b - d) * (1 - v);
    }
  }
  result.preparedTrees = prepareTrees(result.trees);
  self.postMessage(result, { transfer: [result.positions.buffer, result.normals.buffer, result.colors.buffer, result.indices.buffer,
    result.trees.buffer, ...treeTransfers(result.preparedTrees)] });
};
