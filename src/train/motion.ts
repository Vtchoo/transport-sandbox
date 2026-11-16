import type * as THREE from 'three';
import type { VehicleVisual } from './render';

export interface VehicleMotion {
  /** Independently transformed components, also used by instancing and undo. */
  parts: THREE.Group[];
  advance(distance: number): void;
}

/** Signed metres along the vehicle's own forward axis, after movement is accepted. */
export function advanceVehicle(visual: VehicleVisual, distance: number) {
  if (!Number.isFinite(distance)) return;
  visual.motion?.advance(distance);
  visual.sections?.forEach(section => advanceVehicle(section, distance));
}
