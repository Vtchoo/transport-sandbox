import * as THREE from 'three';
import { buildGe809 } from './ge809';
import { vehicleBogie } from './catalog';
import type { BogieDefinition, VehicleDefinition } from './catalog';

export interface VehicleVisual {
  group: THREE.Group; body: THREE.Group; bogies: [THREE.Group, THREE.Group]; definition: VehicleDefinition; gauge: number;
}
export function makeVehicle(definition: VehicleDefinition, gauge: number): VehicleVisual {
  const bogie = vehicleBogie(definition, gauge);
  const group = new THREE.Group(), body = new THREE.Group();
  group.name = definition.id; body.name = 'body'; group.add(body);
  const material = (color: number, metalness = 0.2, roughness = 0.5) => new THREE.MeshStandardMaterial({ color, metalness, roughness });
  const silver = material(0xc4c9cb, 0.75, 0.32), roof = material(0x9ca6aa, 0.65, 0.4);
  const red = material(0xbe211e, 0.4, 0.34), yellow = material(0xe8b52f, 0.55, 0.35);
  const black = material(0x232a2b, 0.3, 0.7), glass = material(0x17343c, 0.6, 0.17), rubber = material(0x151c1d, 0, 0.9);
  function mesh(parent: THREE.Group, geometry: THREE.BufferGeometry, mat: THREE.Material, x: number, y: number, z: number) {
    const object = new THREE.Mesh(geometry, mat); object.position.set(x, y, z); parent.add(object); return object;
  }
  function box(parent: THREE.Group, w: number, h: number, l: number, x: number, y: number, z: number, mat = silver) {
    return mesh(parent, new THREE.BoxGeometry(w, h, l), mat, x, y, z);
  }
  function cylinder(parent: THREE.Group, r: number, height: number, x: number, y: number, z: number, mat = black) {
    return mesh(parent, new THREE.CylinderGeometry(r, r, height, 20), mat, x, y, z);
  }
  function label(text: string, width: number, height: number, x: number, y: number, z: number, yaw: number, color = '#20252a', italic = false) {
    if (typeof document === 'undefined' || !document.createElement) return;
    const canvas = document.createElement('canvas'); canvas.width = 1024; canvas.height = 128;
    const context = canvas.getContext('2d'); if (!context) return;
    context.fillStyle = color; context.font = `${italic ? 'italic ' : ''}bold 83px ${italic ? 'Georgia' : 'Arial'}`;
    context.textAlign = 'center'; context.textBaseline = 'middle'; context.fillText(text, 512, 65, 1000);
    const texture = new THREE.CanvasTexture(canvas); texture.colorSpace = THREE.SRGBColorSpace;
    const mat = new THREE.MeshBasicMaterial({ map: texture, transparent: true, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2 });
    const sign = mesh(body, new THREE.PlaneGeometry(width, height), mat, x, y, z); sign.rotation.y = yaw;
  }
  // Shell sections: flat sides and a rounded crown, with a tapering bulldog nose.
  function shell(sections: { z: number; w: number; bottom: number; shoulder: number; top: number }[], mat: THREE.Material) {
    const vertices: number[] = [], indices: number[] = [];
    for (const s of sections) for (const [x, y] of [
      [-s.w, s.bottom], [-s.w, s.shoulder], [-s.w * 0.92, s.shoulder + (s.top - s.shoulder) * 0.6],
      [-s.w * 0.55, s.top - 0.025], [0, s.top], [s.w * 0.55, s.top - 0.025],
      [s.w * 0.92, s.shoulder + (s.top - s.shoulder) * 0.6], [s.w, s.shoulder], [s.w, s.bottom],
    ]) vertices.push(x, y, s.z);
    const count = 9;
    for (let i = 0; i < sections.length - 1; i++) for (let j = 0; j < count; j++) {
      const a = i * count + j, b = i * count + (j + 1) % count, c = a + count, d = b + count;
      indices.push(a, c, b, b, c, d);
    }
    for (let j = 1; j < count - 1; j++) {
      indices.push(0, j, j + 1);
      const end = (sections.length - 1) * count; indices.push(end, end + j + 1, end + j);
    }
    const geometry = new THREE.BufferGeometry(); geometry.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3));
    geometry.setIndex(indices); geometry.computeVertexNormals();
    return mesh(body, geometry, mat, 0, 0, 0);
  }
  const half = definition.bodyLength / 2, w = definition.width / 2;
  box(body, definition.width - 0.15, 0.32, definition.bodyLength - 0.25, 0, 1.16, 0, black);
  box(body, 1.7, 0.55, definition.kind === 'locomotive' ? 4.6 : 7.5, 0, 0.75, 0, black);
  for (const end of [-1, 1]) {
    box(body, 0.18, 0.2, 0.6, 0, 0.88, end * (definition.length / 2 - 0.4), black);
    box(body, 0.35, 0.26, 0.22, 0, 0.88, end * (definition.length / 2 - 0.11), black);
  }
  if (definition.id === 'ge-809') {
    buildGe809(body);
  } else if (definition.kind === 'locomotive') {
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
  } else {
    shell([{ z: -half, w, bottom: 1.25, shoulder: 3.32, top: 4.04 }, { z: half, w, bottom: 1.25, shoulder: 3.32, top: 4.04 }], silver);
    for (const side of [-1, 1]) {
      // Stainless-steel fluting, separate glass panes and vestibule doors.
      for (let rib = 0; rib < 9; rib++) box(body, 0.035, 0.026, definition.bodyLength - 0.6, side * (w + 0.012), 1.4 + rib * 0.095, 0, roof);
      for (const z of [-half + 0.65, half - 0.65]) {
        box(body, 0.025, 1.95, 0.75, side * (w + 0.02), 2.3, z, roof);
        box(body, 0.035, 0.55, 0.43, side * (w + 0.04), 2.86, z, glass);
        box(body, 0.025, 0.035, 0.18, side * (w + 0.03), 2.22, z + 0.18, black);
        for (let step = 0; step < 3; step++) box(body, 0.23, 0.07, 0.8, side * (w - 0.05), 0.57 + step * 0.22, z, black);
      }
      for (let i = 0; i < 13; i++) {
        const z = -9.8 + i * 1.63;
        box(body, 0.025, 0.91, 1.23, side * (w + 0.015), 2.8, z, black);
        box(body, 0.035, 0.8, 1.1, side * (w + 0.03), 2.8, z, glass);
      }
      label('S A N T A   F E', 6.5, 0.28, side * (w + 0.04), 3.47, 0, side * Math.PI / 2);
      label(definition.kind === 'dome' ? 'PLEASURE DOME · 500' : 'CHAIR CAR · 3150', 3.5, 0.2, side * (w + 0.04), 2.07, 0, side * Math.PI / 2);
    }
    for (const end of [-1, 1]) {
      box(body, 1.05, 2.35, 0.35, 0, 2.45, end * (half + 0.1), rubber);
      box(body, 0.76, 1.93, 0.38, 0, 2.4, end * (half + 0.12), roof);
      box(body, 0.43, 0.53, 0.4, 0, 2.83, end * (half + 0.13), glass);
    }
    for (const z of [-6, 5]) box(body, 1.05, 0.44, 2.3, 0, 0.81, z, roof);
    if (definition.kind === 'dome') {
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
  }
  function makeBogie(spec: BogieDefinition) {
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
  const bogies: [THREE.Group, THREE.Group] = [makeBogie(bogie), makeBogie(bogie)];
  bogies[0].position.z = definition.bogieCenters / 2; bogies[1].position.z = -definition.bogieCenters / 2;
  group.add(...bogies);
  // Materials created for optional details but not used must not retain GPU resources.
  const used = new Set<THREE.Material>(); group.traverse(object => { if (object instanceof THREE.Mesh) used.add(object.material as THREE.Material); });
  for (const mat of [silver, roof, red, yellow, black, glass, rubber]) if (!used.has(mat)) mat.dispose();
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
