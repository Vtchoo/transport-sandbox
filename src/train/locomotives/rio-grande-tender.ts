import * as THREE from "three";
import type { VehicleDefinition } from "../types";
import type { VehicleModelParts } from "../model-parts";

/** Internal articulated section; purchased and removed with the locomotive. */
export const RIO_GRANDE_TENDER: VehicleDefinition = {
  id: "drgw-consolidation-tender",
  name: "Rio Grande · Coal tender",
  kind: "coach",
  length: 6.8,
  bodyLength: 6.1,
  width: 2.7,
  height: 2.85,
  bogieCenters: 4.1,
  bogiesByGauge: { 914: "archbar-914", 1435: "archbar-1435" },
  physics: { massKg: 32659, powerW: 0, tractiveEffortN: 0, maxSpeedMps: 12.5 },
};
export function buildRioGrandeTender(
  parts: VehicleModelParts,
  definition: VehicleDefinition,
) {
  const {
    body,
    box,
    cylinder,
    mesh,
    label,
    materials: { yellow, black },
  } = parts;
  const w = definition.width / 2;
  box(body, definition.width, 1.23, 5.9, 0, 1.82, -0.05, yellow);
  box(body, definition.width + 0.03, 0.065, 6.0, 0, 2.47, -0.05, black);
  for (const side of [-1, 1]) {
    box(body, 0.09, 0.38, 3.8, side * (w - 0.04), 2.63, 0.95, yellow);
    for (let z = -2.8; z < 2.9; z += 0.23) {
      const rivet = mesh(
        body,
        new THREE.SphereGeometry(0.026, 4, 3),
        black,
        side * (w + 0.01),
        1.3,
        z,
      );
      rivet.scale.x = 0.5;
    }
    label(
      "Rio Grande",
      3.8,
      0.54,
      side * (w + 0.035),
      1.99,
      -0.2,
      (side * Math.PI) / 2,
      "#25241b",
      true,
    );
  }
  box(body, definition.width - 0.12, 0.38, 0.1, 0, 2.63, 2.83, yellow);
  const coal = new THREE.MeshStandardMaterial({
    color: 0x252724,
    roughness: 1,
  });
  box(body, definition.width - 0.2, 0.1, 3.7, 0, 2.58, 0.95, coal);
  for (let i = 0; i < 50; i++) {
    const x = Math.sin(i * 17.3) * (w - 0.25),
      z = -0.62 + ((i * 0.61803398875) % 1) * 3.1;
    const lump = mesh(
      body,
      new THREE.IcosahedronGeometry(0.15 + (i % 3) * 0.035, 0),
      coal,
      x,
      2.65 + 0.05 * Math.cos(i),
      z,
    );
    lump.rotation.set(i, i * 2, i * 0.3);
    lump.scale.y = 0.6;
  }
  cylinder(body, 0.31, 0.13, 0, 2.56, -2.1, black);
  for (const x of [-0.95, 0.95]) {
    box(body, 0.035, 1.25, 0.035, x, 1.87, -3.06, black);
    for (let i = 0; i < 5; i++)
      box(body, 0.3, 0.025, 0.035, x, 1.34 + i * 0.25, -3.08, black);
  }
}
