import { TerrainGenerator, buildChunk } from './generator';
import type { ChunkRequest } from './generator';

let generator: TerrainGenerator | undefined;
let signature = '';
self.onmessage = (event: MessageEvent<ChunkRequest>) => {
  const request = event.data;
  const nextSignature = JSON.stringify(request.settings);
  if (!generator || signature !== nextSignature) {
    generator = new TerrainGenerator(request.settings);
    signature = nextSignature;
  }
  const result = buildChunk(generator, request);
  self.postMessage(result, { transfer: [result.positions.buffer, result.normals.buffer, result.colors.buffer, result.indices.buffer] });
};
