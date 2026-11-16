import * as THREE from 'three';
import type { BogieDefinition } from './types';
import type { VehicleModelParts } from './model-parts';

export function makeBogie(spec: BogieDefinition, parts: VehicleModelParts) {
  const { box, cylinder, materials: { black, roof } } = parts;
  const bogie = new THREE.Group(); bogie.name = spec.id; bogie.userData.bogieId = spec.id; bogie.userData.gauge = spec.gauge;
  const tread = 0.12, wheelX = spec.gauge / 2000 + tread / 2;
  for (const z of [-spec.wheelbase / 2, spec.wheelbase / 2]) {
    const axle = cylinder(bogie, 0.08, wheelX * 2 + 0.15, 0, spec.wheelRadius, z, black); axle.rotation.z = Math.PI / 2;
    for (const side of [-1, 1]) {
      const wheel = cylinder(bogie, spec.wheelRadius, tread, side * wheelX, spec.wheelRadius, z, black); wheel.rotation.z = Math.PI / 2;
      wheel.name = 'wheel'; wheel.userData.treadCenter = side * wheelX;
      const flange = cylinder(bogie, spec.wheelRadius + 0.025, 0.025, side * (wheelX - tread / 2 - 0.012), spec.wheelRadius, z, roof); flange.rotation.z = Math.PI / 2;
      const bearing = cylinder(bogie, 0.16, 0.18, side * (spec.frameWidth / 2), spec.wheelRadius, z, roof); bearing.rotation.z = Math.PI / 2;
    }
    if (spec.powered) box(bogie, 1.05, 0.35, 0.5, 0, 0.52, z - 0.25, black);
  }
  box(bogie, spec.frameWidth, 0.22, 0.4, 0, 0.85, 0, black);
  for (const side of [-1, 1]) {
    if (spec.frameStyle === 'archbar') {
      const x = side * spec.frameWidth / 2;
      box(bogie, .11, .09, spec.wheelbase + .5, x, .72, 0, black);
      box(bogie, .11, .09, spec.wheelbase + .5, x, .39, 0, black);
      for (const end of [-1, 1]) {
        box(bogie, .22, .22, .3, x, .43, end * spec.wheelbase / 2, black);
        const brace = box(bogie, .1, .065, .95, x, .55, end * .46, roof);
        brace.rotation.x = end * .32;
      }
      for (const z of [-.18, .18]) for (let coil = 0; coil < 4; coil++) cylinder(bogie, .09, .025, x, .49 + coil * .055, z, black);
      continue;
    }
    if (spec.frameStyle === 'fb2') {
      // FB-2: deep angular casting, outboard axle boxes and twin spring packs.
      box(bogie, 0.2, 0.37, spec.wheelbase + 0.6, side * spec.frameWidth / 2, 0.78, 0, roof);
      for (const z of [-spec.wheelbase / 2, spec.wheelbase / 2]) {
        box(bogie, 0.27, 0.3, 0.43, side * spec.frameWidth / 2, 0.51, z, black);
        box(bogie, 0.19, 0.19, 0.34, side * (spec.frameWidth / 2 + 0.08), 0.53, z, roof);
      }
      for (const z of [-0.58, 0.58]) for (let coil = 0; coil < 5; coil++) {
        cylinder(bogie, 0.15, 0.028, side * (spec.frameWidth / 2 + 0.05), 0.57 + coil * 0.07, z, black);
      }
      box(bogie, 0.14, 0.12, 2.2, side * (spec.frameWidth / 2 + 0.09), 0.34, 0, black);
      continue;
    }
    box(bogie, 0.13, 0.22, spec.wheelbase + 0.5, side * spec.frameWidth / 2, 0.69, 0, roof);
    for (const z of [-0.42, 0.42]) {
      cylinder(bogie, 0.1, 0.3, side * (spec.frameWidth / 2), 0.79, z, black);
      for (let coil = 0; coil < 4; coil++) cylinder(bogie, 0.12, 0.023, side * (spec.frameWidth / 2), 0.67 + coil * 0.07, z, roof);
    }
    if (spec.powered) box(bogie, 0.13, 0.12, 1.8, side * (spec.frameWidth / 2 + 0.08), 0.42, 0, black);
  }
  return bogie;
}
