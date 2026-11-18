/** Geometry and placement fixtures from the actual original tree/menu classes. */
import { buildSync } from 'esbuild';
import { mkdir, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
import * as THREE from 'three';
const entry = path.resolve('godot/.godot/test-build/tree-source.mjs');
await mkdir(path.dirname(entry), {recursive:true});
buildSync({entryPoints:['src/menu-scene.ts'],outfile:entry,bundle:true,format:'esm',platform:'node',external:['three','three/*']});
const { MenuScene } = await import(pathToFileURL(entry));
const menu = new MenuScene(2), trees = menu.trees;
const positions = geometry => {
  const expanded = geometry.index ? geometry.toNonIndexed() : geometry;
  const data = Array.from(expanded.attributes.position.array);
  if (expanded !== geometry) expanded.dispose();
  return data;
};
const geometry = {trunk:positions(trees.trunk), crowns:trees.crowns.map(positions), distant:trees.distantCrowns.map(positions)};
const batches = [];
for (let species=0;species<2;species++) {
  const crown=menu.woodland.meshes.find(mesh=>mesh.userData.species===species);
  const trunk=menu.woodland.meshes[menu.woodland.meshes.indexOf(crown)-1];
  const transforms=[], color=new THREE.Color(), matrix=new THREE.Matrix4();
  for(let i=0;i<crown.count;i++) {
    trunk.getMatrixAt(i,matrix);const bark=matrix.toArray();
    crown.getMatrixAt(i,matrix);crown.getColorAt(i,color);
    transforms.push({trunk:bark,crown:matrix.toArray(),color:color.toArray()});
  }
  batches.push({species,transforms});
}
await writeFile('godot/tests/tree-fixtures.json',JSON.stringify({geometry,batches})+'\n');
menu.dispose();
console.log('Exported five tree meshes and all 256 menu tree transforms/colors from TypeScript.');
