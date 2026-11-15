import * as THREE from 'three';
import type { VehicleDefinition } from '../types';
import type { VehicleModelParts } from '../model-parts';

export const F7A: VehicleDefinition = {
  id: 'atsf-f7a',
  name: 'Santa Fe F7A · Warbonnet',
  kind: 'locomotive',
  length: 15.4432, bodyLength: 14.4,
  width: 2.9972, height: 4.5,
  bogieCenters: 9.144,
  bogiesByGauge: { 1435: 'blomberg-b-1435' },
  physics: { massKg: 104326, powerW: 1118550, tractiveEffortN: 251326, maxSpeedMps: 45.599 },
};

export function buildF7a(parts: VehicleModelParts, definition: VehicleDefinition) {
  const { body, box, cylinder, mesh, label, shell, materials: { silver, roof, red, yellow, black, glass } } = parts;
  const half = definition.bodyLength / 2, w = definition.width / 2;
  shell([{ z: -half, w, bottom: 1.3, shoulder: 3.45, top: 4.27 }, { z: 3.15, w, bottom: 1.3, shoulder: 3.45, top: 4.27 }], silver);
  shell([
    { z: 3.15, w, bottom: 1.3, shoulder: 3.45, top: 4.27 },
    { z: 4.35, w, bottom: 1.3, shoulder: 3.44, top: 4.19 },
    { z: 5.15, w: w * 0.96, bottom: 1.3, shoulder: 3.05, top: 3.73 },
    { z: 6.15, w: w * 0.85, bottom: 1.3, shoulder: 2.65, top: 3.0 },
    { z: 6.85, w: w * 0.61, bottom: 1.3, shoulder: 2.48, top: 2.73 },
    { z: half, w: w * 0.28, bottom: 1.3, shoulder: 2.34, top: 2.5 },
  ], red);
  for (const side of [-1, 1]) {
    // Yellow/black border and swept-back red cheek of the Warbonnet.
    const cheek = new THREE.Shape(); cheek.moveTo(1.4, 1.45); cheek.lineTo(1.7, 2.2); cheek.quadraticCurveTo(1.9, 3.25, 3.15, 3.6); cheek.lineTo(3.15, 1.45); cheek.closePath();
    const patch = mesh(body, new THREE.ShapeGeometry(cheek), red, side * (w + 0.006), 0, 0);
    // Shape local X follows longitudinal Z, local Y stays vertical.
    patch.rotation.y = side * Math.PI / 2; patch.scale.x = -side;
    const outline = new THREE.CatmullRomCurve3([new THREE.Vector3(side * (w + 0.02), 1.45, 1.4), new THREE.Vector3(side * (w + 0.02), 2.2, 1.7), new THREE.Vector3(side * (w + 0.02), 3.15, 2.35), new THREE.Vector3(side * (w + 0.02), 3.6, 3.15)]);
    mesh(body, new THREE.TubeGeometry(outline, 16, 0.045, 5, false), yellow, 0, 0, 0);
    box(body, 0.035, 0.09, 10.3, side * (w + 0.02), 1.45, -1.7, yellow);
    box(body, 0.035, 0.035, 10.3, side * (w + 0.04), 1.36, -1.7, black);
    // Engine-room portholes and radiator grilles.
    for (const z of [-4.8, -2.1, 0.5]) {
      const rim = cylinder(body, 0.27, 0.055, side * (w + 0.015), 2.65, z, black); rim.rotation.z = Math.PI / 2;
      const window = cylinder(body, 0.22, 0.065, side * (w + 0.03), 2.65, z, glass); window.rotation.z = Math.PI / 2;
    }
    box(body, 0.025, 0.5, 8.8, side * (w + 0.005), 3.4, -1.7, black);
    for (let i = 0; i < 54; i++) box(body, 0.035, 0.48, 0.026, side * (w + 0.023), 3.4, -5.95 + i * 0.16, roof);
    box(body, 0.035, 0.6, 0.7, side * (w + 0.015), 3.3, 3.95, black);
    box(body, 0.04, 0.5, 0.58, side * (w + 0.03), 3.3, 3.95, glass);
    for (let step = 0; step < 3; step++) box(body, 0.23, 0.09, 0.72, side * (w - 0.07), 0.46 + step * 0.25, 4.02, silver);
    label('Santa Fe', 3.35, 0.63, side * (w + 0.045), 2.03, -2.25, side * Math.PI / 2, '#202b31', true);
    label('37 L', 0.65, 0.22, side * (w + 0.05), 2.45, 3.2, side * Math.PI / 2, '#f5ddac');
    const windscreen = box(body, 0.92, 0.62, 0.045, side * 0.52, 3.93, 4.9, black);
    windscreen.rotation.x = -1.045;
    const pane = box(body, 0.79, 0.49, 0.055, side * 0.52, 3.956, 4.918, glass); pane.rotation.x = -1.045;
    const wiper = box(body, 0.025, 0.3, 0.06, side * 0.57, 3.97, 4.98, black); wiper.rotation.x = -1.045; wiper.rotation.z = side * 0.35;
  }
  for (const z of [-5.6, -4.25, -2.9, -1.55]) {
    cylinder(body, 0.49, 0.09, 0, 4.28, z, black);
    cylinder(body, 0.43, 0.11, 0, 4.3, z, roof);
    for (let spoke = 0; spoke < 8; spoke++) { const blade = box(body, 0.8, 0.025, 0.024, 0, 4.36, z, black); blade.rotation.y = spoke * Math.PI / 8; }
  }
  cylinder(body, 0.4, 0.06, 0, 4.29, 0.2, black);
  for (const x of [-0.26, 0.26]) {
    const horn = cylinder(body, 0.085, 0.42, x, 4.4, 2.35, yellow); horn.rotation.x = Math.PI / 2;
  }
  const lampMaterial = new THREE.MeshStandardMaterial({ color: 0xffedc2, emissive: 0xffd98a, emissiveIntensity: 1.8 });
  for (const y of [2.34, 2.52]) { const lamp = cylinder(body, 0.09, 0.12, 0, y, half + 0.03, lampMaterial); lamp.rotation.x = Math.PI / 2; }
  const badge = cylinder(body, 0.38, 0.06, 0, 2.03, half + 0.04, yellow); badge.rotation.x = Math.PI / 2;
  box(body, 0.65, 0.055, 0.08, 0, 2.03, half + 0.08, black);
  box(body, 0.055, 0.65, 0.08, 0, 2.03, half + 0.08, black);
  label('SANTA FE', 0.63, 0.13, 0, 2.02, half + 0.13, 0);
  box(body, 2.65, 0.42, 0.16, 0, 0.53, half - 0.1, red);
  
}
