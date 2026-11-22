/** Copy the authored v5 paint atlases into the native Godot catalog. No geometry rebuild. */
import { readFile, writeFile, mkdir, copyFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

const variants = [
  ['default', 'Default', 'dash9 v5 default.png'],
  ['santa-fe', 'Santa Fe', 'dash9 v5 santa fe texture.png'],
  ['vale', 'Vale', 'dash9 v5 vale.png'],
  ['ferronorte', 'Ferronorte', 'dash9 v5 ferronorte texture.png'],
];
const root = new URL('../', import.meta.url);
const catalogPath = new URL('godot/assets/vehicles/catalog.json', root);
const catalog = JSON.parse(await readFile(catalogPath, 'utf8'));
const definition = catalog.find(v => v.id === 'ge-dash9');
assert.ok(definition, 'Dash 9 catalog entry is required');
// Validate every input before changing the existing catalog/assets.
for (const [, , filename] of variants) {
  const png = await readFile(new URL(`.files/dash-9/${filename}`, root));
  assert.equal(png.subarray(1, 4).toString(), 'PNG', filename);
  assert.ok(png.readUInt32BE(16) > 0, `${filename}: nonempty atlas`);
  assert.equal(png.readUInt32BE(16), png.readUInt32BE(20), `${filename}: square atlas`);
}
await mkdir(new URL('godot/assets/vehicles/liveries/dash9/', root), { recursive: true });
definition.liveryMaterial = 'Santa Fe paintable panels';
definition.liveries = [];
for (const [id, name, filename] of variants) {
  const path = `assets/vehicles/liveries/dash9/${id}.png`;
  await copyFile(new URL(`.files/dash-9/${filename}`, root), new URL(`godot/${path}`, root));
  definition.liveries.push({ id, name, texture: `res://${path}` });
}
await writeFile(catalogPath, JSON.stringify(catalog, null, 2) + '\n');
console.log('Imported Dash 9 liveries: ' + variants.map(v => v[1]).join(', '));
