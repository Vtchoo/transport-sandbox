import { CHUNK_SIZE, smooth } from './generator';
import type { TerrainGenerator } from './generator';

/** Local X, elevation, local Z, height, crown radius, yaw, species (0 broadleaf / 1 conifer). */
export const TREE_STRIDE = 7;
const SPACING = 8; // Divides chunk size; jitter stays inside its own global cell.

/** Stateless, independent of terrain tessellation, chunk order and worker scheduling. */
export function generateTrees(generator: TerrainGenerator, chunkX: number, chunkZ: number): Float32Array {
  const trees: number[] = [];
  const random = (x: number, z: number, salt: number) => {
    let h = generator.seed ^ Math.imul(x, 374761393) ^ Math.imul(z, 668265263) ^ salt;
    h = Math.imul(h ^ (h >>> 13), 1274126177);
    return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
  };
  const cells = CHUNK_SIZE / SPACING;
  const { seaLevel, horizontalScale } = generator.settings;
  for (let dz = 0; dz < cells; dz++) for (let dx = 0; dx < cells; dx++) {
    const cx = chunkX * cells + dx, cz = chunkZ * cells + dz;
    const localX = (dx + .18 + random(cx, cz, 31) * .64) * SPACING;
    const localZ = (dz + .18 + random(cx, cz, 53) * .64) * SPACING;
    const x = chunkX * CHUNK_SIZE + localX, z = chunkZ * CHUNK_SIZE + localZ;
    const forest = generator.noise(x / (650 * horizontalScale), z / (650 * horizontalScale), 307);
    const patch = generator.noise(x / 105, z / 105, 311);
    const density = (.025 + .73 * smooth(-.28, .42, forest + patch * .3)) / 4;
    const chance = random(cx, cz, 79);
    if (chance > density) continue;
    const y = generator.baseHeight(x, z);
    const shore = smooth(seaLevel + 4, seaLevel + 16, y);
    const treeline = 1 - smooth(seaLevel + 1000, seaLevel + 1450, y + generator.noise(x / 400, z / 400, 317) * 90);
    if (chance > density * shore * treeline) continue;
    const slope = Math.hypot(generator.baseHeight(x + 4, z) - generator.baseHeight(x - 4, z),
      generator.baseHeight(x, z + 4) - generator.baseHeight(x, z - 4)) / 8;
    if (chance > density * shore * treeline * (1 - smooth(.35, .8, slope))) continue;
    const earthwork = generator.earthworks?.nearest(x, z);
    if (earthwork && (earthwork.distance < 8 || (Number.isFinite(earthwork.width)
      ? earthwork.distance < earthwork.width + 4
      : Math.abs(generator.height(x, z) - y) > .05))) continue;
    const conifer = random(cx, cz, 101) < .2 + .65 * smooth(seaLevel + 250, seaLevel + 1000, y) ? 1 : 0;
    const height = (conifer ? 13 : 9) + random(cx, cz, 127) * (conifer ? 13 : 12);
    const radius = height * (conifer ? .19 : .26) * (.85 + random(cx, cz, 149) * .3);
    trees.push(localX, y, localZ, height, radius, random(cx, cz, 173) * Math.PI * 2, conifer);
  }
  return new Float32Array(trees);
}
