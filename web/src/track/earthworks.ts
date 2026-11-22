export interface EarthPoint { x: number; y: number; z: number; nx?: number; nz?: number; left?: number; right?: number }
export interface EarthPath { points: EarthPoint[] }
/** Stop the side slopes at their first meeting with natural ground, not in a valley beyond it. */
export function prepareEarthPaths(paths: EarthPath[], natural: (x: number, z: number) => number, mountainHeight: number): EarthPath[] {
  return paths.map(path => ({ points: path.points.map((point, index) => {
    const before = path.points[Math.max(0, index - 1)], after = path.points[Math.min(path.points.length - 1, index + 1)];
    const length = Math.hypot(after.x - before.x, after.z - before.z) || 1;
    const nx = -(after.z - before.z) / length, nz = (after.x - before.x) / length;
    const bed = point.y - 0.65;
    const width = (side: number) => {
      const at = (distance: number) => natural(point.x + nx * distance * side, point.z + nz * distance * side);
      const fill = bed >= at(2);
      const inside = (distance: number) => fill ? at(distance) < bed - (distance - 2) / 2 : at(distance) > bed + (distance - 2) / 2;
      const limit = 2 + 2 * Math.max(Math.abs(bed + 700), Math.abs(bed - mountainHeight - 600));
      let previous = 2;
      for (let distance = 3; distance <= limit + 20; distance += Math.min(8, Math.max(1, distance * 0.02))) {
        if (!inside(distance)) {
          let low = previous, high = distance;
          for (let i = 0; i < 12; i++) { const mid = (low + high) / 2; if (inside(mid)) low = mid; else high = mid; }
          return (low + high) / 2;
        }
        previous = distance;
      }
      return limit;
    };
    return { ...point, nx, nz, left: width(-1), right: width(1) };
  }) }));
}
interface Edge { a: EarthPoint; b: EarthPoint; minX: number; maxX: number; minZ: number; maxZ: number }
interface Branch { minX: number; maxX: number; minZ: number; maxZ: number; left?: Branch; right?: Branch; edges?: Edge[] }
const boxDistance = (x: number, z: number, box: Branch) => Math.max(box.minX - x, 0, x - box.maxX) ** 2 + Math.max(box.minZ - z, 0, z - box.maxZ) ** 2;
/** Shared by the main thread and workers. A 2D BVH keeps nearest-spline queries local. */
export class EarthworkField {
  private root?: Branch;
  constructor(readonly paths: EarthPath[]) {
    const edges: Edge[] = [];
    for (const path of paths) for (let i = 1; i < path.points.length; i++) {
      const a = path.points[i - 1], b = path.points[i];
      edges.push({ a, b, minX: Math.min(a.x, b.x), maxX: Math.max(a.x, b.x), minZ: Math.min(a.z, b.z), maxZ: Math.max(a.z, b.z) });
    }
    const build = (items: Edge[]): Branch => {
      const bounds: Branch = { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity };
      for (const item of items) { bounds.minX = Math.min(bounds.minX, item.minX); bounds.maxX = Math.max(bounds.maxX, item.maxX); bounds.minZ = Math.min(bounds.minZ, item.minZ); bounds.maxZ = Math.max(bounds.maxZ, item.maxZ); }
      if (items.length <= 8) bounds.edges = items;
      else {
        const axis = bounds.maxX - bounds.minX >= bounds.maxZ - bounds.minZ ? 'x' : 'z';
        items.sort((a, b) => a.a[axis] + a.b[axis] - b.a[axis] - b.b[axis]);
        const half = Math.floor(items.length / 2);
        bounds.left = build(items.slice(0, half)); bounds.right = build(items.slice(half));
      }
      return bounds;
    };
    if (edges.length) this.root = build(edges);
  }
  nearest(x: number, z: number) {
    let distanceSq = Infinity, railHeight = 0, width = Infinity;
    const visit = (branch: Branch) => {
      if (boxDistance(x, z, branch) > distanceSq) return;
      if (branch.edges) for (const edge of branch.edges) {
        const dx = edge.b.x - edge.a.x, dz = edge.b.z - edge.a.z;
        const denominator = dx * dx + dz * dz;
        const t = denominator > 1e-12 ? Math.max(0, Math.min(1, ((x - edge.a.x) * dx + (z - edge.a.z) * dz) / denominator)) : 0;
        const d = (x - edge.a.x - t * dx) ** 2 + (z - edge.a.z - t * dz) ** 2;
        if (d < distanceSq) {
          distanceSq = d; railHeight = edge.a.y + (edge.b.y - edge.a.y) * t;
          const nx = (edge.a.nx ?? 0) + ((edge.b.nx ?? 0) - (edge.a.nx ?? 0)) * t;
          const nz = (edge.a.nz ?? 1) + ((edge.b.nz ?? 1) - (edge.a.nz ?? 1)) * t;
          const side = (x - edge.a.x - t * dx) * nx + (z - edge.a.z - t * dz) * nz;
          const aWidth = (side >= 0 ? edge.a.right : edge.a.left) ?? Infinity;
          const bWidth = (side >= 0 ? edge.b.right : edge.b.left) ?? Infinity;
          width = Number.isFinite(aWidth) && Number.isFinite(bWidth) ? aWidth + (bWidth - aWidth) * t : Infinity;
        }
      }
      else if (branch.left && branch.right) {
        const leftFirst = boxDistance(x, z, branch.left) < boxDistance(x, z, branch.right);
        visit(leftFirst ? branch.left : branch.right); visit(leftFirst ? branch.right : branch.left);
      }
    };
    if (this.root) visit(this.root);
    return { distance: Math.sqrt(distanceSq), railHeight, width };
  }
  height(x: number, z: number, natural: number) {
    const nearest = this.nearest(x, z);
    if (!Number.isFinite(nearest.distance) || nearest.distance > nearest.width) return natural;
    const bed = nearest.railHeight - 0.65;
    const rise = Math.max(0, nearest.distance - 2) / 2;
    return Math.max(bed - rise, Math.min(natural, bed + rise));
  }
  affectsBounds(x: number, z: number, size: number, natural: (x: number, z: number) => number) {
    if (!this.root) return false;
    if (this.nearest(x + size / 2, z + size / 2).distance <= size * Math.SQRT1_2 + 3) return true;
    for (const [dx, dz] of [[0, 0], [size, 0], [0, size], [size, size], [size / 2, size / 2]]) {
      const base = natural(x + dx, z + dz);
      if (Math.abs(this.height(x + dx, z + dz, base) - base) > 0.02) return true;
    }
    return false;
  }
}
