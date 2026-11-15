import { TerrainGenerator, buildChunk } from './generator';
import type { ChunkRequest } from './generator';
import { EarthworkField } from '../track/earthworks';

let generator: TerrainGenerator | undefined;
let signature = '';
let revision = -1;
self.onmessage = (event: MessageEvent<ChunkRequest>) => {
  const request = event.data;
  const nextSignature = JSON.stringify(request.settings);
  if (!generator || signature !== nextSignature) {
    generator = new TerrainGenerator(request.settings);
    signature = nextSignature;
    revision = -1;
  }
  if (revision !== (request.revision ?? 0)) {
    generator.earthworks = request.earthPaths?.length ? new EarthworkField(request.earthPaths) : undefined;
    revision = request.revision ?? 0;
  }
  const result = buildChunk(generator, request);
  self.postMessage(result, { transfer: [result.positions.buffer, result.normals.buffer, result.colors.buffer, result.indices.buffer] });
};
