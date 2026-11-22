import type { EarthworkField, EarthPath } from '../track/earthworks';
/** Version the settings alongside the seed: both define a reproducible world. Units are metres. */
export const GENERATOR_VERSION = 1;
export const CHUNK_SIZE = 512;
export interface TerrainSettings {
  seed: string;
  mountainHeight: number;
  horizontalScale: number;
  seaLevel: number;
}
export const DEFAULT_SETTINGS: TerrainSettings = {
  seed: 'the-long-way-home', mountainHeight: 1800, horizontalScale: 1, seaLevel: 0,
};
export function hashSeed(seed: string): number {
  let hash = 2166136261;
  for (let i = 0; i < seed.length; i++) hash = Math.imul(hash ^ seed.charCodeAt(i), 16777619);
  return hash >>> 0;
}
const mix = (a: number, b: number, t: number) => a + (b - a) * t;
const clamp = (x: number) => Math.max(0, Math.min(1, x));
export function smooth(a: number, b: number, x: number) {
  const t = clamp((x - a) / (b - a));
  return t * t * (3 - 2 * t);
}

/** Stateless gradient noise: chunk order, LOD, and worker scheduling never affect the result. */
export class TerrainGenerator {
  earthworks?: EarthworkField;
  readonly settings: TerrainSettings;
  readonly seed: number;
  constructor(settings: TerrainSettings) {
    this.settings = { ...settings };
    this.seed = hashSeed(settings.seed);
  }
  private hash(x: number, z: number, salt: number) {
    let h = this.seed ^ Math.imul(x, 374761393) ^ Math.imul(z, 668265263) ^ Math.imul(salt, 1274126177);
    h = Math.imul(h ^ (h >>> 13), 1274126177);
    return (h ^ (h >>> 16)) >>> 0;
  }
  noise(x: number, z: number, salt = 0) {
    const ix = Math.floor(x), iz = Math.floor(z), dx = x - ix, dz = z - iz;
    const fade = (t: number) => t * t * t * (t * (t * 6 - 15) + 10);
    const grad = (gx: number, gz: number, px: number, pz: number) => {
      switch (this.hash(gx, gz, salt) & 7) {
        case 0: return px; case 1: return -px; case 2: return pz; case 3: return -pz;
        case 4: return (px + pz) * Math.SQRT1_2; case 5: return (px - pz) * Math.SQRT1_2;
        case 6: return (-px + pz) * Math.SQRT1_2; default: return (-px - pz) * Math.SQRT1_2;
      }
    };
    return mix(mix(grad(ix, iz, dx, dz), grad(ix + 1, iz, dx - 1, dz), fade(dx)),
      mix(grad(ix, iz + 1, dx, dz - 1), grad(ix + 1, iz + 1, dx - 1, dz - 1), fade(dx)), fade(dz)) * 1.65;
  }
  fbm(x: number, z: number, salt: number, octaves = 4) {
    let result = 0, amplitude = 1, total = 0;
    for (let i = 0; i < octaves; i++) {
      result += this.noise(x, z, salt + i * 101) * amplitude;
      total += amplitude;
      // Rotation avoids axis-aligned octaves; noninteger lacunarity avoids repeating harmonics.
      const nextX = (x * 0.8 - z * 0.6) * 2.03 + 17.1;
      z = (x * 0.6 + z * 0.8) * 2.03 - 9.2;
      x = nextX;
      amplitude *= 0.49;
    }
    return result / total;
  }
  height(worldX: number, worldZ: number): number {
    const natural = this.baseHeight(worldX, worldZ);
    return this.earthworks?.height(worldX, worldZ, natural) ?? natural;
  }
  baseHeight(worldX: number, worldZ: number): number {
    const x = worldX / this.settings.horizontalScale, z = worldZ / this.settings.horizontalScale;
    const wx = x + this.fbm(x / 4200, z / 4200, 11, 2) * 950;
    const wz = z + this.fbm(x / 4200, z / 4200, 29, 2) * 950;
    const continent = this.fbm(wx / 15000 + 5.7, wz / 15000 - 2.8, 43, 3);
    const land = smooth(-0.34, 0.02, continent);
    // Long, warped ridge belts, rather than isolated noise cones.
    const belt = smooth(0.08, 0.56, this.fbm(wx / 6500 + 9, wz / 6500 + 3, 61, 3) + 0.24);
    const ridge = 1 - Math.abs(this.fbm(wx / 1700 + 0.7, wz / 1700 + 4.9, 83, 5));
    const mountain = this.settings.mountainHeight * belt * ridge ** 3.4;
    const foothills = 120 + this.fbm(wx / 1600, wz / 1600, 109) * 190;
    const detail = this.fbm(x / 210, z / 210, 131, 4) * (5 + belt * 22);
    let elevation = mix(-190 + continent * 220, foothills + mountain + detail, land);
    // Analytic drainage corridor: a continuous meandering river with a broad floodplain.
    // This is a landform approximation, not hydraulic/tectonic simulation.
    const riverX = this.fbm(z / 3800 + 31.2, 7.4, 157, 3) * 1800
      + this.noise(z / 900, 2.8, 163) * 95;
    const distance = Math.abs(x - riverX);
    const valley = smooth(30, 1100, distance);
    const floodplain = 4 + smooth(45, 270, distance) * 24 + this.noise(x / 350, z / 350, 179) * 2;
    elevation = mix(Math.min(elevation, floodplain), elevation, valley);
    const channel = 1 - smooth(18, 48, distance);
    elevation = mix(elevation, Math.min(elevation, -7), channel);
    return elevation;
  }
  /** Linear RGB palette: meadow, exposed rock, sediment, and altitude-dependent snow. */
  color(x: number, z: number, height: number, slope: number): [number, number, number] {
    const moisture = this.fbm(x / 1700, z / 1700, 211, 2) * 0.5 + 0.5;
    const variation = this.noise(x / 85, z / 85, 223) * 0.025;
    let r = mix(0.19, 0.09, moisture) + variation;
    let g = mix(0.245, 0.19, moisture) + variation;
    let b = mix(0.085, 0.075, moisture) + variation;
    const alpine = smooth(650, 1250, height);
    r = mix(r, 0.25, alpine); g = mix(g, 0.27, alpine); b = mix(b, 0.19, alpine);
    const rock = smooth(0.35, 0.85, slope);
    r = mix(r, 0.29 + variation, rock); g = mix(g, 0.28 + variation, rock); b = mix(b, 0.255 + variation, rock);
    const shore = 1 - smooth(this.settings.seaLevel + 1, this.settings.seaLevel + 9, height);
    r = mix(r, 0.38, shore); g = mix(g, 0.33, shore); b = mix(b, 0.22, shore);
    const snow = smooth(1280, 1640, height + this.noise(x / 170, z / 170, 241) * 100) * (1 - smooth(0.65, 1.3, slope));
    return [mix(r, 0.83, snow), mix(g, 0.86, snow), mix(b, 0.88, snow)];
  }
}

export interface ChunkRequest {
  id: number; x: number; z: number; segments: number; settings: TerrainSettings;
  earthPaths?: EarthPath[]; revision?: number;
}
export interface ChunkData {
  id: number; x: number; z: number; segments: number;
  revision?: number;
  positions: Float32Array; normals: Float32Array; colors: Float32Array; indices: Uint32Array;
  trees?: Float32Array;
  preparedTrees?: import('./tree-data').PreparedTrees;
}
export function buildChunk(generator: TerrainGenerator, request: ChunkRequest): ChunkData {
  if (generator.earthworks?.affectsBounds(request.x * CHUNK_SIZE, request.z * CHUNK_SIZE, CHUNK_SIZE, (x, z) => generator.baseHeight(x, z))) return buildEarthworkChunk(generator, request);
  const { x, z, segments, id } = request;
  const width = segments + 1, surfaceCount = width * width;
  const vertexCount = surfaceCount + width * 4;
  const positions = new Float32Array(vertexCount * 3);
  const normals = new Float32Array(vertexCount * 3);
  const colors = new Float32Array(vertexCount * 3);
  const indices = new Uint32Array((segments * segments * 2 + segments * 8) * 3);
  const step = CHUNK_SIZE / segments;
  // One padded, globally aligned sample lattice for height and finite difference normals.
  const padWidth = width + 2;
  const heights = new Float64Array(padWidth * padWidth);
  for (let iz = -1; iz <= width; iz++) for (let ix = -1; ix <= width; ix++) {
    heights[(iz + 1) * padWidth + ix + 1] = generator.height(x * CHUNK_SIZE + ix * step, z * CHUNK_SIZE + iz * step);
  }
  for (let iz = 0; iz < width; iz++) for (let ix = 0; ix < width; ix++) {
    const vertex = iz * width + ix, offset = vertex * 3;
    const sample = (iz + 1) * padWidth + ix + 1;
    const h = heights[sample];
    const dx = (heights[sample + 1] - heights[sample - 1]) / (2 * step);
    const dz = (heights[sample + padWidth] - heights[sample - padWidth]) / (2 * step);
    const length = Math.hypot(dx, 1, dz);
    positions.set([ix * step, h, iz * step], offset);
    normals.set([-dx / length, 1 / length, -dz / length], offset);
    colors.set(generator.color(x * CHUNK_SIZE + ix * step, z * CHUNK_SIZE + iz * step, h, Math.hypot(dx, dz)), offset);
  }
  let cursor = 0;
  for (let iz = 0; iz < segments; iz++) for (let ix = 0; ix < segments; ix++) {
    const a = iz * width + ix, b = a + 1, c = a + width, d = c + 1;
    indices.set([a, c, b, b, c, d], cursor); cursor += 6;
  }
  // Deep skirts hide T-junctions between neighbouring LODs. Surface samples remain identical.
  const edges = [
    Array.from({ length: width }, (_, i) => i),
    Array.from({ length: width }, (_, i) => i * width + segments),
    Array.from({ length: width }, (_, i) => segments * width + segments - i),
    Array.from({ length: width }, (_, i) => (segments - i) * width),
  ];
  edges.forEach((edge, side) => {
    edge.forEach((source, i) => {
      const target = surfaceCount + side * width + i;
      positions.set(positions.subarray(source * 3, source * 3 + 3), target * 3);
      positions[target * 3 + 1] -= 180;
      normals.set(normals.subarray(source * 3, source * 3 + 3), target * 3);
      colors.set(colors.subarray(source * 3, source * 3 + 3), target * 3);
      if (i < segments) { indices.set([source, edge[i + 1], target, edge[i + 1], target + 1, target], cursor); cursor += 6; }
    });
  });
  return { id, x, z, segments, revision: request.revision, positions, normals, colors, indices };
}

/** Refine only the changed terrain, down to 1 m. Edge fans stitch unequal cells without holes. */
function buildEarthworkChunk(generator: TerrainGenerator, request: ChunkRequest): ChunkData {
  const ox = request.x * CHUNK_SIZE, oz = request.z * CHUNK_SIZE;
  const natural = (x: number, z: number) => generator.baseHeight(x, z);
  interface Cell { x: number; z: number; size: number }
  const leaves: Cell[] = [];
  const horizontal = new Map<number, Set<number>>(), vertical = new Map<number, Set<number>>();
  const addCorner = (x: number, z: number) => {
    if (!horizontal.has(z)) horizontal.set(z, new Set());
    if (!vertical.has(x)) vertical.set(x, new Set());
    horizontal.get(z)!.add(x); vertical.get(x)!.add(z);
  };
  const subdivide = (x: number, z: number, size: number) => {
    if (size > 1 && generator.earthworks!.affectsBounds(ox + x, oz + z, size, natural)) {
      const half = size / 2;
      subdivide(x, z, half); subdivide(x + half, z, half); subdivide(x, z + half, half); subdivide(x + half, z + half, half);
    } else {
      leaves.push({ x, z, size });
      addCorner(x, z); addCorner(x + size, z); addCorner(x, z + size); addCorner(x + size, z + size);
    }
  };
  const step = CHUNK_SIZE / request.segments;
  for (let z = 0; z < CHUNK_SIZE; z += step) for (let x = 0; x < CHUNK_SIZE; x += step) subdivide(x, z, step);
  const rows = new Map([...horizontal].map(([key, values]) => [key, [...values].sort((a, b) => a - b)]));
  const columns = new Map([...vertical].map(([key, values]) => [key, [...values].sort((a, b) => a - b)]));
  const positions: number[] = [], normals: number[] = [], colors: number[] = [], indices: number[] = [];
  const vertices = new Map<string, number>();
  const vertex = (x: number, z: number) => {
    const key = `${x},${z}`;
    const previous = vertices.get(key); if (previous !== undefined) return previous;
    const wx = ox + x, wz = oz + z, h = generator.height(wx, wz);
    const dx = (generator.height(wx + 1, wz) - generator.height(wx - 1, wz)) / 2;
    const dz = (generator.height(wx, wz + 1) - generator.height(wx, wz - 1)) / 2;
    const length = Math.hypot(dx, 1, dz);
    const index = positions.length / 3;
    positions.push(x, h, z); normals.push(-dx / length, 1 / length, -dz / length);
    colors.push(...(Math.abs(h - natural(wx, wz)) > 0.2 ? [0.26, 0.19, 0.12] : generator.color(wx, wz, h, Math.hypot(dx, dz))));
    vertices.set(key, index); return index;
  };
  const between = (values: number[], min: number, max: number) => values.filter(value => value >= min && value < max);
  for (const { x, z, size } of leaves) {
    // Clockwise in XZ gives upward-facing triangles.
    const boundary = [
      ...between(columns.get(x)!, z, z + size).map(v => vertex(x, v)),
      ...between(rows.get(z + size)!, x, x + size).map(v => vertex(v, z + size)),
      ...columns.get(x + size)!.filter(v => v > z && v <= z + size).reverse().map(v => vertex(x + size, v)),
      ...rows.get(z)!.filter(v => v > x && v <= x + size).reverse().map(v => vertex(v, z)),
    ];
    const center = vertex(x + size / 2, z + size / 2);
    for (let i = 0; i < boundary.length; i++) indices.push(center, boundary[i], boundary[(i + 1) % boundary.length]);
  }
  const edges = [rows.get(0)!.map(x => vertex(x, 0)), columns.get(CHUNK_SIZE)!.map(z => vertex(CHUNK_SIZE, z)), rows.get(CHUNK_SIZE)!.slice().reverse().map(x => vertex(x, CHUNK_SIZE)), columns.get(0)!.slice().reverse().map(z => vertex(0, z))];
  for (const edge of edges) {
    const bottom = edge.map(index => {
      const next = positions.length / 3;
      positions.push(positions[index * 3], positions[index * 3 + 1] - 180, positions[index * 3 + 2]);
      normals.push(...normals.slice(index * 3, index * 3 + 3)); colors.push(...colors.slice(index * 3, index * 3 + 3));
      return next;
    });
    for (let i = 0; i < edge.length - 1; i++) indices.push(edge[i], edge[i + 1], bottom[i], edge[i + 1], bottom[i + 1], bottom[i]);
  }
  return { id: request.id, x: request.x, z: request.z, segments: request.segments, revision: request.revision,
    positions: new Float32Array(positions), normals: new Float32Array(normals), colors: new Float32Array(colors), indices: new Uint32Array(indices) };
}
