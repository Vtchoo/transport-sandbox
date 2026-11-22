import * as THREE from 'three';
import type { VehicleDefinition } from '../types';
import type { VehicleModelParts } from '../model-parts';

const thumbnail = (power: boolean) => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 184 72"><path d="${power ? 'M8 51 30 24H174V51Z' : 'M8 51V25H176V51Z'}" fill="#ef6b22"/><path d="M32 25H174V32H32Z" fill="#93989b"/><path d="M30 34H174V42H23Z" fill="#303a43"/><path d="M12 48H174" stroke="#ddd7ca" stroke-width="3"/><g fill="#22313a"><circle cx="41" cy="56" r="6"/><circle cx="56" cy="56" r="6"/><circle cx="141" cy="56" r="6"/><circle cx="156" cy="56" r="6"/></g></svg>`;

export const TGV_POWER: VehicleDefinition = {
  id: 'tgv-sud-est-power', name: 'TGV Sud-Est power car', kind: 'locomotive', thumbnailSvg: thumbnail(true),
  length: 19, bodyLength: 18.65, width: 2.81, height: 4.1, bogieCenters: 14,
  bogiesByGauge: { 1435: 'tgv-powered-1435' },
  // Half the trainset rating; trailer traction is folded into the power cars.
  physics: { massKg: 68000, powerW: 3225000, tractiveEffortN: 110000, maxSpeedMps: 75 },
};
export const TGV_COACH: VehicleDefinition = {
  id: 'tgv-sud-est-coach', name: 'TGV Sud-Est passenger coach', kind: 'coach', thumbnailSvg: thumbnail(false),
  length: 20.275, bodyLength: 19.975, width: 2.904, height: 3.42, bogieCenters: 16.2,
  bogiesByGauge: { 1435: 'tgv-trailer-1435' },
  physics: { massKg: 31125, powerW: 0, tractiveEffortN: 0, maxSpeedMps: 75 },
};

/** Original orange Sud-Est livery. Dimensions and details are procedural approximations. */
export function buildTgv(parts: VehicleModelParts, definition: VehicleDefinition) {
  const { body, box, shell, label, mesh, materials: { black, glass, silver } } = parts;
  const orange = new THREE.MeshStandardMaterial({ color: 0xef6824, metalness: .25, roughness: .4 });
  const gray = new THREE.MeshStandardMaterial({ color: 0x707980, metalness: .45, roughness: .45 });
  const cream = new THREE.MeshStandardMaterial({ color: 0xe6dfd0, roughness: .5 });
  const power = definition.kind === 'locomotive', half = definition.bodyLength / 2, w = definition.width / 2;
  const roof = power ? 3.78 : 3.42, shoulder = power ? 3.3 : 3.05;
  const end = power ? 4.6 : half;
  shell([{ z: -half, w, bottom: 1.25, shoulder, top: roof }, { z: end, w, bottom: 1.25, shoulder, top: roof }], orange);
  box(body, w * 1.7, .12, end + half, 0, roof - .04, (end - half) / 2, gray);
  if (power) {
    shell([
      { z: end, w, bottom: 1.25, shoulder, top: roof },
      { z: 5.8, w: w * .96, bottom: 1.25, shoulder: 3.12, top: 3.5 },
      { z: 7.4, w: w * .83, bottom: 1.25, shoulder: 2.25, top: 2.6 },
      { z: half, w: w * .6, bottom: 1.25, shoulder: 1.8, top: 2.02 },
    ], orange);
    for (const side of [-1, 1]) {
      const pane = box(body, .92, .62, .065, side * .51, 3.03, 6.36, glass);
      pane.rotation.x = -.52; pane.name = 'windshield';
      box(body, .035, .48, .8, side * (w + .012), 3.05, 4.45, glass);
      for (let i = 0; i < 25; i++) box(body, .035, .72, .065, side * (w + .018), 2.65, -6.8 + i * .29, gray);
      box(body, .035, 1.58, .75, side * (w + .02), 2.13, 2.8, gray);
      box(body, .04, .47, .55, side * (w + .045), 2.72, 2.8, glass);
      const lamp = box(body, .35, .18, .08, side * .57, 1.84, half + .01, cream); lamp.name = 'headlight';
      label('TGV', 1.9, .5, side * (w + .045), 1.91, -.7, side * Math.PI / 2, '#f2e9d9', true);
    }
    // Folded diamond pantograph, deliberately kept inside the catalog height.
    box(body, 1.25, .1, 2.1, 0, 3.82, -4.2, black);
    for (const x of [-.42, .42]) {
      const curve = new THREE.CatmullRomCurve3([
        new THREE.Vector3(x, 3.86, -5.05), new THREE.Vector3(x, 4.02, -4.2), new THREE.Vector3(x, 3.92, -3.35),
      ]);
      mesh(body, new THREE.TubeGeometry(curve, 4, .025, 5, false), black, 0, 0, 0);
    }
    box(body, 1.6, .08, .12, 0, 4.06, -4.2, silver).name = 'pantograph';
  }
  for (const side of [-1, 1]) {
    const span = end + half, center = (end - half) / 2;
    box(body, .025, .57, span, side * (w + .015), 2.65, center, black);
    box(body, .035, .1, span, side * (w + .025), 1.62, center, cream);
    if (!power) {
      for (let i = 0; i < 13; i++) box(body, .04, .43, .88, side * (w + .035), 2.65, -7.5 + i * 1.25, glass);
      for (const z of [-8.8, 8.8]) {
        box(body, .04, 1.75, .8, side * (w + .035), 2.2, z, gray);
        box(body, .045, .48, .48, side * (w + .04), 2.69, z, glass);
      }
      label('SNCF', 1.25, .28, side * (w + .045), 1.94, -5.7, side * Math.PI / 2, '#f5ead8');
    }
  }
  box(body, 1.8, 1.6, .12, 0, 2.13, -half - .02, black).name = 'gangway';
  if (!power) box(body, 1.8, 1.6, .12, 0, 2.13, half + .02, black).name = 'gangway';
}
