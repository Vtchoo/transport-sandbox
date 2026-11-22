import * as THREE from 'three';
import { trackFrame } from './model';

const SLEEPER_LOD_DISTANCES = [150, 400, 900, 1600];
interface SleeperLOD { center: THREE.Vector3; counts: number[]; level: number }

/** Adjust instance draw counts without rebuilding tracks or uploading new matrices. */
export function updateSleeperLOD(group: THREE.Group, eye: THREE.Vector3) {
  for (const child of group.children) {
    const lod = child.userData.sleeperLOD as SleeperLOD | undefined;
    if (!(child instanceof THREE.InstancedMesh) || !lod) continue;
    const distance = eye.distanceTo(lod.center);
    // Separate enter/leave distances prevent flickering near a boundary.
    while (lod.level < 4 && distance > SLEEPER_LOD_DISTANCES[lod.level] * 1.1) lod.level++;
    while (lod.level > 0 && distance < SLEEPER_LOD_DISTANCES[lod.level - 1] * 0.9) lod.level--;
    child.count = lod.counts[lod.level]; child.visible = child.count > 0;
  }
}

function sweep(points: THREE.Vector3[], rights: THREE.Vector3[], ups: THREE.Vector3[], profile: [number, number][], material: THREE.Material, anchor: THREE.Vector3) {
  const positions: number[] = [], indices: number[] = [];
  const p = new THREE.Vector3();
  points.forEach((point, i) => profile.forEach(([x, y]) => {
    p.copy(point).sub(anchor).addScaledVector(rights[i], x).addScaledVector(ups[i], y);
    positions.push(p.x, p.y, p.z);
  }));
  const width = profile.length;
  for (let i = 0; i < points.length - 1; i++) for (let j = 0; j < width; j++) {
    const a = i * width + j, b = i * width + (j + 1) % width, c = a + width, d = b + width;
    indices.push(a, b, c, b, d, c);
  }
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
  geometry.setIndex(indices); geometry.computeVertexNormals();
  return new THREE.Mesh(geometry, material);
}
export function makeTrack(curve: THREE.CubicBezierCurve3, gauge: number, preview = false, railsOnly = false) {
  curve.arcLengthDivisions = 512;
  const length = curve.getLength();
  const steps = Math.max(2, Math.ceil(length / 1.5));
  const points = curve.getSpacedPoints(steps);
  const frames = points.map((_, i) => trackFrame(curve.getTangentAt(i / steps)));
  const rights = frames.map(frame => frame.right), ups = frames.map(frame => frame.up);
  const anchor = points[0].clone();
  const group = new THREE.Group(); group.position.copy(anchor); group.userData.anchor = anchor;
  const steel = new THREE.MeshStandardMaterial({ color: preview ? 0x7aefc1 : 0xa2acaf, metalness: 0.7, roughness: 0.3, side: THREE.DoubleSide, transparent: preview, opacity: preview ? 0.42 : 1, depthWrite: !preview });
  if (!railsOnly) {
    const ballast = new THREE.MeshStandardMaterial({ color: preview ? 0x7aefc1 : 0x77766b, roughness: 1, side: THREE.DoubleSide, transparent: preview, opacity: preview ? 0.25 : 1, depthWrite: !preview });
    group.add(sweep(points, rights, ups, [[-2, -0.65], [-1.36, -0.33], [1.36, -0.33], [2, -0.65]], ballast, anchor));
  }
  for (const sign of [-1, 1]) {
    const offset = sign * (gauge / 2000 + 0.035);
    group.add(sweep(points, rights, ups, [[offset - 0.035, 0], [offset + 0.035, 0], [offset + 0.035, -0.15], [offset - 0.035, -0.15]], steel, anchor));
  }
  if (!preview && !railsOnly) {
    const sleeperGeometry = new THREE.BoxGeometry(gauge / 1000 + 0.85, 0.18, 0.24);
    const sleeperMaterial = new THREE.MeshStandardMaterial({ color: 0x605346, roughness: 0.95 });
    const count = Math.max(1, Math.floor(length / 0.65));
    const matrix = new THREE.Matrix4(), point = new THREE.Vector3();
    // Short batches allow the nearby end of a long segment to retain full detail.
    for (let start = 0; start < count; start += 64) {
      const size = Math.min(64, count - start);
      const sleepers = new THREE.InstancedMesh(sleeperGeometry, sleeperMaterial, size);
      // Prefixes contain every eighth, fourth, second, then every sleeper.
      // All levels reuse original sleeper positions rather than stretching spacing.
      const indices = Array.from({ length: size }, (_, i) => start + i);
      const priority = (i: number) => i % 8 === 0 ? 0 : i % 4 === 0 ? 1 : i % 2 === 0 ? 2 : 3;
      indices.sort((a, b) => priority(a) - priority(b) || a - b);
      for (const [instance, i] of indices.entries()) {
        const t = (i + 0.5) / count, frame = trackFrame(curve.getTangentAt(t));
        point.copy(curve.getPointAt(t)).sub(anchor).addScaledVector(frame.up, -0.24);
        matrix.makeBasis(frame.right, frame.up, frame.forward).setPosition(point);
        sleepers.setMatrixAt(instance, matrix);
      }
      sleepers.userData.sleeperLOD = {
        center: curve.getPointAt((start + size / 2) / count),
        counts: [size, indices.filter(i => i % 2 === 0).length, indices.filter(i => i % 4 === 0).length, indices.filter(i => i % 8 === 0).length, 0],
        level: 0,
      } satisfies SleeperLOD;
      // Keep full bounds even while only an instance prefix is being drawn.
      sleepers.computeBoundingSphere();
      group.add(sleepers);
    }
  }
  return group;
}
export function disposeTrack(group: THREE.Object3D) {
  const materials = new Set<THREE.Material>();
  group.traverse(object => {
    if (object instanceof THREE.Mesh || object instanceof THREE.Line) {
      object.geometry.dispose();
      for (const material of Array.isArray(object.material) ? object.material : [object.material]) materials.add(material);
      if (object instanceof THREE.InstancedMesh) object.dispose();
    }
  });
  materials.forEach(material => material.dispose());
  group.clear();
}
