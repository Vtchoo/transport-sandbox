/** Shared by platform meshes and parallel-track clearance calculations (metres). */
export const PLATFORM_WIDTH = 3;
export function platformEdgeOffset(gauge: number) {
  return Math.max(1.75, gauge / 2000 + 1.05);
}
