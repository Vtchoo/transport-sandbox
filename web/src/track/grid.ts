import * as THREE from 'three';

export const GRID_SPACINGS = [0, 1, 5, 10, 25];

/** Coordinates are absolute world metres, independent of the rendering origin. */
export function snapWorldPoint(point: THREE.Vector3, spacing: number) {
  if (!GRID_SPACINGS.includes(spacing) || spacing === 0) return point.clone();
  return new THREE.Vector3(Math.round(point.x / spacing) * spacing, point.y, Math.round(point.z / spacing) * spacing);
}

export class WorldGrid extends THREE.Group {
  spacing = 0;
  private helper?: THREE.GridHelper;
  setSpacing(spacing: number) {
    if (!GRID_SPACINGS.includes(spacing)) return;
    if (spacing === this.spacing) return;
    this.dispose(); this.spacing = spacing;
    if (!spacing) return;
    this.helper = new THREE.GridHelper(spacing * 80, 80, 0xb6e0c4, 0x8ab7a0);
    for (const material of Array.isArray(this.helper.material) ? this.helper.material : [this.helper.material]) {
      material.transparent = true; material.opacity = 0.24; material.depthTest = false; material.depthWrite = false;
    }
    this.helper.renderOrder = 10; this.add(this.helper);
  }
  snap(point: THREE.Vector3) { return snapWorldPoint(point, this.spacing); }
  showAt(point: THREE.Vector3 | undefined, enabled: boolean) {
    this.visible = enabled && this.spacing > 0 && !!point;
    if (!this.visible || !point) return;
    const snapped = this.snap(point);
    this.position.set(snapped.x, point.y, snapped.z);
  }
  dispose() {
    if (this.helper) {
      this.helper.geometry.dispose();
      for (const material of Array.isArray(this.helper.material) ? this.helper.material : [this.helper.material]) material.dispose();
      this.remove(this.helper); this.helper = undefined;
    }
    this.visible = false;
  }
}
