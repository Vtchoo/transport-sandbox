import * as THREE from 'three';
import { TREE_STRIDE } from './vegetation';
import { treeCrown, treeTrunk } from './tree-geometry';

type Point = [number, number, number];
export interface TreeBounds { min: Point; max: Point; center: Point; radius: number }
export interface PreparedTreeBatch {
  species: number;
  trunks: Float32Array; crowns: Float32Array; colors: Float32Array;
  trunkBounds: TreeBounds; nearBounds: TreeBounds; farBounds: TreeBounds;
}
export interface PreparedTrees { count: number; bytes: number; batches: PreparedTreeBatch[] }

// CPU-only Three.js geometry/math is safe in workers. No renderer or GPU resources here.
let geometry: THREE.BufferGeometry[] | undefined;
function bounds(matrices: Float32Array, shape: THREE.BufferGeometry): TreeBounds {
  if (!shape.boundingBox) shape.computeBoundingBox();
  if (!shape.boundingSphere) shape.computeBoundingSphere();
  const box = new THREE.Box3(), sphere = new THREE.Sphere();
  const matrix = new THREE.Matrix4(), partBox = new THREE.Box3(), partSphere = new THREE.Sphere();
  for (let i = 0; i < matrices.length; i += 16) {
    matrix.fromArray(matrices, i);
    box.union(partBox.copy(shape.boundingBox!).applyMatrix4(matrix));
    sphere.union(partSphere.copy(shape.boundingSphere!).applyMatrix4(matrix));
  }
  return { min: box.min.toArray(), max: box.max.toArray(), center: sphere.center.toArray(), radius: sphere.radius };
}

/** Identical transforms, HSL colors and culling bounds to the original per-instance renderer. */
export function prepareTrees(data: Float32Array): PreparedTrees {
  geometry ??= [treeTrunk(), treeCrown(false, false), treeCrown(true, false), treeCrown(false, true), treeCrown(true, true)];
  const batches: PreparedTreeBatch[] = [], transform = new THREE.Object3D(), color = new THREE.Color();
  let bytes = 0;
  for (let species = 0; species < 2; species++) {
    let count = 0;
    for (let i = 0; i < data.length; i += TREE_STRIDE) if (data[i + 6] === species) count++;
    if (!count) continue;
    const trunks = new Float32Array(count * 16), crowns = new Float32Array(count * 16), colors = new Float32Array(count * 3);
    let index = 0;
    for (let i = 0; i < data.length; i += TREE_STRIDE) {
      if (data[i + 6] !== species) continue;
      const x = data[i], y = data[i + 1], z = data[i + 2], height = data[i + 3], radius = data[i + 4], yaw = data[i + 5];
      transform.position.set(x, y - .8, z);
      transform.rotation.set(0, yaw, 0);
      transform.scale.set(height, height, height);
      transform.updateMatrix(); transform.matrix.toArray(trunks, index * 16);
      transform.scale.set(radius, height, radius);
      transform.updateMatrix(); transform.matrix.toArray(crowns, index * 16);
      color.setHSL((species ? .32 : .28) + Math.sin(yaw) * .018, .42 + Math.cos(yaw) * .06, species ? .14 : .19);
      color.toArray(colors, index * 3);
      index++;
    }
    batches.push({ species, trunks, crowns, colors, trunkBounds: bounds(trunks, geometry[0]),
      nearBounds: bounds(crowns, geometry[species + 1]), farBounds: bounds(crowns, geometry[species + 3]) });
    bytes += trunks.byteLength + crowns.byteLength + colors.byteLength;
  }
  return { count: data.length / TREE_STRIDE, bytes, batches };
}

export function treeTransfers(prepared: PreparedTrees): ArrayBuffer[] {
  return prepared.batches.flatMap(batch => [batch.trunks.buffer, batch.crowns.buffer, batch.colors.buffer] as ArrayBuffer[]);
}
