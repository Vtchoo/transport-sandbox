/** Rebuild the native Godot catalog from the browser game's source of truth. */
import { buildSync } from 'esbuild';
import { mkdir, writeFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import { vehicleExportBrowser } from './lib/vehicle-export-browser.mjs';

const load = async entry => {
  const { outputFiles } = buildSync({ entryPoints: [entry], bundle: true, write: false, format: 'esm', platform: 'node', external: ['three'] });
  return import(`data:text/javascript;base64,${Buffer.from(outputFiles[0].text.replaceAll('"three"', JSON.stringify(import.meta.resolve('three')))).toString('base64')}`);
};
const { VEHICLES } = await load('src/train/catalog.ts');
const directory = new URL('../godot/assets/vehicles/', import.meta.url);
await mkdir(directory, { recursive: true });
const catalog = [], appearance = [];
const browser = await vehicleExportBrowser();
try {
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
      const result = await browser.exportVehicle(definition.id, gauge);
      const binary = Buffer.from(result.base64, 'base64');
      const jsonLength = binary.readUInt32LE(12);
      const gltf = JSON.parse(binary.subarray(20, 20 + jsonLength).toString('utf8'));
      const signs = (gltf.nodes ?? []).filter(node => node.name?.startsWith('lettering_'));
      assert.equal(signs.length, result.lettering, `${definition.id}: all source lettering was exported`);
      for (const sign of signs) {
        const mesh = gltf.meshes[sign.mesh];
        for (const primitive of mesh.primitives) {
          const material = gltf.materials[primitive.material];
          assert.ok(material.pbrMetallicRoughness.baseColorTexture, `${definition.id}: lettering has its texture`);
          assert.equal(material.alphaMode, 'BLEND');
          assert.ok(material.extensions.KHR_materials_unlit);
          assert.ok(primitive.attributes.TEXCOORD_0 !== undefined);
        }
      }
      const file = `${definition.id}-${gauge}.glb`;
      await writeFile(new URL(file, directory), binary);
      appearance.push({ id: definition.id, gauge, lettering: result.lettering });
      entry.models[gauge] = `res://assets/vehicles/${file}`;
    }
    catalog.push(entry);
  }
} finally { await browser.close(); }
assert.ok(appearance.some(model => model.lettering > 0), 'Browser lettering must not silently disappear');
await writeFile(new URL('../../tests/vehicle-appearance-fixtures.json', directory), JSON.stringify(appearance, null, 2) + '\n');
await writeFile(new URL('catalog.json', directory), JSON.stringify(catalog, null, 2) + '\n');
await import('./export-godot-train-metadata.mjs');
await import('./export-godot-terrain-fixtures.mjs');
console.log(`Exported ${catalog.length} vehicle definitions, all supported gauges, including browser lettering, and terrain parity fixtures.`);
