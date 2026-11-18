/** Rebuild the native Godot catalog from the browser game's source of truth. */
import { buildSync } from 'esbuild';
import { mkdir, writeFile } from 'node:fs/promises';
import { GLTFExporter } from 'three/addons/exporters/GLTFExporter.js';
import * as THREE from 'three';

// GLTFExporter uses the browser FileReader API even when exporting geometry only.
globalThis.FileReader = class {
  async readAsArrayBuffer(blob) { this.result = await blob.arrayBuffer(); this.onloadend?.(); }
  async readAsDataURL(blob) { this.result = `data:${blob.type};base64,${Buffer.from(await blob.arrayBuffer()).toString('base64')}`; this.onloadend?.(); }
};
const load = async entry => {
  const { outputFiles } = buildSync({ entryPoints: [entry], bundle: true, write: false, format: 'esm', platform: 'node', external: ['three'] });
  return import(`data:text/javascript;base64,${Buffer.from(outputFiles[0].text.replaceAll('"three"', JSON.stringify(import.meta.resolve('three')))).toString('base64')}`);
};
const { VEHICLES } = await load('src/train/catalog.ts');
const { makeVehicle, disposeVehicle } = await load('src/train/render.ts');
const directory = new URL('../godot/assets/vehicles/', import.meta.url);
await mkdir(directory, { recursive: true });
const catalog = [];
for (const definition of VEHICLES) {
  const gauges = Object.keys(definition.bogiesByGauge).map(Number);
  const entry = { ...definition, gauges, models: {} };
  delete entry.thumbnailSvg; delete entry.bogiesByGauge; delete entry.sections;
  entry.exhausts = [];
  const exhausts = (d, offset = 0) => {
    if (d.sections?.length) { for (const s of d.sections) exhausts(s.definition, offset + s.offset); return; }
    if (d.exhaust) entry.exhausts.push({ ...d.exhaust, position: [d.exhaust.position[0], d.exhaust.position[1], d.exhaust.position[2] + offset], kind: d.exhaust.kind ?? (d.runningGear === 'steam' ? 'steam' : 'diesel') });
  };
  exhausts(definition);
  for (const gauge of gauges) {
    const visual = makeVehicle(definition, gauge);
    const animations = [];
    const mark = (v, prefix = '') => {
      v.body.name = `${prefix}body`;
      v.bogies.forEach((bogie, i) => { bogie.name = `${prefix}bogie_${i}`; });
      if (v.motion) {
        v.motion.parts.forEach((part, i) => { part.name = `${prefix}motion_${i}`; });
        // Bake one full crank revolution; animation stays distance driven in Godot.
        const times = [], positions = v.motion.parts.map(() => []), rotations = v.motion.parts.map(() => []), scales = v.motion.parts.map(() => []);
        for (let frame = 0; frame <= 64; frame++) {
          if (frame) v.motion.advance(Math.PI * 2 * .65 / 64);
          times.push(frame / 64);
          v.motion.parts.forEach((p, i) => { positions[i].push(...p.position.toArray()); rotations[i].push(...p.quaternion.toArray()); scales[i].push(...p.scale.toArray()); });
        }
        v.motion.parts.forEach((p, i) => {
          animations.push(new THREE.VectorKeyframeTrack(`${p.name}.position`, times, positions[i]));
          animations.push(new THREE.QuaternionKeyframeTrack(`${p.name}.quaternion`, times, rotations[i]));
          animations.push(new THREE.VectorKeyframeTrack(`${p.name}.scale`, times, scales[i]));
        });
      }
      v.sections?.forEach((s, i) => mark(s, `section_${i}_`));
    };
    mark(visual);
    const file = `${definition.id}-${gauge}.glb`;
    const clips = animations.length ? [new THREE.AnimationClip('running_gear', 1, animations)] : [];
    const binary = await new GLTFExporter().parseAsync(visual.group, { binary: true, animations: clips, onlyVisible: true });
    await writeFile(new URL(file, directory), Buffer.from(binary));
    entry.models[gauge] = `res://assets/vehicles/${file}`;
    disposeVehicle(visual);
  }
  catalog.push(entry);
}
await writeFile(new URL('catalog.json', directory), JSON.stringify(catalog, null, 2) + '\n');
await import('./export-godot-terrain-fixtures.mjs');
console.log(`Exported ${catalog.length} vehicle definitions, all supported gauges, and terrain parity fixtures.`);
