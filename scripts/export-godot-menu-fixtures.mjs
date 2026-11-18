/** Numerical behavior fixtures from the browser MenuScene, independent of the native port. */
import {buildSync} from 'esbuild';
import {writeFile,mkdir} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';
import path from 'node:path';
import * as THREE from 'three';
const {outputFiles}=buildSync({entryPoints:['src/menu-scene.ts'],bundle:true,write:false,format:'esm',platform:'node',external:['three']});
await mkdir('godot/.godot/test-build',{recursive:true});
const bundle=path.resolve('godot/.godot/test-build/menu-fixtures.mjs');
await writeFile(bundle,outputFiles[0].text);
const {MenuScene,MENU_TRAINS}=await import(pathToFileURL(bundle).href);
const fixtures=[];
for(let preset=0;preset<MENU_TRAINS.length;preset++){
 const menu=new MenuScene(preset);
 const cameras=[];
 for(const aspect of [16/9,1.6,0.6])for(const time of [0,30000]){menu.update(time,aspect);cameras.push({aspect,time,position:menu.camera.position.toArray(),quaternion:menu.camera.quaternion.toArray()});}
 menu.dispose();
 const simulation=new MenuScene(preset);
 // Feed precisely the same time steps to each implementation, without RAF rounding differences.
 for(let i=1;i<=60;i++)simulation.smoke.update(1/60,simulation.visuals.map(visual=>({visual,effort:0})),new THREE.Vector2(),simulation.camera);
 fixtures.push({preset,...MENU_TRAINS[preset],length:simulation.length,cameras,smoke:simulation.smoke.particles.map(p=>({...p,position:p.position.toArray(),velocity:p.velocity.toArray()}))});
 simulation.dispose();
}
await writeFile('godot/tests/menu-fixtures.json',JSON.stringify(fixtures,null,2)+'\n');
console.log('Wrote browser menu camera/composition/exhaust fixtures for all three presets.');
