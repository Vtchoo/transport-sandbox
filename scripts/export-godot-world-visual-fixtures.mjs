import { readFile, writeFile } from 'node:fs/promises';
import { vehicleExportBrowser } from './lib/vehicle-export-browser.mjs';

const browser = await vehicleExportBrowser('scripts/lib/browser-world-visuals.ts');
try {
  const source = await readFile(new URL('../src/main.ts', import.meta.url), 'utf8');
  const vertex = source.match(/vertexShader: '([^']+)'/)[1];
  const fragment = source.match(/fragmentShader: `(varying vec3 vDirection[\s\S]*?)`,/)[1];
  const fixtures = await browser.evaluate(`window.exportModule.worldVisualFixtures(${JSON.stringify(vertex)}, ${JSON.stringify(fragment)})`);
  await writeFile(new URL('../godot/tests/world-visual-fixtures.json', import.meta.url), JSON.stringify(fixtures, null, 2) + '\n');
  console.log(`Recorded ${fixtures.length} actual browser terrain/water/sky GPU samples.`);
} finally { await browser.close(); }
