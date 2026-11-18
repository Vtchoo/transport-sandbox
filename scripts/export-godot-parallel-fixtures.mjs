import {buildSync} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';
import path from 'node:path';
import * as THREE from 'three';
const entry=path.resolve('godot/.godot/test-build/parallel-source.mjs');await mkdir(path.dirname(entry),{recursive:true});
buildSync({entryPoints:['src/track/model.ts'],outfile:entry,bundle:true,format:'esm',platform:'node',external:['three','three/*']});
const {TrackNetwork}=await import(pathToFileURL(entry));
const cases=[];
for(const [side,point,endDirection,targetSign] of [[1,[600,0,1100],[0,0,1],0],[1,[610,2,1100],[0,0,1],0],[-1,[605,1,700],[0,0,-1],0],[1,[600,0,1100],[0,0,1],-1]]){
 const network=new TrackNetwork(),source=network.addNode(new THREE.Vector3(600,0,900),1435);source.axis.set(0,0,1);
 const other=network.addParallel(source,1);network.setParallelOffset(other,4.035);
 let target;
 if(targetSign){target=network.addNode(new THREE.Vector3(...point),1435);target.axis.set(0,0,targetSign);const sibling=network.addParallel(target,1);network.setParallelOffset(sibling,4.035*targetSign);}
 const plan=network.previewParallel(source,side,new THREE.Vector3(...point),target,new THREE.Vector3(...endDirection));
 cases.push({side,point,endDirection,targetSign,plan:plan.map(p=>({offset:p.offset,aLength:p.aLength,bLength:p.bLength,controls:[p.curve.v0,p.curve.v1,p.curve.v2,p.curve.v3].map(v=>v.toArray())}))});
}
await writeFile('godot/tests/parallel-fixtures.json',JSON.stringify({cases})+'\n');
console.log('Exported four parallel-construction cases from the browser model.');
