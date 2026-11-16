import * as THREE from 'three';
import { buildTgv } from './locomotives/tgv';
import { buildGe809 } from './locomotives/ge809';
import { buildF7a } from './locomotives/f7a';
import { buildCoach } from './cars/coach';
import { buildPleasureDome } from './cars/pleasure-dome';
import { buildRioGrandeEngine } from './locomotives/rio-grande-consolidation';
import { buildRioGrandeTender } from './locomotives/rio-grande-tender';
import { buildRioGrandeBaggage } from './cars/rio-grande-baggage';
import { buildRioGrandeCoach } from './cars/rio-grande-coach';
import { createVehicleParts } from './model-parts';
import { makeBogie } from './bogies';
import { vehicleBogie } from './catalog';
import type { VehicleDefinition } from './types';
import type { VehicleModelParts } from './model-parts';
import type { VehicleMotion } from './motion';

const BODY_BUILDERS: Readonly<Record<string, (parts: VehicleModelParts, definition: VehicleDefinition, gauge: number) => void | VehicleMotion>> = {
  'tgv-sud-est-power': buildTgv,
  'tgv-sud-est-coach': buildTgv,
  'drgw-consolidation-engine': buildRioGrandeEngine,
  'drgw-consolidation-tender': buildRioGrandeTender,
  'drgw-wood-baggage': buildRioGrandeBaggage,
  'drgw-wood-coach': buildRioGrandeCoach,
  'atsf-f7a': buildF7a,
  'atsf-coach': buildCoach,
  'atsf-pleasure-dome': buildPleasureDome,
  'ge-809': parts => buildGe809(parts.body),
};

export interface VehicleVisual {
  group: THREE.Group; body: THREE.Group; bogies: [THREE.Group, THREE.Group]; definition: VehicleDefinition; gauge: number;
  sections?: VehicleVisual[];
  motion?: VehicleMotion;
}
/** Stable transform list for undo, including permanently coupled articulated sections. */
export function vehicleTransforms(visual: VehicleVisual): THREE.Group[] {
  return [visual.group, visual.body, ...visual.bogies, ...(visual.motion?.parts ?? []), ...(visual.sections?.flatMap(vehicleTransforms) ?? [])];
}
export function makeVehicle(definition: VehicleDefinition, gauge: number): VehicleVisual {
  const bogie = vehicleBogie(definition, gauge);
  if (definition.sections) {
    const group = new THREE.Group(), body = new THREE.Group();
    const bogies: [THREE.Group, THREE.Group] = [new THREE.Group(), new THREE.Group()];
    group.name = definition.id; group.add(body, ...bogies);
    const sections = definition.sections.map(section => {
      const visual = makeVehicle(section.definition, gauge);
      visual.group.position.z = section.offset; group.add(visual.group); return visual;
    });
    return { group, body, bogies, sections, definition, gauge };
  }
  const buildBody = BODY_BUILDERS[definition.id];
  if (!buildBody) throw new Error('Unknown vehicle model.');
  const group = new THREE.Group(), body = new THREE.Group();
  group.name = definition.id; body.name = 'body'; group.add(body);
  const parts = createVehicleParts(body), { box, materials: { black } } = parts;
  if (definition.runningGear !== 'steam') box(body, definition.width - 0.15, 0.32, definition.bodyLength - 0.25, 0, 1.16, 0, black);
  if (!definition.id.startsWith('drgw-')) box(body, 1.7, 0.55, definition.kind === 'locomotive' ? 4.6 : 7.5, 0, 0.75, 0, black);
  for (const end of [-1, 1]) {
    box(body, 0.18, 0.2, 0.6, 0, 0.88, end * (definition.length / 2 - 0.4), black);
    box(body, 0.35, 0.26, 0.22, 0, 0.88, end * (definition.length / 2 - 0.11), black);
  }
  const motion = buildBody(parts, definition, gauge) || undefined;
  const bogies: [THREE.Group, THREE.Group] = definition.runningGear === 'steam'
    ? [new THREE.Group(), new THREE.Group()] : [makeBogie(bogie, parts), makeBogie(bogie, parts)];
  bogies[0].position.z = definition.bogieCenters / 2; bogies[1].position.z = -definition.bogieCenters / 2;
  group.add(...bogies);
  // Materials created for optional details but not used must not retain GPU resources.
  const used = new Set<THREE.Material>(); group.traverse(object => { if (object instanceof THREE.Mesh) used.add(object.material as THREE.Material); });
  for (const mat of Object.values(parts.materials)) if (!used.has(mat)) mat.dispose();
  return { group, body, bogies, definition, gauge, motion };
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
