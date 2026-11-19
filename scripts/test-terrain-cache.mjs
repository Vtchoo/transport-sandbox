import assert from 'node:assert/strict';
import {test} from 'node:test';
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
const {outputFiles}=await build({stdin:{contents:`export {TerrainLodCache} from './src/terrain/cache.ts';export {TerrainWorld} from './src/terrain/world.ts';export {prepareTrees} from './src/terrain/tree-data.ts';export {generateTrees} from './src/terrain/vegetation.ts';export {DEFAULT_SETTINGS,buildChunk} from './src/terrain/generator.ts';`,resolveDir:process.cwd()},bundle:true,write:false,format:'esm',platform:'node',external:['three','three/*'],plugins:[{name:'worker-url',setup(build){build.onLoad({filter:/terrain[\\/]world\.ts$/},args=>({contents:readFileSync(args.path,'utf8').replaceAll('import.meta.url',JSON.stringify('file:///terrain/world.ts')),loader:'ts'}));}}]});
const code=outputFiles[0].text.replace(/"three(?:\/[^\"]*)?"/g,s=>JSON.stringify(import.meta.resolve(JSON.parse(s))));
const {TerrainLodCache,TerrainWorld,DEFAULT_SETTINGS,buildChunk,prepareTrees,generateTrees}=await import('data:text/javascript;base64,'+Buffer.from(code).toString('base64'));
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
  world.frameBudgetMs=10000;
  const pump=()=>{world.frameOperations=0;world.frameStarted=performance.now();world.pump();};
  const want=segments=>{world.wanted=new Map([['0,0',{x:0,z:0,segments,ring:0}]]);pump();};
  const reply=()=>{const job=worker.jobs.at(-1),data=buildChunk(world.generator,job);worker.onmessage({data:{...data,trees:new Float32Array()}});pump();};
  want(16);reply();const original=world.chunks.get('0,0').mesh.geometry;
  want(32);reply();const farther=world.chunks.get('0,0').mesh.geometry,jobs=worker.jobs.length;
  want(16);assert.equal(worker.jobs.length,jobs);assert.equal(world.chunks.get('0,0').mesh.geometry,original);
  want(32);assert.equal(worker.jobs.length,jobs);assert.equal(world.chunks.get('0,0').mesh.geometry,farther);
  const current=world.chunks.get('0,0');world.retireChunk(current);world.chunks.clear();want(16);
  assert.equal(worker.jobs.length,jobs);assert.equal(world.chunks.get('0,0').mesh.geometry,original);
  world.setContours(true);want(32);assert.equal(world.chunks.get('0,0').trees,undefined);
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


test('world budgets uploads and retirement, retains trees during edits, and bounds queued worker results',()=>{
 const previousWorker=globalThis.Worker,previousNavigator=Object.getOwnPropertyDescriptor(globalThis,'navigator'),workers=[];
 globalThis.Worker=class{jobs=[];constructor(){workers.push(this);}postMessage(data){this.jobs.push(data);}terminate(){}};
 Object.defineProperty(globalThis,'navigator',{value:{hardwareConcurrency:3},configurable:true});
 let world;
 try{
  world=new TerrainWorld(DEFAULT_SETTINGS);world.maxOperationsPerFrame=1;world.frameBudgetMs=10000;
  const worker=workers[0],tile=(x,segments=64)=>({x,z:0,segments,ring:0});
  const pump=()=>{world.frameOperations=0;world.frameStarted=performance.now();world.pump();assert.ok(world.frameOperations<=1);};
  const want=tiles=>{world.wanted=new Map(tiles.map(t=>[`${t.x},0`,t]));pump();};
  const reply=()=>{const job=worker.jobs.at(-1),data=buildChunk(world.generator,job);data.preparedTrees=prepareTrees(generateTrees(world.generator,job.x,job.z));worker.onmessage({data});};
  want([tile(0)]);reply();assert.equal(world.chunks.size,0);pump();
  const first=world.chunks.get('0,0').trees,holder=first.group.parent,attribute=first.meshes[0].instanceMatrix;
  want([tile(0,128)]);assert.equal(first.group.parent,holder);reply();
  assert.equal(first.group.parent,holder,'Worker completion does not remove the previous visual');pump();
  assert.equal(world.chunks.get('0,0').trees,first);assert.notEqual(first.group.parent,holder);
  assert.equal(first.meshes[0].instanceMatrix,attribute);
  const uploads=world.stats.treeUploads;world.setContours(true);
  want([tile(0,128)]);assert.equal(first.group.visible,false);world.setContours(false);assert.equal(first.group.visible,true);
  // Returning without changing tree data requires no new upload.
  world.retireChunk(world.chunks.get('0,0'));world.chunks.clear();want([tile(0,128)]);
  assert.equal(world.chunks.get('0,0').trees,first);assert.equal(world.stats.treeUploads,uploads);
  // Cached LODs waiting for a render budget must not spawn duplicate worker jobs.
  const jobs=worker.jobs.length;world.maxOperationsPerFrame=0;want([tile(0)]);assert.equal(worker.jobs.length,jobs);
  world.maxOperationsPerFrame=1;want([tile(0)]);
  // An edit retains the previous mesh until an up-to-date result can be committed.
  world.setEarthworks([{points:[{x:0,y:20,z:200},{x:512,y:20,z:200}]}]);
  const oldVisual=world.chunks.get('0,0').trees;reply();
  assert.equal(world.chunks.get('0,0').trees,oldVisual);assert.equal(oldVisual.group.visible,true);
  world.setEarthworks([]);pump();assert.equal(world.chunks.get('0,0').trees,oldVisual);
  reply();pump();pump();
  // With no upload budget, one worker stops dispatching after its first queued result.
  world.maxOperationsPerFrame=0;want([tile(2),tile(3),tile(4)]);reply();
  const stoppedJobs=worker.jobs.length;world.pump();
  assert.equal(world.completed.length,1);assert.equal(worker.jobs.length,stoppedJobs);
  world.maxOperationsPerFrame=1;world.frameBudgetMs=10000;
  let disposed=0;const chunk=world.chunks.get('0,0');chunk.mesh.geometry.addEventListener('dispose',()=>disposed++);
  world.releaseChunk(chunk);world.chunks.delete('0,0');assert.equal(disposed,0);pump();assert.equal(disposed,1);
 }finally{world?.dispose();globalThis.Worker=previousWorker;if(previousNavigator)Object.defineProperty(globalThis,'navigator',previousNavigator);else delete globalThis.navigator;}
});
