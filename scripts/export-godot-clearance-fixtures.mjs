import { buildSync } from 'esbuild';
import { mkdir, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
import * as THREE from 'three';
const entry = path.resolve('godot/.godot/test-build/clearance-source.mjs');
await mkdir(path.dirname(entry), {recursive:true});
buildSync({stdin:{contents:`export {prepareEarthPaths,EarthworkField} from './src/track/earthworks.ts'; export {TerrainGenerator,DEFAULT_SETTINGS} from './src/terrain/generator.ts'; export {generateTrees} from './src/terrain/vegetation.ts'; export {trackFrame} from './src/track/model.ts';`,resolveDir:process.cwd()},outfile:entry,bundle:true,format:'esm',platform:'node',external:['three','three/*']});
const {prepareEarthPaths,EarthworkField,TerrainGenerator,DEFAULT_SETTINGS,generateTrees,trackFrame} = await import(pathToFileURL(entry));
const cases=[];
for(const ground of ['flat','valley','seeded']) {
  const generator=new TerrainGenerator(DEFAULT_SETTINGS);
  const natural=ground==='flat'?()=>20:ground==='valley'?(x,z)=>20+Math.sin(x/15)*12+Math.cos(z/31)*18:(x,z)=>generator.baseHeight(x,z);
  const controls=ground==='seeded'?[[550,90,550],[600,120,650],[740,60,550],[800,80,720]]:[[0,30,0],[20,40,35],[60,10,-20],[100,25,40]];
  const curve=new THREE.CubicBezierCurve3(...controls.map(p=>new THREE.Vector3(...p)));curve.arcLengthDivisions=512;
  const raw=curve.getSpacedPoints(Math.max(2,Math.ceil(curve.getLength()/2))).map((p,i,points)=>{
    const bed=p.clone().addScaledVector(trackFrame(curve.getTangentAt(i/(points.length-1))).up,-.65);
    return {x:bed.x,y:bed.y+.65,z:bed.z};
  });
  // Crossings, degenerate edges, different grades and multiple BVH leaves.
  const paths=[{points:raw},{points:[{x:10,y:10,z:-60},{x:10,y:10,z:-60},{x:10,y:50,z:90}]}];
  const prepared=prepareEarthPaths(paths,natural,DEFAULT_SETTINGS.mountainHeight), field=new EarthworkField(prepared);
  const queries=[];
  for(let i=0;i<raw.length;i+=5)for(const offset of [-90,-20,-8,-2,0,2,8,20,90]){
    const x=raw[i].x+offset,z=raw[i].z+offset*.3,base=natural(x,z),hit=field.nearest(x,z);
    queries.push({x,z,natural:base,...hit,height:field.height(x,z,base),clears:hit.distance<8||(Number.isFinite(hit.width)?hit.distance<hit.width+4:Math.abs(field.height(x,z,base)-base)>.05)});
  }
  cases.push({ground,controls,raw,paths,prepared,queries});
}
const generator=new TerrainGenerator(DEFAULT_SETTINGS),natural=generateTrees(generator,1,1);
const paths=[];
for(let i=0;i<Math.min(natural.length,7*40);i+=7*4){
 const x=512+natural[i],z=512+natural[i+2],y=natural[i+1]+(i%3===0?25:-15);
 paths.push({points:[{x:x-30,y,z},{x:x+30,y,z}]});
}
const prepared=prepareEarthPaths(paths,(x,z)=>generator.baseHeight(x,z),DEFAULT_SETTINGS.mountainHeight);
generator.earthworks=new EarthworkField(prepared);
const cleared=generateTrees(generator,1,1);
await writeFile('godot/tests/clearance-fixtures.json',JSON.stringify({settings:DEFAULT_SETTINGS,cases,vegetation:{x:1,z:1,paths,prepared,natural:Array.from(natural),cleared:Array.from(cleared)}})+'\n');
console.log(`Exported ${cases.length} original earthwork fields and tree clearance (${natural.length/7} -> ${cleared.length/7}).`);
