/** Reference numerical samples and startup cameras from the original browser implementation. */
import { buildSync } from 'esbuild';
import { mkdir, writeFile } from 'node:fs/promises';
const load = async entry => {
  const { outputFiles } = buildSync({ entryPoints: [entry], bundle: true, write: false, format: 'esm', platform: 'node', external: ['three'] });
  return import(`data:text/javascript;base64,${Buffer.from(outputFiles[0].text.replaceAll('"three"', JSON.stringify(import.meta.resolve('three')))).toString('base64')}`);
};
const { TerrainGenerator, DEFAULT_SETTINGS } = await load('src/terrain/generator.ts');
const { CameraRig } = await load('src/camera.ts');
const settingsList = [
  DEFAULT_SETTINGS,
  { ...DEFAULT_SETTINGS, seed: 'native-startup-check', mountainHeight: 800 },
  { ...DEFAULT_SETTINGS, seed: 'rio-grande', mountainHeight: 2600, horizontalScale: 2 },
  { ...DEFAULT_SETTINGS, seed: '铁路🚂', mountainHeight: 400, horizontalScale: .5 },
  { ...DEFAULT_SETTINGS, seed: '', mountainHeight: 0 },
];
const locations = [[0,0], [100,-100], [-1024,2048], [10000,10000], [-25000,19231], [600,900], [2000,0], [.125,-.375], [-512,-512], [65536,-65536]];
const cases = settingsList.map(settings => {
  const generator = new TerrainGenerator(settings);
  const ground = (x,z) => Math.max(settings.seaLevel, generator.height(x,z));
  const camera = new CameraRig(); camera.reset(600, 900, ground);
  return { settings, seedHash: generator.seed, samples: locations.map(([x,z]) => ({x,z,height:generator.height(x,z)})),
    camera: { target: camera.target.toArray(), position: camera.position.toArray(), yaw: camera.yaw, pitch: camera.pitch, distance: camera.distance } };
});
await mkdir(new URL('../godot/tests/', import.meta.url), {recursive:true});
await writeFile(new URL('../godot/tests/terrain-fixtures.json', import.meta.url), JSON.stringify({...cases[0], cases}, null, 2) + '\n');
console.log(`Wrote ${cases.length * locations.length} terrain samples and ${cases.length} startup camera fixtures from TypeScript.`);
