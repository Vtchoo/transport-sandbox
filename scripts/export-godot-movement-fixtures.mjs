import {buildSync} from 'esbuild';
import {writeFile} from 'node:fs/promises';
import * as THREE from 'three';
const load=async path=>{
  const {outputFiles}=buildSync({entryPoints:[path],bundle:true,write:false,format:'esm',platform:'node',external:['three']});
  return import('data:text/javascript;base64,'+Buffer.from(outputFiles[0].text.replaceAll('"three"',JSON.stringify(import.meta.resolve('three')))).toString('base64'));
};
const {TrackNetwork}=await load('src/track/model.ts');
const {TrainRoute}=await load('src/train/route.ts');
const {physicsStep}=await load('src/train/physics.ts');
const {VEHICLES}=await load('src/train/catalog.ts');
const routes=[];
for(const seed of [1,42,65535]){
  const graph=new TrackNetwork();
  const nodes=[[0,0,0],[0,0,200],[0,0,500],[120,8,500],[-100,0,0]].map(p=>graph.addNode(new THREE.Vector3(...p),1435));
  graph.connect(nodes[0],nodes[1],1435);
  graph.connect(nodes[1],nodes[2],1435,1);
  graph.connect(nodes[1],nodes[3],1435,1);
  // This connection is on the incoming side and must never be chosen as a U-turn.
  graph.connect(nodes[1],nodes[4],1435,-1);
  const position={edge:1,u:.5,facing:1};
  const route=new TrainRoute(graph,position,seed);
  const samples=[];
  for(const distance of [50,150,250,20,-80,300]){
    const s=route.sample(position,distance);
    const back=route.sample(s.position,-distance);
    samples.push({distance,position:s.position,point:s.point.toArray(),back:back.point.toArray(),radius:route.turnRadius(s.position)});
  }
  routes.push({seed,nodes:[...graph.nodes.values()].map(n=>({position:n.position.toArray(),axis:n.axis.toArray()})),edges:[...graph.segments.values()],position,samples,clearance:route.clearance(position,2000)});
}
const physics=[];
for(const mode of ['ho','realistic'])for(const grade of [0,.04,-.03]){
  const ids=['ge-809','atsf-coach','atsf-coach'];
  let speed=0,distance=0;
  for(let i=0;i<1800;i++){
    const next=physicsStep(ids.map(id=>VEHICLES.find(d=>d.id===id)),speed,i<1200,grade,mode,1/60,24);
    speed=next.speed;distance+=next.distance;
    if([599,1199,1799].includes(i))physics.push({mode,grade,ids,step:i+1,speed,distance});
  }
}
await writeFile('godot/tests/train-movement-fixtures.json',JSON.stringify({routes,physics},null,2)+'\n');
console.log(`Exported ${routes.length} browser junction routes and ${physics.length} physics trajectory checkpoints.`);
