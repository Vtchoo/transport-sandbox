import * as THREE from 'three';
import { GLTFExporter } from 'three/addons/exporters/GLTFExporter.js';
import { VEHICLES } from '../../src/train/catalog';
import { makeVehicle, disposeVehicle } from '../../src/train/render';

// Run inside Chrome: vehicle factories need real Canvas2D fonts and textures.
export async function exportVehicle(id: string, gauge: number) {
  await document.fonts.ready;
  const definition = VEHICLES.find(vehicle => vehicle.id === id);
  if (!definition) throw new Error(`Unknown vehicle: ${id}`);
  const visual = makeVehicle(definition, gauge);
  try {
    const animations: THREE.KeyframeTrack[] = [];
    const mark = (v: typeof visual, prefix = '') => {
      v.body.name = `${prefix}body`;
      v.bogies.forEach((bogie, i) => { bogie.name = `${prefix}bogie_${i}`; });
      if (v.motion) {
        v.motion.parts.forEach((part, i) => { part.name = `${prefix}motion_${i}`; });
        const times: number[] = [], positions = v.motion.parts.map(() => [] as number[]);
        const rotations = v.motion.parts.map(() => [] as number[]), scales = v.motion.parts.map(() => [] as number[]);
        // One full crank revolution, still driven by distance in the game.
        for (let frame = 0; frame <= 64; frame++) {
          if (frame) v.motion.advance(Math.PI * 2 * .65 / 64);
          times.push(frame / 64);
          v.motion.parts.forEach((p, i) => {
            positions[i].push(...p.position.toArray()); rotations[i].push(...p.quaternion.toArray()); scales[i].push(...p.scale.toArray());
          });
        }
        v.motion.parts.forEach((p, i) => {
          animations.push(new THREE.VectorKeyframeTrack(`${p.name}.position`, times, positions[i]));
          animations.push(new THREE.QuaternionKeyframeTrack(`${p.name}.quaternion`, times, rotations[i]));
          animations.push(new THREE.VectorKeyframeTrack(`${p.name}.scale`, times, scales[i]));
        });
      }
      v.sections?.forEach((section, i) => mark(section, `section_${i}_`));
    };
    mark(visual);
    let lettering = 0;
    visual.group.traverse(object => {
      if (!(object instanceof THREE.Mesh)) return;
      const materials = Array.isArray(object.material) ? object.material : [object.material];
      if (materials.some(material => material instanceof THREE.MeshBasicMaterial && material.map instanceof THREE.CanvasTexture)) object.name = `lettering_${lettering++}`;
    });
    const clips = animations.length ? [new THREE.AnimationClip('running_gear', 1, animations)] : [];
    const binary = await new GLTFExporter().parseAsync(visual.group, { binary: true, animations: clips, onlyVisible: true }) as ArrayBuffer;
    const bytes = new Uint8Array(binary);
    let encoded = '';
    for (let i = 0; i < bytes.length; i += 32768) encoded += String.fromCharCode(...bytes.subarray(i, i + 32768));
    return { base64: btoa(encoded), lettering };
  } finally {
    disposeVehicle(visual);
  }
}
