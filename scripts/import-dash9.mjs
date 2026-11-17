import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { convertColladaVehicle } from './lib/collada-vehicle.mjs';

const source = new URL('../public/models/locomotives/dash-9/dash9.dae', import.meta.url);
const destination = new URL('../src/train/assets/dash9.json', import.meta.url);
const data = convertColladaVehicle(await readFile(source, 'utf8'));
const output = JSON.stringify(data) + '\n';
if (process.argv.includes('--check')) {
  if (await readFile(destination, 'utf8') !== output)
    throw Error('Dash 9 generated data is stale. Run npm run import:dash9 after re-exporting.');
  console.log('Dash 9 generated data matches the COLLADA export.');
} else {
  await mkdir(new URL('../src/train/assets/', import.meta.url), { recursive: true });
  await writeFile(destination, output);
  console.log(`Imported ${fileURLToPath(source)}\nBounds (metres): ${data.bounds.size.join(' × ')}\nBogie centers: ${data.bogieCenters} m\nAxes: SketchUp ${data.sketchupToGame}`);
}
