import * as THREE from 'three';
import type { VehicleDefinition } from '../types';
import type { VehicleModelParts } from '../model-parts';
import { buildPassengerBody } from './passenger-body';

export const PLEASURE_DOME: VehicleDefinition = {
  id: 'atsf-pleasure-dome',
  name: 'Santa Fe · Pleasure Dome lounge',
  kind: 'dome',
  length: 25.7048, bodyLength: 24.4,
  width: 3.048, height: 4.72,
  bogieCenters: 18.288,
  bogiesByGauge: { 1435: 'passenger-2axle-1435' },
  physics: { massKg: 75000, powerW: 0, tractiveEffortN: 0, maxSpeedMps: 45.599 },
};

export function buildPleasureDome(parts: VehicleModelParts, definition: VehicleDefinition) {
  buildPassengerBody(parts, definition, 'PLEASURE DOME · 500');
  const { body, box, mesh, shell, materials: { silver, glass } } = parts;
  shell([{ z: -4.15, w: 1.18, bottom: 3.8, shoulder: 4.1, top: 4.35 }, { z: -3.45, w: 1.3, bottom: 3.8, shoulder: 4.48, top: 4.72 }, { z: 3.45, w: 1.3, bottom: 3.8, shoulder: 4.48, top: 4.72 }, { z: 4.15, w: 1.18, bottom: 3.8, shoulder: 4.1, top: 4.35 }], glass);
  for (const side of [-1, 1]) {
    box(body, 0.08, 0.09, 8.1, side * 1.29, 3.96, 0, silver);
    box(body, 0.08, 0.06, 6.8, side * 1.3, 4.45, 0, silver);
    for (let i = 0; i <= 8; i++) box(body, 0.08, 0.6, 0.055, side * 1.31, 4.22, -3.4 + i * 0.85, silver);
  }
  box(body, 0.08, 0.05, 6.8, 0, 4.69, 0, silver);
  for (let i = 0; i <= 8; i++) {
    const arch = new THREE.CatmullRomCurve3([new THREE.Vector3(-1.3, 4.46, -3.4 + i * 0.85), new THREE.Vector3(0, 4.69, -3.4 + i * 0.85), new THREE.Vector3(1.3, 4.46, -3.4 + i * 0.85)]);
    mesh(body, new THREE.TubeGeometry(arch, 8, 0.027, 4, false), silver, 0, 0, 0);
  }
}
