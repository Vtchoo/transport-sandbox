import assert from 'node:assert/strict';
import {test} from 'node:test';
import {buildSync} from 'esbuild';
import * as THREE from 'three';
const load=async entry=>{const {outputFiles}=buildSync({entryPoints:[entry],bundle:true,write:false,format:'esm',platform:'node',external:['three']});const code=outputFiles[0].text.replace(/"three(?:\/[^\"]*)?"/g,spec=>JSON.stringify(import.meta.resolve(JSON.parse(spec))));return import('data:text/javascript;base64,'+Buffer.from(code).toString('base64'));};
const {VehicleInstances}=await load('src/train/instances.ts');
const {makeVehicle,disposeVehicle,tintVehicle}=await load('src/train/render.ts');
const {VEHICLES}=await load('src/train/catalog.ts');
const {advanceVehicle}=await load('src/train/motion.ts');
function fixture(run){const instances=new VehicleInstances(),scene=new THREE.Scene();scene.add(instances.group);const visuals=[];const create=definition=>{const visual=makeVehicle(definition,1435);visuals.push(visual);scene.add(visual.group);return visual;};try{run({instances,scene,create});}finally{instances.dispose();visuals.forEach(disposeVehicle);}}

test('identical cars share a constant number of material batches with independent body and bogie instances',()=>fixture(({instances,create})=>{
 const first=create(VEHICLES[1]);instances.sync([first]);const count=instances.group.children.length;
 const cars=[first,...Array.from({length:19},()=>create(VEHICLES[1]))];cars.forEach((v,i)=>v.group.position.z=5+i*20);
 instances.sync(cars);assert.equal(instances.group.children.length,count);
 let sourceMeshes=0;cars.forEach(v=>v.group.traverse(o=>{if(o.isMesh)sourceMeshes++}));assert.ok(count<sourceMeshes/50);
 assert.ok(instances.group.children.every(mesh=>mesh.isInstancedMesh&&mesh.count===(mesh.name.endsWith(':body')?20:40)));
 assert.ok(cars.every(v=>!v.group.visible));
 const ray=new THREE.Raycaster(new THREE.Vector3(0,10,5),new THREE.Vector3(0,-1,0));assert.ok(ray.intersectObjects(cars.map(v=>v.group),true).length>0,'Source hierarchy remains pickable');
}));

test('moving steam parts update independent instance matrices without rebuilding shared templates or static bodies',()=>fixture(({instances,create})=>{
 const definition=VEHICLES.find(def=>def.id==='drgw-consolidation');
 const a=create(definition),b=create(definition);b.group.position.z=30;
 instances.sync([a,b]);
 const mesh=instances.group.children.find(mesh=>mesh.name.endsWith(':moving:1'));
 assert.ok(mesh&&mesh.count===2);
 const geometry=mesh.geometry,material=mesh.material,first=new THREE.Matrix4(),next=new THREE.Matrix4(),other=new THREE.Matrix4();
 mesh.getMatrixAt(0,first);
 const staticBody=instances.group.children.find(mesh=>mesh.name.endsWith(':body')),staticMatrix=new THREE.Matrix4();staticBody.getMatrixAt(0,staticMatrix);
 advanceVehicle(a,.4);advanceVehicle(b,1.1);instances.sync([a,b]);
 assert.equal(mesh.geometry,geometry);assert.equal(mesh.material,material);
 mesh.getMatrixAt(0,next);mesh.getMatrixAt(1,other);
 assert.equal(first.equals(next),false);assert.equal(next.equals(other),false);
 const unchanged=new THREE.Matrix4();staticBody.getMatrixAt(0,unchanged);assert.ok(unchanged.equals(staticMatrix));
 const count=instances.group.children.length;
 const more=Array.from({length:10},(_,i)=>{const v=create(definition);v.group.position.z=50+i*25;advanceVehicle(v,i*.2);return v});
 instances.sync([a,b,...more]);assert.equal(instances.group.children.length,count);
 assert.equal(instances.group.children.find(mesh=>mesh.name.endsWith(':moving:1')).count,12);
}));

test('body and bogie transforms preserve curves, reversal and precision across floating-origin changes',()=>fixture(({instances,scene,create})=>{
 const car=create(VEHICLES[3]);car.group.position.set(1000000.123,10,-1000000.456);car.body.quaternion.setFromAxisAngle(new THREE.Vector3(0,1,0),1.3);
 car.bogies[0].position.set(1,.1,6);car.bogies[0].rotation.y=.2;car.bogies[1].rotation.y=-.4;
 const matrix=new THREE.Matrix4(),expected=new THREE.Vector3(),actual=new THREE.Vector3();
 for(const origin of [new THREE.Vector3(-1000000,0,1000000),new THREE.Vector3(-1000512,0,999488)]){
  scene.position.copy(origin);instances.sync([car]);
  for(const mesh of instances.group.children){const components=mesh.name.endsWith(':body')?[car.body]:car.bogies;
   components.forEach((component,i)=>{mesh.getMatrixAt(i,matrix);assert.ok(Math.abs(matrix.elements[12])<512.01&&Math.abs(matrix.elements[14])<512.01);matrix.premultiply(mesh.matrixWorld);expected.setFromMatrixPosition(component.matrixWorld);actual.setFromMatrixPosition(matrix);assert.ok(expected.distanceTo(actual)<.0001);});
  }
 }
}));

test('spatial cells share template geometry and removal cannot dispose surviving car resources',()=>fixture(({instances,create})=>{
 const a=create(VEHICLES[1]),b=create(VEHICLES[1]);b.group.position.x=600;instances.sync([a,b]);
 const meshes=instances.group.children,perCell=meshes.length/2;assert.ok(perCell>0);
 assert.equal(meshes[0].geometry,meshes[perCell].geometry);assert.equal(meshes[0].material,meshes[perCell].material);
 const geometry=meshes[0].geometry,material=meshes[0].material;let geometryDisposals=0,materialDisposals=0;
 geometry.addEventListener('dispose',()=>geometryDisposals++);material.addEventListener('dispose',()=>materialDisposals++);
 disposeVehicle(a);instances.sync([b]);assert.equal(geometryDisposals,0);assert.equal(materialDisposals,0);assert.equal(instances.group.children.length,perCell);
 assert.equal(instances.group.children[0].geometry,geometry);
 instances.sync([]);assert.equal(geometryDisposals,1);assert.equal(materialDisposals,1);assert.equal(instances.group.children.length,0);assert.equal(b.group.visible,true);
}));

test('every model batches without changing bounds or allowing preview tint to affect placed instances',()=>fixture(({instances,create})=>{
 for(const definition of VEHICLES){const car=create(definition);car.group.position.set(25,2,30);const expected=new THREE.Box3().setFromObject(car.group);instances.sync([car]);const actual=new THREE.Box3().setFromObject(instances.group);assert.ok(expected.min.distanceTo(actual.min)<.001);assert.ok(expected.max.distanceTo(actual.max)<.001);
  const preview=create(definition);tintVehicle(preview,false);assert.ok(instances.group.children.every(m=>m.material.opacity===1));instances.sync([]);assert.equal(car.group.visible,true);
 }
}));
test('mirrored details retain front-face winding when their transforms are baked into a batch',()=>fixture(({instances,create})=>{
 const car=create(VEHICLES[0]),material=new THREE.MeshBasicMaterial({color:0x123456}),detail=new THREE.Mesh(new THREE.PlaneGeometry(1,1),material);
 detail.scale.x=-1;car.body.add(detail);instances.sync([car]);
 const mesh=instances.group.children.find(m=>m.material.color?.getHex()===0x123456),positions=mesh.geometry.getAttribute('position'),normals=mesh.geometry.getAttribute('normal');
 const a=new THREE.Vector3().fromBufferAttribute(positions,0),b=new THREE.Vector3().fromBufferAttribute(positions,1),c=new THREE.Vector3().fromBufferAttribute(positions,2),normal=new THREE.Vector3().fromBufferAttribute(normals,0);
 assert.ok(b.sub(a).cross(c.sub(a)).dot(normal)>0);
}));

test('permanently coupled sections keep their own transforms and small instance translations far from the origin',()=>fixture(({instances,scene,create})=>{
 const car=create(VEHICLES.find(def=>def.id==='drgw-consolidation'));
 car.group.position.set(1000050,12,-1000050);car.group.rotation.y=.7;
 car.sections[1].body.rotation.y=.25;
 scene.position.set(-1000000,0,1000000);
 scene.updateMatrixWorld(true);
 const preciseBounds=group=>{
  group.updateWorldMatrix(true,true);const bounds=new THREE.Box3(),point=new THREE.Vector3(),matrix=new THREE.Matrix4();
  group.traverse(object=>{if(!object.isMesh)return;const positions=object.geometry.attributes.position;
   for(let i=0;i<(object.isInstancedMesh?object.count:1);i++){
    if(object.isInstancedMesh){object.getMatrixAt(i,matrix);matrix.premultiply(object.matrixWorld)}else matrix.copy(object.matrixWorld);
    for(let vertex=0;vertex<positions.count;vertex++)bounds.expandByPoint(point.fromBufferAttribute(positions,vertex).applyMatrix4(matrix));
   }
  });return bounds;
 };
 const expected=preciseBounds(car.group);
 instances.sync([car]);
 const actual=preciseBounds(instances.group);
 assert.ok(expected.min.distanceTo(actual.min)<.002&&expected.max.distanceTo(actual.max)<.002);
 assert.ok(car.sections.every(section=>!section.group.visible));
 const matrix=new THREE.Matrix4();
 for(const mesh of instances.group.children)for(let i=0;i<mesh.count;i++){
  mesh.getMatrixAt(i,matrix);assert.ok(Math.abs(matrix.elements[12])<512.01&&Math.abs(matrix.elements[14])<512.01);
 }
 instances.sync([]);assert.ok(car.sections.every(section=>section.group.visible));
}));
