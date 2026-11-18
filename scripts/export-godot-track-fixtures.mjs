/** Reference meshes and sleeper batches from the unmodified browser track renderer. */
import {buildSync} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';
import path from 'node:path';
import * as THREE from 'three';
await mkdir('godot/.godot/test-build',{recursive:true});
async function source(entry,name){
 const file=path.resolve('godot/.godot/test-build/'+name+'.mjs');
 buildSync({entryPoints:[entry],outfile:file,bundle:true,format:'esm',platform:'node',external:['three','three/*']});
 return import(pathToFileURL(file));
}
const {makeTrack,disposeTrack,updateSleeperLOD}=await source('src/track/render.ts','track-reference');
const {makePlatform}=await source('src/track/stations.ts','platform-reference');
const cases=[];
for(const spec of [
 {name:'straight',gauge:1435,points:[[0,0,0],[0,0,40],[0,0,80],[0,0,120]],sides:[1]},
 {name:'graded-curve',gauge:914,points:[[-25,7,12],[40,30,20],[-15,-5,110],[50,18,160]],sides:[-1,1]},
 {name:'translated',gauge:1676,points:[[4194304,10,-4194304],[4194334,20,-4194264],[4194384,22,-4194274],[4194404,12,-4194214]],sides:[-1]},
 {name:'vertical',gauge:1435,points:[[0,0,0],[0,20,0],[0,40,0],[0,60,0]],sides:[]},
 {name:'short',gauge:610,points:[[0,0,0],[0,0,.1],[0,0,.2],[0,0,.3]],sides:[]},
 {name:'preview',gauge:914,points:[[10,0,0],[25,10,40],[50,10,70],[50,0,100]],sides:[],preview:true},
 {name:'rails-only',gauge:1435,points:[[0,0,0],[30,5,30],[40,15,60],[50,20,100]],sides:[1],railsOnly:true}
]){
 const curve=new THREE.CubicBezierCurve3(...spec.points.map(p=>new THREE.Vector3(...p)));
 const group=makeTrack(curve,spec.gauge,spec.preview??false,spec.railsOnly??false);
 const surfaces=[],sleepers=[];
 function surface(mesh,name){surfaces.push({name,positions:Array.from(mesh.geometry.attributes.position.array),normals:Array.from(mesh.geometry.attributes.normal.array),indices:Array.from(mesh.geometry.index.array)});}
 let rail=0;
 for(const mesh of group.children){
  if(mesh instanceof THREE.InstancedMesh){
   const matrix=new THREE.Matrix4(),transforms=[];
   for(let i=0;i<mesh.count;i++){mesh.getMatrixAt(i,matrix);transforms.push(matrix.toArray());}
   const lod=mesh.userData.sleeperLOD;
   sleepers.push({transforms,center:lod.center.clone().sub(group.position).toArray(),counts:lod.counts});
  }else if(mesh.material.metalness>.5){surface(mesh,rail++===0?'RailLeft':'RailRight');}
  else surface(mesh,'Ballast');
 }
 for(const side of spec.sides){const platform=makePlatform(curve,spec.gauge,side,spec.preview??false);surface(platform.children[0],'Platform_'+side);surface(platform.children[1],'PlatformEdge_'+side);disposeTrack(platform);}
 const distances=[0,170,160,134,450,430,359,1000,800,1800,1500,1400,0],lod=[];
 if(sleepers.length){const center=group.children.find(m=>m.userData.sleeperLOD).userData.sleeperLOD.center;
  for(const distance of distances){updateSleeperLOD(group,center.clone().add(new THREE.Vector3(distance,0,0)));lod.push({distance,count:group.children.find(m=>m.userData.sleeperLOD).count});}}
 cases.push({...spec,length:curve.getLength(),anchor:group.position.toArray(),surfaces,sleepers,lod});disposeTrack(group);
}
await writeFile('godot/tests/track-geometry-fixtures.json',JSON.stringify(cases)+'\n');
console.log('Exported '+cases.length+' original track cases, profiles, platforms, sleeper matrices and LOD transitions.');
