import * as THREE from 'three';

export interface ImportedMesh {
  name: string; material: number; positions: readonly number[]; normals: readonly number[];
}
export interface ImportedVehicleAsset {
  materials: readonly { name: string; color: readonly number[]; opacity: number; side: number }[];
  parts: { body: readonly ImportedMesh[]; front: readonly ImportedMesh[]; rear: readonly ImportedMesh[] };
}

/** Build independent materials/geometry so ghosts and undo cannot alter another vehicle. */
export function importedVehicleParts(asset: ImportedVehicleAsset) {
  const materials = asset.materials.map(source => {
    const material = new THREE.MeshStandardMaterial({
      name: source.name, color: new THREE.Color().fromArray(source.color),
      opacity: source.opacity, transparent: source.opacity < 1,
      side: source.side as THREE.Side, roughness: .65, metalness: .1,
    });
    return material;
  });
  function build(name: keyof ImportedVehicleAsset['parts']) {
    const group = new THREE.Group(); group.name = `imported-${name}`;
    for (const [index, source] of asset.parts[name].entries()) {
      const geometry = new THREE.BufferGeometry();
      geometry.setAttribute('position', new THREE.Float32BufferAttribute(source.positions, 3));
      geometry.setAttribute('normal', new THREE.Float32BufferAttribute(source.normals, 3));
      const mesh = new THREE.Mesh(geometry, materials[source.material]);
      mesh.name = `${source.name}-${index}`; group.add(mesh);
    }
    return group;
  }
  return { body: build('body'), front: build('front'), rear: build('rear') };
}
