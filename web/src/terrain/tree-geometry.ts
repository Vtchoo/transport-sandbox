import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

export const treeTrunk = () => new THREE.CylinderGeometry(.013, .025, 1, 5).translate(0, .5, 0);

export function treeCrown(conifer: boolean, distant: boolean) {
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

