/** Chunk buffers and LOD decisions sampled from the unmodified browser implementation. */
import { buildSync } from 'esbuild';
import { writeFile } from 'node:fs/promises';
async function load(entry) {
  const { outputFiles } = buildSync({ entryPoints: [entry], bundle: true, write: false, format: 'esm', platform: 'node' });
  return import(`data:text/javascript;base64,${Buffer.from(outputFiles[0].text).toString('base64')}`);
}
const { TerrainGenerator, DEFAULT_SETTINGS, buildChunk } = await load('src/terrain/generator.ts');
const { wantedTiles } = await load('src/terrain/lod.ts');
const chunks = [];
for (const [x, z, segments] of [[1,1,128],[-2,3,64],[4,-7,32],[-10,-10,16]]) {
  const request = {id:1,x,z,segments,settings:DEFAULT_SETTINGS};
  const data = buildChunk(new TerrainGenerator(DEFAULT_SETTINGS),request);
  const count = data.positions.length / 3;
  const samples = Array.from(new Set([0,1,segments,(segments+1)**2-1,...Array.from({length:80},(_,i)=>Math.floor(i*(count-1)/79))]));
  const triangles = Array.from({length:80},(_,i)=>Math.floor(i*(data.indices.length/3-1)/79));
  chunks.push({x,z,segments,vertexCount:count,indexCount:data.indices.length,
    triangles:triangles.map(index=>({index,vertices:Array.from(data.indices.slice(index*3,index*3+3))})),
    samples:samples.map(index=>({index,position:Array.from(data.positions.slice(index*3,index*3+3)),normal:Array.from(data.normals.slice(index*3,index*3+3)),color:Array.from(data.colors.slice(index*3,index*3+3))}))});
}
const generator = new TerrainGenerator(DEFAULT_SETTINGS);
const camera = {x:600,y:1100,z:900};
const tiles = wantedTiles(camera.x,camera.z,camera.y,(x,z)=>generator.height(x,z));
await writeFile('godot/tests/terrain-chunk-fixtures.json',JSON.stringify({settings:DEFAULT_SETTINGS,chunks,camera,tiles},null,2)+'\n');
console.log('Exported four chunk LODs and 441 tile decisions from TypeScript.');
