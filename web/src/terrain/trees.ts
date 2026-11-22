import * as THREE from 'three';
import { treeCrown, treeTrunk } from './tree-geometry';
import { prepareTrees, type PreparedTrees, type TreeBounds } from './tree-data';

export interface TreeChunk { group: THREE.Group; count: number; meshes: THREE.InstancedMesh[]; prepared: PreparedTrees; lod?: number }

/** Shared geometry/materials, with separate batches per terrain tile for frustum culling. */
export class Trees {
  private bark = new THREE.MeshStandardMaterial({ color: 0x655343, roughness: 1 });
  private leaves = new THREE.MeshStandardMaterial({ roughness: 1, flatShading: true });
  private trunk = treeTrunk();
  private crowns = [treeCrown(false, false), treeCrown(true, false)];
  private distantCrowns = [treeCrown(false, true), treeCrown(true, true)];

  /** Synchronous convenience for the small menu scene. Streaming uses worker-prepared data. */
  create(data: Float32Array): TreeChunk { return this.createPrepared(prepareTrees(data)); }

  createPrepared(prepared: PreparedTrees): TreeChunk {
    const group = new THREE.Group();
    group.name = 'Natural trees';
    const chunk: TreeChunk = { group, meshes: [], count: prepared.count, prepared };
    for (const batch of prepared.batches) {
      // Avoid InstancedMesh's identity-matrix initialization loop; install complete worker buffers.
      const trunk = new THREE.InstancedMesh(this.trunk, this.bark, 0);
      const crown = new THREE.InstancedMesh(this.crowns[batch.species], this.leaves, 0);
      crown.userData.species = batch.species;
      chunk.meshes.push(trunk, crown);
      group.add(trunk, crown);
    }
    this.replace(chunk, prepared);
    return chunk;
  }

  /** Reuse uploaded batches when only terrain LOD/root elevations change. */
  replace(chunk: TreeChunk, prepared: PreparedTrees) {
    chunk.prepared = prepared;
    chunk.count = prepared.count;
    chunk.lod = undefined;
    for (let i = 0; i < prepared.batches.length; i++) {
      const batch = prepared.batches[i], trunk = chunk.meshes[i * 2], crown = chunk.meshes[i * 2 + 1];
      trunk.count = crown.count = batch.trunks.length / 16;
      this.buffer(trunk, batch.trunks);
      this.buffer(crown, batch.crowns);
      if (!crown.instanceColor || crown.instanceColor.array.length !== batch.colors.length)
        crown.instanceColor = new THREE.InstancedBufferAttribute(batch.colors.slice(), 3);
      else { crown.instanceColor.array.set(batch.colors); crown.instanceColor.needsUpdate = true; }
      this.bounds(trunk, batch.trunkBounds);
      this.bounds(crown, batch.nearBounds);
    }
    this.update(chunk, 0);
  }

  private buffer(mesh: THREE.InstancedMesh, values: Float32Array) {
    if (mesh.instanceMatrix.array.length !== values.length)
      mesh.instanceMatrix = new THREE.InstancedBufferAttribute(values.slice(), 16);
    else { mesh.instanceMatrix.array.set(values); mesh.instanceMatrix.needsUpdate = true; }
  }

  private bounds(mesh: THREE.InstancedMesh, bounds: TreeBounds) {
    mesh.boundingBox = new THREE.Box3(new THREE.Vector3(...bounds.min), new THREE.Vector3(...bounds.max));
    mesh.boundingSphere = new THREE.Sphere(new THREE.Vector3(...bounds.center), bounds.radius);
  }

  update(chunk: TreeChunk, distance: number) {
    const lod = (distance < 850 ? 0 : 1) + (distance < 1100 ? 0 : 2);
    if (chunk.lod === lod) return;
    chunk.lod = lod;
    for (let i = 0; i < chunk.prepared.batches.length; i++) {
      const batch = chunk.prepared.batches[i], trunk = chunk.meshes[i * 2], crown = chunk.meshes[i * 2 + 1];
      trunk.visible = distance < 1100;
      crown.geometry = (distance < 850 ? this.crowns : this.distantCrowns)[batch.species];
      this.bounds(crown, distance < 850 ? batch.nearBounds : batch.farBounds);
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
