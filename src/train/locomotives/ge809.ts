import * as THREE from 'three';
import type { VehicleDefinition } from '../types';

export const GE809: VehicleDefinition = {
  id: 'ge-809',
  name: 'GE 809 · Wide-cab demonstrator',
  kind: 'locomotive',
  length: 20.2184, bodyLength: 19.2,
  width: 3.0861, height: 4.6863,
  bogieCenters: 11.9888,
  bogiesByGauge: { 1435: 'ge-fb2-1435' },
  physics: { massKg: 127006, powerW: 2908230, tractiveEffortN: 304704, maxSpeedMps: 31.2928 },
};


/** GE's experimental wide-cab 809. Metres; railhead Y=0, short hood toward +Z.
 * Class envelope and photo-derived details, not a dimensioned factory replica.
 */
export function buildGe809(body: THREE.Group) {
  const paint = (color: number, metalness = 0.2, roughness = 0.55) => new THREE.MeshStandardMaterial({ color, metalness, roughness });
  const gray = paint(0xa6a7a3), red = paint(0xb7202b), dark = paint(0x282c2d), metal = paint(0x6c7172, 0.65);
  const glass = paint(0x152f3b, 0.45, 0.18), white = paint(0xf1e9d6);
  function mesh(geometry: THREE.BufferGeometry, material: THREE.Material, x = 0, y = 0, z = 0, name = '') {
    const object = new THREE.Mesh(geometry, material); object.position.set(x, y, z); object.name = name; body.add(object); return object;
  }
  function box(w: number, h: number, l: number, x: number, y: number, z: number, mat = gray, name = '') {
    return mesh(new THREE.BoxGeometry(w, h, l), mat, x, y, z, name);
  }
  function rod(a: number[], b: number[], radius = 0.024, mat = white) {
    const start = new THREE.Vector3(...a), end = new THREE.Vector3(...b), delta = end.clone().sub(start);
    const object = mesh(new THREE.CylinderGeometry(radius, radius, delta.length(), 6), mat);
    object.position.copy(start.add(end).multiplyScalar(0.5)); object.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), delta.normalize()); return object;
  }
  // Planar, chamfered cross-sections. Unshared face normals keep the hood/cab crisp.
  function housing(sections: { z: number; w: number; bottomW?: number; bottom: number; shoulder: number; top: number }[], mat = gray, name = '') {
    const vertices: number[] = [], indices: number[] = [];
    for (const s of sections) for (const [x, y] of [[-(s.bottomW ?? s.w), s.bottom], [-s.w, s.shoulder], [-s.w + 0.18, s.top], [s.w - 0.18, s.top], [s.w, s.shoulder], [s.bottomW ?? s.w, s.bottom]]) vertices.push(x, y, s.z);
    for (let i = 0; i < sections.length - 1; i++) for (let j = 0; j < 6; j++) {
      const a = i * 6 + j, b = i * 6 + (j + 1) % 6;
      indices.push(a, a + 6, b, b, a + 6, b + 6);
    }
    for (let j = 1; j < 5; j++) {
      indices.push(0, j, j + 1); const end = (sections.length - 1) * 6; indices.push(end, end + j + 1, end + j);
    }
    const indexed = new THREE.BufferGeometry(); indexed.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3)); indexed.setIndex(indices);
    const geometry = indexed.toNonIndexed(); indexed.dispose(); geometry.computeVertexNormals(); return mesh(geometry, mat, 0, 0, 0, name);
  }
  function lettering(text: string, w: number, h: number, x: number, y: number, z: number, yaw = 0) {
    if (typeof document === 'undefined') return;
    const canvas = document.createElement('canvas'); canvas.width = 1024; canvas.height = 256;
    const ctx = canvas.getContext('2d'); if (!ctx) return;
    ctx.fillStyle = '#f4eee1'; ctx.font = 'bold 220px Arial'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.translate(512, 134); ctx.scale(940 / Math.max(1, ctx.measureText(text).width), 1); ctx.fillText(text, 0, 0);
    const map = new THREE.CanvasTexture(canvas); map.colorSpace = THREE.SRGBColorSpace;
    const sign = mesh(new THREE.PlaneGeometry(w, h), new THREE.MeshBasicMaterial({ map, transparent: true, depthWrite: false }), x, y, z);
    sign.rotation.y = yaw;
  }
  box(3.03, 0.25, 18.9, 0, 1.62, 0, dark, 'walkway');
  box(2.05, 0.7, 5.1, 0, 0.91, -0.3, dark, 'fuel-tank');
  // Narrow machinery hood and the broad, flared radiator at the long-hood end.
  housing([{ z: -8.65, w: 1.02, bottom: 1.75, shoulder: 4.22, top: 4.39 }, { z: 3.3, w: 1.02, bottom: 1.75, shoulder: 4.22, top: 4.39 }], gray, 'long-hood');
  housing([{ z: -8.9, w: 1.36, bottomW: 1.02, bottom: 3.27, shoulder: 4.52, top: 4.6863 }, { z: -5.8, w: 1.36, bottomW: 1.02, bottom: 3.27, shoulder: 4.52, top: 4.6863 }], gray, 'radiator-housing');
  // Short experimental North American cab; three-pane windscreen, center nose door.
  housing([
    { z: 3.25, w: 1.49, bottom: 1.75, shoulder: 4.31, top: 4.56 },
    { z: 5.7, w: 1.49, bottom: 1.75, shoulder: 4.31, top: 4.56 },
    { z: 6.08, w: 1.49, bottom: 1.75, shoulder: 4.15, top: 4.4 },
    { z: 6.65, w: 1.49, bottom: 1.75, shoulder: 3.15, top: 3.4 },
  ], gray, 'wide-cab');
  housing([{ z: 6.35, w: 1.49, bottom: 1.75, shoulder: 3.02, top: 3.2 }, { z: 7.82, w: 1.49, bottom: 1.75, shoulder: 3.02, top: 3.2 }, { z: 8.25, w: 1.12, bottom: 1.75, shoulder: 3.02, top: 3.2 }], red, 'short-nose');
  for (let i = -1; i <= 1; i++) {
    const frame = box(0.79, 0.77, 0.055, i * 0.88, 3.94, 6.37, dark, 'windshield-frame'); frame.rotation.x = -Math.atan(0.57);
    const pane = box(0.68, 0.64, 0.025, i * 0.88, 3.955, 6.4, glass, 'windshield'); pane.rotation.x = frame.rotation.x;
    rod([i * 0.88 - 0.23, 3.71, 6.57], [i * 0.88 + 0.11, 4.03, 6.39], 0.012, dark);
  }
  box(0.62, 1.43, 0.035, 0, 2.48, 8.275, dark, 'nose-door-frame');
  box(0.54, 1.34, 0.04, 0, 2.48, 8.3, red, 'nose-door');
  box(0.035, 0.18, 0.04, 0.19, 2.57, 8.33, metal);
  for (const side of [-1, 1]) {
    // Three red bands wrap the gray machinery hood; service doors below.
    for (const [y, h] of [[2.19, 0.51], [2.69, 0.18], [3.02, 0.1]]) {
      box(0.018, h, 11.85, side * 1.034, y, -2.7, red);
      box(0.018, h, 2.4, side * 1.502, y, 4.5, red);
    }
    for (let i = 0; i < 8; i++) {
      const z = -4.8 + i * 0.93;
      box(0.012, 1.4, 0.014, side * 1.047, 2.51, z, dark);
      box(0.035, 0.13, 0.025, side * 1.067, 2.59, z + 0.13, metal);
      for (let vent = 0; vent < 5; vent++) box(0.018, 0.025, 0.43, side * 1.05, 1.94 + vent * 0.075, z + 0.43, dark);
    }
    for (const [z, length] of [[-7.35, 2.7], [1.9, 1.8]]) {
      const radiator = z < -5, x = radiator ? 1.16 : 1.055, tilt = radiator ? -side * Math.atan(0.34 / 1.25) : 0;
      box(0.035, 0.86, length, side * x, 3.72, z, dark, 'intake-grille').rotation.z = tilt;
      for (let i = 0; i < 18; i++) box(0.044, 0.8, 0.025, side * (x + 0.025), 3.72, z - length / 2 + (i + 0.5) * length / 18, metal).rotation.z = tilt;
    }
    box(0.03, 0.94, 1.68, side * 1.507, 3.76, 4.55, dark);
    box(0.035, 0.8, 1.5, side * 1.53, 3.77, 4.55, glass, 'cab-side-window');
    box(0.045, 0.83, 0.045, side * 1.55, 3.77, 4.4, metal);
    lettering('809', 1.08, 0.38, side * 1.518, 3.02, 4.64, side * Math.PI / 2);
    lettering('GENERAL ELECTRIC', 5.8, 0.34, side * 1.057, 2.18, -2.4, side * Math.PI / 2);
    // GE roundel on the long hood, independently surfaced for both sides.
    const emblem = mesh(new THREE.CircleGeometry(0.46, 32), red, side * 1.058, 3.48, -2.9); emblem.rotation.y = side * Math.PI / 2;
    lettering('GE', 0.65, 0.39, side * 1.063, 3.48, -2.9, side * Math.PI / 2);
    // End platforms and walkway railings. Cab side stays clear for boarding.
    for (const [start, end] of [[-9.2, 3.1], [6.85, 9.2]]) {
      rod([side * 1.48, 2.73, start], [side * 1.48, 2.73, end]);
      const count = Math.ceil((end - start) / 1.3);
      for (let i = 0; i <= count; i++) {
        const z = start + (end - start) * i / count;
        rod([side * 1.48, 1.75, z], [side * 1.48, 2.73, z]);
      }
    }
    for (const end of [-1, 1]) {
      for (let step = 0; step < 4; step++) box(0.46, 0.075, 0.34, side * 1.22, 0.52 + step * 0.29, end * (9.33 - step * 0.17), metal);
      rod([side * 0.43, 2.73, end * 9.22], [side * 1.48, 2.73, end * 9.22]);
      rod([side * 0.43, 1.75, end * 9.22], [side * 0.43, 2.73, end * 9.22]);
    }
  }
  // Roof grilles, exhaust stack, brake resistor grid and low horn cluster.
  for (const z of [-7.85, -6.55]) {
    mesh(new THREE.CylinderGeometry(0.6, 0.6, 0.04, 32), dark, 0, 4.65, z, 'radiator-fan');
    for (let i = -4; i <= 4; i++) box(1.02, 0.015, 0.025, 0, 4.68, z + i * 0.105, metal);
  }
  box(0.5, 0.21, 0.62, 0, 4.48, -1.65, dark, 'exhaust');
  for (let i = 0; i < 12; i++) box(1.72, 0.035, 0.045, 0, 4.41, 0.15 + i * 0.12, dark);
  for (const x of [-0.19, 0, 0.19]) {
    const horn = mesh(new THREE.ConeGeometry(0.075, 0.35, 10), metal, x, 4.61, 3.85); horn.rotation.x = Math.PI / 2;
  }
  for (const end of [-1, 1]) {
    box(2.92, 0.6, 0.14, 0, 0.83, end * 9.45, dark, 'pilot');
    box(2.8, 0.045, 0.08, 0, 1.5, end * 9.5, white);
  }
  const lamps = new THREE.MeshStandardMaterial({ color: 0xfff0cd, emissive: 0xffd494, emissiveIntensity: 0.65 });
  for (const x of [-0.13, 0.13]) {
    box(0.19, 0.19, 0.06, x, 3.1, 8.29, dark);
    const lamp = mesh(new THREE.CylinderGeometry(0.065, 0.065, 0.07, 12), lamps, x, 3.1, 8.34); lamp.rotation.x = Math.PI / 2;
  }
  for (const x of [-0.95, 0.95]) {
    box(0.62, 0.22, 0.065, x, 4.25, 6.24, dark);
    lettering('809', 0.53, 0.18, x, 4.25, 6.28);
  }
}
