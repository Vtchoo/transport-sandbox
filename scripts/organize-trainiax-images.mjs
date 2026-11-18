import { readFile, readdir, mkdir, copyFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

/** Offline only: select one largest local drawing per indexed locomotive type. */
const root = fileURLToPath(new URL('../', import.meta.url));
const destination = path.join(root, '.files', 'imagedump');
const source = process.argv[2] && path.resolve(process.argv[2]);
if (!source) throw Error('Usage: node scripts/organize-trainiax-images.mjs <local-drawing-folder>');
if (source === destination || source.startsWith(destination + path.sep))
  throw Error('Choose a source folder outside .files/imagedump.');
const index = JSON.parse(await readFile(path.join(destination, 'locomotive-index.json'), 'utf8'));
const files = [];
async function scan(folder) {
  for (const entry of await readdir(folder, { withFileTypes: true })) {
    const location = path.join(folder, entry.name);
    if (path.resolve(location) === destination) continue;
    if (entry.isDirectory()) await scan(location);
    else if (entry.isFile() && /\.(png|gif)$/i.test(entry.name)) files.push(location);
  }
}
function dimensions(data) {
  if (data.length >= 24 && data.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])))
    return [data.readUInt32BE(16), data.readUInt32BE(20)];
  if (data.length >= 10 && /^GIF8[79]a$/.test(data.toString('ascii', 0, 6)))
    return [data.readUInt16LE(6), data.readUInt16LE(8)];
  return undefined;
}
await scan(source);
const candidates = [];
for (const location of files.sort()) {
  const size = dimensions(await readFile(location));
  if (size?.every(n => n > 0)) candidates.push({ location, filename: path.basename(location), width: size[0], height: size[1] });
}
const manifest = [];
const slug = text => text.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
for (const model of index.locomotives) {
  // Trainiax drawing names encode scale, manufacturer and model. Require a
  // token boundary, so GP38 does not accidentally match GP38-2.
  const key = `${slug(model.manufacturer)}-${slug(model.model)}`;
  const matching = candidates.filter(file => {
    const name = file.filename.toLowerCase().replace(/^r?(?:18|36|55)-/, '').replace(/\.(?:png|gif)$/, '');
    if (!(name === key || name.startsWith(key + '-'))) return false;
    const longerModel = index.locomotives.some(other => {
      const otherKey = `${slug(other.manufacturer)}-${slug(other.model)}`;
      return otherKey !== key && otherKey.startsWith(key + '-') && (name === otherKey || name.startsWith(otherKey + '-'));
    });
    return !longerModel;
  }).sort((a, b) => b.width * b.height - a.width * a.height || a.filename.localeCompare(b.filename));
  const selected = matching[0];
  if (!selected) { manifest.push({ ...model, status: 'no-matching-local-image' }); continue; }
  const modelFolder = path.join(destination, slug(model.manufacturer + '-' + model.model) + (model.rebuilt === 'Y' ? '-rebuilt' : ''));
  await mkdir(modelFolder, { recursive: true });
  const target = path.join(modelFolder, selected.filename);
  await copyFile(selected.location, target);
  manifest.push({ ...model, status: 'copied', file: path.relative(destination, target).replaceAll('\\', '/'),
    width: selected.width, height: selected.height, sourceFile: selected.location });
}
await writeFile(path.join(destination, 'local-image-manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
console.log(`Copied ${manifest.filter(item => item.status === 'copied').length} of ${manifest.length} locomotive types. See local-image-manifest.json for unmatched types.`);
