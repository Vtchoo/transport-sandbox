import assert from 'node:assert/strict';
import {test} from 'node:test';
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
const {outputFiles}=await build({stdin:{contents:`export {TerrainLodCache} from './src/terrain/cache.ts';export {TerrainWorld} from './src/terrain/world.ts';export {DEFAULT_SETTINGS,buildChunk} from './src/terrain/generator.ts';`,resolveDir:process.cwd()},bundle:true,write:false,format:'esm',platform:'node',external:['three','three/*'],plugins:[{name:'worker-url',setup(build){build.onLoad({filter:/terrain[\\/]world\.ts$/},args=>({contents:readFileSync(args.path,'utf8').replaceAll('import.meta.url',JSON.stringify('file:///terrain/world.ts')),loader:'ts'}));}}]});
const code=outputFiles[0].text.replace(/"three(?:\/[^\"]*)?"/g,s=>JSON.stringify(import.meta.resolve(JSON.parse(s))));
const {TerrainLodCache,TerrainWorld,DEFAULT_SETTINGS,buildChunk}=await import('data:text/javascript;base64,'+Buffer.from(code).toString('base64'));
test('LRU transfers ownership, enforces memory/entry bounds, invalidates all affected LODs',()=>{
 const released=[],cache=new TerrainLodCache(entry=>released.push(entry));cache.budgetBytes=20;
 const entry=(x,segments=16,bytes=10)=>({x,z:0,segments,revision:0,bytes});
 const a=entry(0),b=entry(1),c=entry(2);cache.store(a);cache.store(b);assert.equal(cache.take(0,0,16,0),a);cache.store(a);cache.store(c);
 assert.deepEqual(released,[b]);assert.equal(cache.bytes,20);
 assert.equal(cache.take(0,0,16,0),a);assert.equal(cache.take(2,0,16,0),c);cache.store(a);cache.store(entry(0,64));
 cache.advanceRevision(1,x=>x===0);assert.equal(cache.count,0);assert.equal(cache.bytes,0);
 cache.store(entry(3));cache.advanceRevision(2,()=>false);assert.ok(cache.take(3,0,16,2));
 cache.store(entry(4));cache.budgetBytes=0;assert.equal(cache.count,0);assert.equal(cache.bytes,0);
 cache.budgetBytes=10000;for(let i=0;i<300;i++)cache.store(entry(i,16,1));assert.equal(cache.count,256);cache.clear();assert.equal(cache.bytes,0);
});
test('world reuses geometry on LOD return/unload and rejects cached or pending geometry after edits',()=>{
 const previousWorker=globalThis.Worker,previousNavigator=Object.getOwnPropertyDescriptor(globalThis,'navigator');
 const workers=[];
 globalThis.Worker=class{jobs=[];constructor(){workers.push(this);}postMessage(data){this.jobs.push(data);}terminate(){}};
 Object.defineProperty(globalThis,'navigator',{value:{hardwareConcurrency:3},configurable:true});
 let world;
 try{
  world=new TerrainWorld(DEFAULT_SETTINGS);const worker=workers[0];
  const want=segments=>{world.wanted=new Map([['0,0',{x:0,z:0,segments,ring:0}]]);world.pump();};
  const reply=()=>{const job=worker.jobs.at(-1),data=buildChunk(world.generator,job);worker.onmessage({data:{...data,trees:new Float32Array()}});};
  want(16);reply();const original=world.chunks.get('0,0').mesh.geometry;
  want(32);reply();const farther=world.chunks.get('0,0').mesh.geometry,jobs=worker.jobs.length;
  want(16);assert.equal(worker.jobs.length,jobs);assert.equal(world.chunks.get('0,0').mesh.geometry,original);
  want(32);assert.equal(worker.jobs.length,jobs);assert.equal(world.chunks.get('0,0').mesh.geometry,farther);
  const current=world.chunks.get('0,0');world.retireChunk(current);world.chunks.clear();want(16);
  assert.equal(worker.jobs.length,jobs);assert.equal(world.chunks.get('0,0').mesh.geometry,original);
  world.setContours(true);want(32);assert.equal(world.chunks.get('0,0').trees.group.visible,false);
  // Natural geometry far from the earthwork stays reusable after the revision advances.
  world.setEarthworks([{points:[{x:10000,y:20,z:10000},{x:10100,y:20,z:10000}]}]);
  want(16);assert.equal(world.chunks.get('0,0').mesh.geometry,original);assert.equal(worker.jobs.length,jobs);
  world.setEarthworks([{points:[{x:0,y:20,z:200},{x:512,y:20,z:200}]}]);
  assert.ok(worker.jobs.length>jobs);assert.equal(world.cache.count,0);
  // Remove the earthwork while an old request is running. Its reply must be discarded.
  const old=worker.jobs.at(-1);world.setEarthworks([]);
  const data=buildChunk(world.generator,{...old,earthPaths:undefined});worker.onmessage({data});
  assert.equal(world.chunks.get('0,0').mesh.geometry,original);assert.notEqual(worker.jobs.at(-1).revision,old.revision);
  assert.ok(world.stats.cacheHits>=5);
 }finally{world?.dispose();globalThis.Worker=previousWorker;if(previousNavigator)Object.defineProperty(globalThis,'navigator',previousNavigator);else delete globalThis.navigator;}
});
