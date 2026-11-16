import * as THREE from "three";
import type { VehicleDefinition } from "../types";
import type { VehicleModelParts } from "../model-parts";
import { RIO_GRANDE_TENDER } from "./rio-grande-tender";
import { buildSteamGear } from "./steam-gear";

export const RIO_GRANDE_ENGINE: VehicleDefinition = {
  id: "drgw-consolidation-engine",
  name: "Rio Grande · 2-8-0 engine",
  kind: "locomotive",
  length: 11.3,
  bodyLength: 10.7,
  width: 2.75,
  height: 3.95,
  bogieCenters: 5.1,
  runningGear: "steam",
  bogiesByGauge: { 914: "steam-drivers-914", 1435: "steam-drivers-1435" },
  exhaust: { position: [0, 3.97, 3.65] },
  physics: {
    massKg: 60341,
    powerW: 700000,
    tractiveEffortN: 142608,
    maxSpeedMps: 12.5,
  },
};
/** One train-builder item; engine and tender follow the track independently. */
export const RIO_GRANDE_CONSOLIDATION: VehicleDefinition = {
  ...RIO_GRANDE_ENGINE,
  id: "drgw-consolidation",
  name: "Rio Grande · 2-8-0 + tender",
  length: 18.3,
  bodyLength: 17.6,
  bogieCenters: 14,
  sections: [
    { definition: RIO_GRANDE_ENGINE, offset: 3.5 },
    { definition: RIO_GRANDE_TENDER, offset: -5.75 },
  ],
  physics: { ...RIO_GRANDE_ENGINE.physics, massKg: 93200 },
};

export function buildRioGrandeEngine(
  parts: VehicleModelParts,
  definition: VehicleDefinition,
  gauge: number,
) {
  const {
    body,
    mesh,
    box,
    cylinder,
    label,
    shell,
    materials: { black, yellow, glass, silver },
  } = parts;
  const boiler = new THREE.MeshStandardMaterial({
    color: 0x30342e,
    metalness: 0.45,
    roughness: 0.65,
  });
  const smokebox = new THREE.MeshStandardMaterial({
    color: 0x767d78,
    metalness: 0.6,
    roughness: 0.65,
  });
  const brass = new THREE.MeshStandardMaterial({
    color: 0xb7a15b,
    metalness: 0.7,
    roughness: 0.4,
  });
  const pipe = (
    radius: number,
    length: number,
    x: number,
    y: number,
    z: number,
    mat: THREE.MeshStandardMaterial = boiler,
  ) => {
    const item = cylinder(body, radius, length, x, y, z, mat);
    item.rotation.x = Math.PI / 2;
    return item;
  };
  // Boiler, separate graphite smokebox and front door.
  pipe(0.82, 5.85, 0, 2.37, 0.15).name = "boiler";
  pipe(0.84, 1.65, 0, 2.37, 3.77, smokebox).name = "smokebox";
  pipe(0.71, 0.075, 0, 2.37, 4.62, black);
  pipe(0.62, 0.09, 0, 2.37, 4.67, smokebox);
  for (let i = 0; i < 12; i++)
    pipe(
      0.035,
      0.12,
      Math.sin((i * Math.PI) / 6) * 0.65,
      2.37 + Math.cos((i * Math.PI) / 6) * 0.65,
      4.74,
      black,
    );
  pipe(0.13, 0.07, 0, 2.37, 4.76, brass);
  label("265", 0.22, 0.14, 0, 2.37, 4.81, 0, "#181c18");
  for (const z of [-2.25, -0.9, 0.5, 1.9, 2.9]) {
    const band = mesh(
      body,
      new THREE.TorusGeometry(0.827, 0.025, 5, 32),
      black,
      0,
      2.37,
      z,
    );
    band.name = "boiler-band";
  }
  // Two domes, brass bell and straight stack with a flared cap.
  for (const [z, r] of [
    [-0.55, 0.35],
    [1.4, 0.3],
  ]) {
    cylinder(body, r + 0.11, 0.12, 0, 3.14, z, boiler);
    cylinder(body, r, 0.46, 0, 3.37, z, boiler);
    mesh(
      body,
      new THREE.SphereGeometry(r, 16, 8, 0, Math.PI * 2, 0, Math.PI / 2),
      boiler,
      0,
      3.59,
      z,
    );
  }
  cylinder(body, 0.27, 0.73, 0, 3.52, 3.65, black);
  mesh(
    body,
    new THREE.CylinderGeometry(0.35, 0.27, 0.13, 20),
    black,
    0,
    3.885,
    3.65,
  ).name = "chimney";
  cylinder(body, 0.35, 0.06, 0, 3.93, 3.65, black);
  mesh(
    body,
    new THREE.CylinderGeometry(0.1, 0.21, 0.23, 14),
    brass,
    0,
    3.35,
    2.35,
  );
  for (const x of [-0.24, 0.24])
    box(body, 0.035, 0.37, 0.035, x, 3.34, 2.35, black);
  box(body, 0.52, 0.035, 0.035, 0, 3.52, 2.35, black);
  // Yellow open-back cab, with actual openings rather than windows pasted over a solid block.
  box(body, 2.7, 0.12, 2.45, 0, 1.38, -3.72, black);
  box(body, 2.72, 1.4, 0.16, 0, 2.16, -2.62, yellow);
  for (const side of [-1, 1]) {
    box(body, 0.12, 0.75, 2.32, side * 1.3, 1.81, -3.72, yellow);
    box(body, 0.12, 0.22, 2.32, side * 1.3, 3.16, -3.72, yellow);
    for (const z of [-4.82, -3.62, -2.62])
      box(body, 0.14, 0.85, 0.1, side * 1.3, 2.62, z, yellow);
    for (const z of [-4.2, -3.13])
      box(body, 0.035, 0.64, 0.85, side * 1.31, 2.63, z, glass);
    label(
      "D&RGW",
      1.5,
      0.24,
      side * 1.37,
      1.87,
      -3.72,
      (side * Math.PI) / 2,
      "#22271e",
    );
    label(
      "265",
      0.64,
      0.21,
      side * 1.37,
      2.16,
      -3.72,
      (side * Math.PI) / 2,
      "#22271e",
    );
    box(body, 0.65, 0.1, 7.3, side * 1.04, 1.63, 0.3, black);
    pipe(0.028, 6.4, side * 0.91, 2.73, 0.8, silver);
    for (const z of [-1.9, 0.3, 2.5, 3.8])
      box(body, 0.23, 0.035, 0.035, side * 0.8, 2.73, z, silver);
    pipe(0.1, 2.8, side * 0.96, 1.45, 0.3, black);
    pipe(0.28, 0.85, side * (gauge / 2000 + 0.29), 0.85, 3.08, smokebox);
    box(body, 0.1, 1.15, 0.38, side * 0.83, 1.59, 2.8, boiler);
    for (let i = 0; i < 3; i++)
      box(body, 0.32, 0.08, 0.63, side * 1.23, 0.48 + i * 0.29, -4.5, black);
  }
  shell(
    [
      { z: -5.05, w: 1.375, bottom: 3.28, shoulder: 3.31, top: 3.61 },
      { z: -2.39, w: 1.375, bottom: 3.28, shoulder: 3.31, top: 3.61 },
    ],
    black,
  );
  const motion = buildSteamGear(parts, gauge, boiler, smokebox);
  box(body, 1.0, 0.35, 6.3, 0, 0.92, 0.1, boiler);
  box(body, 0.5, 0.22, 2.3, 0, 0.7, 4.1, black); // Leading-truck and pilot frame extension.
  box(body, 2.72, 0.2, 0.22, 0, 0.65, 4.98, black);
  // Slatted wedge-shaped pilot (cowcatcher).
  for (let i = -7; i <= 7; i++) {
    const slat = box(body, 0.065, 0.08, 0.9, i * 0.17, 0.48, 5.1, black);
    slat.rotation.x = 0.55;
  }
  box(body, 2.72, 0.09, 0.09, 0, 0.25, 5.49, black);
  const lamp = new THREE.MeshStandardMaterial({
    color: 0xffe4a6,
    emissive: 0xffce77,
    emissiveIntensity: 0.8,
  });
  pipe(0.2, 0.29, 0, 3.21, 4.44, black);
  pipe(0.145, 0.035, 0, 3.21, 4.61, lamp);
  return motion;
}
