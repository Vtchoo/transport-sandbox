import * as THREE from 'three';

/** Per-vehicle materials and reusable mesh primitives; dimensions are metres. */
export function createVehicleParts(body: THREE.Group) {
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
  // Cross-section shell used for rounded bodies and tapered noses.
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
  return { body, materials: { silver, roof, red, yellow, black, glass, rubber }, mesh, box, cylinder, label, shell };
}
export type VehicleModelParts = ReturnType<typeof createVehicleParts>;
