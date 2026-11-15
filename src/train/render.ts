import * as THREE from 'three';
import { buildGe809 } from './locomotives/ge809';
import { buildF7a } from './locomotives/f7a';
import { buildCoach } from './cars/coach';
import { buildPleasureDome } from './cars/pleasure-dome';
import { createVehicleParts } from './model-parts';
import { makeBogie } from './bogies';
import { vehicleBogie } from './catalog';
import type { VehicleDefinition } from './types';
import type { VehicleModelParts } from './model-parts';

const BODY_BUILDERS: Readonly<Record<string, (parts: VehicleModelParts, definition: VehicleDefinition) => void>> = {
  'atsf-f7a': buildF7a,
  'atsf-coach': buildCoach,
  'atsf-pleasure-dome': buildPleasureDome,
  'ge-809': parts => buildGe809(parts.body),
};

export interface VehicleVisual {
  group: THREE.Group; body: THREE.Group; bogies: [THREE.Group, THREE.Group]; definition: VehicleDefinition; gauge: number;
}
export function makeVehicle(definition: VehicleDefinition, gauge: number): VehicleVisual {
  const bogie = vehicleBogie(definition, gauge);
  const buildBody = BODY_BUILDERS[definition.id];
  if (!buildBody) throw new Error('Unknown vehicle model.');
  const group = new THREE.Group(), body = new THREE.Group();
  group.name = definition.id; body.name = 'body'; group.add(body);
  const parts = createVehicleParts(body), { box, materials: { black } } = parts;
  box(body, definition.width - 0.15, 0.32, definition.bodyLength - 0.25, 0, 1.16, 0, black);
  box(body, 1.7, 0.55, definition.kind === 'locomotive' ? 4.6 : 7.5, 0, 0.75, 0, black);
  for (const end of [-1, 1]) {
    box(body, 0.18, 0.2, 0.6, 0, 0.88, end * (definition.length / 2 - 0.4), black);
    box(body, 0.35, 0.26, 0.22, 0, 0.88, end * (definition.length / 2 - 0.11), black);
  }
  buildBody(parts, definition);
  const bogies: [THREE.Group, THREE.Group] = [makeBogie(bogie, parts), makeBogie(bogie, parts)];
  bogies[0].position.z = definition.bogieCenters / 2; bogies[1].position.z = -definition.bogieCenters / 2;
  group.add(...bogies);
  // Materials created for optional details but not used must not retain GPU resources.
  const used = new Set<THREE.Material>(); group.traverse(object => { if (object instanceof THREE.Mesh) used.add(object.material as THREE.Material); });
  for (const mat of Object.values(parts.materials)) if (!used.has(mat)) mat.dispose();
  return { group, body, bogies, definition, gauge };
}

export function tintVehicle(visual: VehicleVisual, valid: boolean) {
  visual.group.traverse(object => {
    if (!(object instanceof THREE.Mesh)) return;
    const material = object.material as THREE.MeshStandardMaterial;
    if (!material.transparent || material.opacity > 0.42) {
      material.transparent = true; material.opacity = 0.42; material.depthWrite = false; material.needsUpdate = true;
    }
    if (material.emissive) { material.emissive.set(valid ? 0x143d23 : 0x861817); material.emissiveIntensity = valid ? 0.22 : 0.65; }
  });
}

export function disposeVehicle(visual: VehicleVisual) {
  const geometries = new Set<THREE.BufferGeometry>(), materials = new Set<THREE.Material>(), textures = new Set<THREE.Texture>();
  visual.group.traverse(object => {
    if (!(object instanceof THREE.Mesh)) return;
    geometries.add(object.geometry);
    for (const material of Array.isArray(object.material) ? object.material : [object.material]) {
      materials.add(material); const texture = (material as THREE.MeshBasicMaterial).map; if (texture) textures.add(texture);
    }
  });
  geometries.forEach(item => item.dispose()); materials.forEach(item => item.dispose()); textures.forEach(item => item.dispose()); visual.group.clear();
}
