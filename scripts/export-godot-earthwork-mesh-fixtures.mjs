/** Adaptive cut/fill buffers from the original browser mesher, including a chunk boundary. */
import {buildSync} from 'esbuild';
import {writeFile} from 'node:fs/promises';
const {outputFiles}=buildSync({stdin:{contents:`export {TerrainGenerator,DEFAULT_SETTINGS,buildChunk} from './src/terrain/generator.ts';export {EarthworkField,prepareEarthPaths} from './src/track/earthworks.ts';`,resolveDir:process.cwd()},bundle:true,write:false,format:'esm',platform:'node'});
const {TerrainGenerator,DEFAULT_SETTINGS,buildChunk,EarthworkField,prepareEarthPaths}=await import(`data:text/javascript;base64,${Buffer.from(outputFiles[0].text).toString('base64')}`);
const generator=new TerrainGenerator(DEFAULT_SETTINGS);
const paths=[{points:Array.from({length:41},(_,i)=>{
  const x=480+i*2,z=600+i*.5;return{x,z,y:generator.baseHeight(x,z)+10};
})},{points:Array.from({length:31},(_,i)=>{
  const x=700+i*2,z=710+i*.6;return{x,z,y:generator.baseHeight(x,z)-10};
})}];
generator.earthworks=new EarthworkField(prepareEarthPaths(paths,(x,z)=>generator.baseHeight(x,z),DEFAULT_SETTINGS.mountainHeight));
const chunks=[];
for(const [x,z,segments] of [[0,1,128],[1,1,64],[1,1,16]]){
 const data=buildChunk(generator,{id:1,x,z,segments,settings:DEFAULT_SETTINGS});
 const count=data.positions.length/3;
 const samples=Array.from(new Set([0,count-1,...Array.from({length:1024},(_,i)=>Math.floor(i*(count-1)/1023))]));
 let hash=2166136261;
 // Godot has reversed front-face winding.
 for(let i=0;i<data.indices.length;i+=3)for(const offset of [0,2,1])hash=Math.imul(hash^data.indices[i+offset],16777619)>>>0;
 chunks.push({x,z,segments,vertexCount:count,indexCount:data.indices.length,indicesHash:hash,
 samples:samples.map(index=>({index,position:Array.from(data.positions.slice(index*3,index*3+3)),normal:Array.from(data.normals.slice(index*3,index*3+3)),color:Array.from(data.colors.slice(index*3,index*3+3))}))});
}
await writeFile('godot/tests/earthwork-mesh-fixtures.json',JSON.stringify({settings:DEFAULT_SETTINGS,paths,chunks})+'\n');
console.log('Exported original adaptive meshes: '+chunks.map(c=>`${c.x},${c.z}/${c.segments}: ${c.vertexCount} vertices`).join('; '));
