import {buildSync} from 'esbuild';
import {writeFile} from 'node:fs/promises';
import * as THREE from 'three';
const load=async path=>{const {outputFiles}=buildSync({entryPoints:[path],bundle:true,write:false,format:'esm',platform:'node',external:['three']});return import('data:text/javascript;base64,'+Buffer.from(outputFiles[0].text.replaceAll('"three"',JSON.stringify(import.meta.resolve('three')))).toString('base64'));};
const {TrackNetwork}=await load('src/track/model.ts');
const {consistPoses}=await load('src/train/consist.ts');
const {sampleTrack,snapTrainToStationEnd}=await load('src/train/placement.ts');
const {VEHICLES}=await load('src/train/catalog.ts');
const def=id=>VEHICLES.find(d=>d.id===id);
const v=(x,y,z)=>new THREE.Vector3(x,y,z);
const cases=[];
function add(name,points,ids,gauge=1435,station=false,branch=false){
 for(const id of ids)if(!def(id))throw Error("Unknown vehicle "+id);
 const graph=new TrackNetwork();const nodes=points.map(p=>graph.addNode(v(...p),gauge));
 for(let i=1;i<nodes.length;i++)graph.connect(nodes[i-1],nodes[i],gauge);
 if(branch){const extra=graph.addNode(v(-50,0,200),gauge);graph.connect(nodes[1],extra,gauge,1);}
 if(station)for(const edge of graph.segments.values())graph.addStation(edge,1);
 const edges=[...graph.segments.values()];
 const encode=p=>({center:p.center.toArray(),front:p.front.toArray(),rear:p.rear.toArray(),frontDirection:p.frontDirection.toArray(),rearDirection:p.rearDirection.toArray(),sections:p.sections?.(1).map(encode)??[]});
 const samples=[];
 for(const facing of [-1,1])for(const u of [.2,.5,.8]){
  const position={edge:edges[Math.floor(edges.length/2)].id,u,facing};
  try{samples.push({position,poses:consistPoses(graph,ids.map(def),position).map(x=>encode(x.pose)),station:snapTrainToStationEnd(graph,position,ids.map(def).reduce((s,d)=>s+d.length,0)+(ids.length-1)*.12)??null});}catch(e){samples.push({position,error:e.message});}
 }
 const walks=[];
 for(const distance of [-180,-30,0,25,170]){const position={edge:edges[0].id,u:.7,facing:1};try{const s=sampleTrack(graph,position,distance);walks.push({distance,point:s.point.toArray(),tangent:s.tangent.toArray(),position:s.position});}catch(e){walks.push({distance,error:e.message});}}
 cases.push({name,gauge,ids,nodes:[...graph.nodes.values()].map(n=>({id:n.id,position:n.position.toArray(),axis:n.axis.toArray()})),edges:[...graph.segments.values()],samples,walks});
}
add('straight passenger consist',[[0,0,0],[0,0,400]],['atsf-f7a','atsf-coach','atsf-pleasure-dome']);
add('curved graded passenger consist',[[0,0,0],[25,6,180],[65,14,400]],['atsf-f7a','atsf-coach']);
add('station across split segments',[[0,0,0],[0,0,100],[0,0,200]],['atsf-f7a'],1435,true);
add('articulated steam narrow gauge',[[0,0,0],[25,4,180],[60,9,360]],['drgw-consolidation','drgw-wood-coach'],914);
add('ambiguous junction',[[0,0,0],[0,0,100],[0,0,300]],['atsf-f7a','atsf-coach'],1435,false,true);
await writeFile('godot/tests/train-placement-fixtures.json',JSON.stringify(cases,null,2)+'\n');
console.log('Exported '+cases.length+' browser train placement scenarios.');
