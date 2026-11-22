import { CHUNK_SIZE } from './generator';

export interface Tile { x: number; z: number; segments: number; distance: number; ring: number }
export const VIEW_RADIUS = 10;
export function wantedTiles(worldX: number, worldZ: number, altitude = 0, heightAt: (x: number, z: number) => number = () => 0): Tile[] {
  const cx = Math.floor(worldX / CHUNK_SIZE), cz = Math.floor(worldZ / CHUNK_SIZE);
  const tiles: Tile[] = [];
  for (let dz = -VIEW_RADIUS; dz <= VIEW_RADIUS; dz++) for (let dx = -VIEW_RADIUS; dx <= VIEW_RADIUS; dx++) {
    const ring = Math.max(Math.abs(dx), Math.abs(dz));
    const x = cx + dx, z = cz + dz;
    const height = heightAt((x + 0.5) * CHUNK_SIZE, (z + 0.5) * CHUNK_SIZE);
    const verticalDistance = Math.abs(altitude - height) / CHUNK_SIZE;
    const detailDistance = Math.max(ring, verticalDistance);
    tiles.push({ x, z, segments: detailDistance <= 1.5 ? 128 : detailDistance <= 3.5 ? 64 : detailDistance <= 6.5 ? 32 : 16,
      distance: Math.hypot(dx, dz, verticalDistance), ring });
  }
  return tiles.sort((a, b) => a.distance - b.distance);
}
