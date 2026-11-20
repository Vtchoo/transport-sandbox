import { buildSync } from 'esbuild';
import { readFile, writeFile } from 'node:fs/promises';
const { outputFiles } = buildSync({ entryPoints: ['src/train/physics.ts'], bundle: true, write: false, format: 'esm', platform: 'node' });
const { physicsStep } = await import('data:text/javascript;base64,' + Buffer.from(outputFiles[0].text).toString('base64'));
const path = 'godot/tests/physics-fixtures.json';
const cases = JSON.parse(await readFile(path, 'utf8'));
const cars = [
  { physics: { massKg: 120000, powerW: 3000000, tractiveEffortN: 300000, maxSpeedMps: 40 } },
  { physics: { massKg: 40000, powerW: 0, tractiveEffortN: 0, maxSpeedMps: 50 } },
];
for (const test of cases)
  test.result = physicsStep(cars, test.speed, test.started, test.grade, test.ho ? 'ho' : 'realistic', test.dt, test.limit);
await writeFile(path, JSON.stringify(cases, null, 2) + '\n');
console.log(`Exported ${cases.length} browser physics cases.`);
