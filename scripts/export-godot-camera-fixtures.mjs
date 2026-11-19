import {buildSync} from 'esbuild';
import {writeFile} from 'node:fs/promises';
import * as THREE from 'three';
const {outputFiles}=buildSync({entryPoints:['src/camera.ts'],bundle:true,write:false,format:'esm',platform:'node',external:['three']});
const {CameraRig}=await import('data:text/javascript;base64,'+Buffer.from(outputFiles[0].text.replaceAll('"three"',JSON.stringify(import.meta.resolve('three')))).toString('base64'));
const ground=(x,z)=>100+Math.sin(x*.003)*40+Math.cos(z*.002)*20;
const scenarios=[];
for(const [x,z] of [[600,900],[-1800,-2600],[1000000000.125,-1000000000.375]]){
  const rig=new CameraRig();
  const actions=[{kind:'reset',x,z},
    {kind:'look',x:120,y:-65},{kind:'pan',x:33,y:-18},{kind:'scroll',value:-120},
    {kind:'update',keys:['KeyW','KeyD','ShiftLeft'],dt:.05},{kind:'update',keys:['KeyQ'],dt:.04},
    {kind:'update',keys:['ArrowDown','ArrowLeft'],dt:.05},{kind:'update',keys:['KeyW'],dt:.05,blocked:true},
    {kind:'mode',free:true},{kind:'look',x:-73,y:52},{kind:'scroll',value:-600},
    {kind:'update',keys:['KeyW','Space','ShiftLeft'],dt:.05},
    {kind:'follow',point:[x+10,130,z+30]},{kind:'updateFollow',point:[x+15,140,z+45]},
    {kind:'look',x:30,y:20},{kind:'scroll',value:200},
    {kind:'update',keys:['KeyE'],dt:.05},{kind:'mode',free:false},
    {kind:'follow',point:[x+20,150,z+80]},{kind:'updateFollow',point:[x+35,155,z+100]},
    {kind:'update',keys:['KeyQ'],dt:.02},{kind:'pan',x:1,y:0},
    {kind:'look',x:0,y:10000},{kind:'look',x:0,y:-10000},{kind:'scroll',value:100000},{kind:'scroll',value:-100000}];
  for(const a of actions){
    if(a.kind==='reset')rig.reset(a.x,a.z,ground);
    if(a.kind==='look')rig.look(a.x,a.y,ground);
    if(a.kind==='pan')rig.pan(a.x,a.y,ground);
    if(a.kind==='scroll')rig.scroll(a.value,ground);
    if(a.kind==='mode')rig.setMode(a.free?'free':'overlook',ground);
    if(a.kind==='update')rig.update(new Set(a.keys),a.dt,ground,()=>!a.blocked);
    if(a.kind==='follow')rig.followTrain(7,new THREE.Vector3(...a.point),ground);
    if(a.kind==='updateFollow')rig.updateFollow(new THREE.Vector3(...a.point),ground);
    a.expected={position:rig.position.toArray(),target:rig.target.toArray(),yaw:rig.yaw,pitch:rig.pitch,distance:rig.distance,speed:rig.speed,free:rig.mode==='free',following:rig.following!==undefined};
  }
  scenarios.push(actions);
}
await writeFile('godot/tests/camera-fixtures.json',JSON.stringify(scenarios,null,2)+'\n');
console.log('Exported browser camera sequences, including negative and billion-metre coordinates.');
