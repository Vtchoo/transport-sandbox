import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { TREE_STRIDE } from './vegetation';

export interface TreeChunk { group: THREE.Group; count: number; meshes: THREE.InstancedMesh[] }

/** Shared geometry/materials, with separate batches per terrain tile for frustum culling. */
export class Trees {
  private bark = new THREE.MeshStandardMaterial({ color: 0x655343, roughness: 1 });
  private leaves = new THREE.MeshStandardMaterial({ roughness: 1, flatShading: true });
  private trunk = new THREE.CylinderGeometry(.013, .025, 1, 5).translate(0, .5, 0);
  private crowns = [this.crown(false, false), this.crown(true, false)];
  private distantCrowns = [this.crown(false, true), this.crown(true, true)];

  private crown(conifer: boolean, distant: boolean) {
    if (conifer) {
      const parts = (distant ? [[1, .82, .59]] : [[1, .57, .4], [.82, .52, .6], [.56, .43, .8]])
        .map(([r, h, y]) => new THREE.ConeGeometry(r, h, distant ? 5 : 7).translate(0, y, 0));
      const result = mergeGeometries(parts)!;
      parts.forEach(part => part.dispose());
      return result;
    }
    const parts = (distant ? [[0, .69, 0, 1, .32, .9]] : [
      [0, .72, 0, .85, .29, .8], [-.4, .57, .12, .64, .24, .67],
      [.42, .62, .17, .66, .27, .65], [.05, .59, -.4, .72, .26, .63],
    ]).map(([x, y, z, sx, sy, sz]) => new THREE.IcosahedronGeometry(1, distant ? 0 : 1).scale(sx, sy, sz).translate(x, y, z));
    const result = mergeGeometries(parts)!;
    parts.forEach(part => part.dispose());
    return result;
  }

  create(data: Float32Array): TreeChunk {
    const group = new THREE.Group();
    group.name = 'Natural trees';
    const meshes: THREE.InstancedMesh[] = [];
    const transform = new THREE.Object3D(), color = new THREE.Color();
    for (let species = 0; species < 2; species++) {
      const indices: number[] = [];
      for (let i = 0; i < data.length; i += TREE_STRIDE) if (data[i + 6] === species) indices.push(i);
      if (!indices.length) continue;
      const trunk = new THREE.InstancedMesh(this.trunk, this.bark, indices.length);
      const crown = new THREE.InstancedMesh(this.crowns[species], this.leaves, indices.length);
      crown.userData.species = species;
      indices.forEach((i, index) => {
        const [x, y, z, height, radius, yaw] = data.subarray(i, i + TREE_STRIDE);
        transform.position.set(x, y - .8, z); // Roots penetrate small terrain LOD differences.
        transform.rotation.set(0, yaw, 0);
        transform.scale.set(height, height, height);
        transform.updateMatrix();
        trunk.setMatrixAt(index, transform.matrix);
        transform.scale.set(radius, height, radius);
        transform.updateMatrix();
        crown.setMatrixAt(index, transform.matrix);
        color.setHSL((species ? .32 : .28) + Math.sin(yaw) * .018, .42 + Math.cos(yaw) * .06, species ? .14 : .19);
        crown.setColorAt(index, color);
      });
      for (const mesh of [trunk, crown]) {
        mesh.computeBoundingSphere();
        mesh.computeBoundingBox();
        meshes.push(mesh); group.add(mesh);
      }
    }
    return { group, meshes, count: data.length / TREE_STRIDE };
  }

  update(chunk: TreeChunk, distance: number) {
    for (const mesh of chunk.meshes) {
      const species = mesh.userData.species as number | undefined;
      if (species === undefined) mesh.visible = distance < 1100;
      else {
        const geometry = (distance < 850 ? this.crowns : this.distantCrowns)[species];
        if (mesh.geometry !== geometry) {
          mesh.geometry = geometry;
          mesh.computeBoundingSphere();
        }
      }
    }
  }

  release(chunk: TreeChunk) {
    chunk.group.removeFromParent();
    chunk.meshes.forEach(mesh => mesh.dispose()); // Shared geometry is owned by this renderer.
    chunk.group.clear();
  }

  dispose() {
    this.trunk.dispose();
    [...this.crowns, ...this.distantCrowns].forEach(geometry => geometry.dispose());
    this.bark.dispose(); this.leaves.dispose();
  }
}
