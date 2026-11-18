/** Natural-tree placement captured directly from the original TypeScript generator. */
import { buildSync } from 'esbuild';
import { writeFile } from 'node:fs/promises';
async function load(entry) {
  const { outputFiles } = buildSync({ entryPoints: [entry], bundle: true, write: false, format: 'esm', platform: 'node' });
  return import(`data:text/javascript;base64,${Buffer.from(outputFiles[0].text).toString('base64')}`);
}
const { TerrainGenerator, DEFAULT_SETTINGS } = await load('src/terrain/generator.ts');
const { generateTrees } = await load('src/terrain/vegetation.ts');
const cases = [];
for (const overrides of [
  {}, { seed: 'forest-🌳-ç', mountainHeight: 3000, horizontalScale: .5 },
  { seed: 'another-world', mountainHeight: 400, horizontalScale: 2, seaLevel: 60 },
  { seed: 'alpine', mountainHeight: 3000, seaLevel: -30 },
]) {
  const settings = { ...DEFAULT_SETTINGS, ...overrides }, generator = new TerrainGenerator(settings);
  for (const [x,z] of [[0,0],[1,1],[-2,3],[4,-7],[-10,-10],[1000000,-1000000]])
    cases.push({settings,x,z,trees:Array.from(generateTrees(generator,x,z))});
}
await writeFile('godot/tests/vegetation-fixtures.json',JSON.stringify({cases})+'\n');
console.log(`Exported ${cases.length} natural-tree chunks (${cases.reduce((n,c)=>n+c.trees.length/7,0)} trees).`);
