/** Refresh builder metadata and the original SVG thumbnails without rebuilding model geometry. */
import {buildSync} from 'esbuild';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
const {outputFiles}=buildSync({entryPoints:['src/train/catalog.ts'],bundle:true,write:false,format:'esm',platform:'node',external:['three']});
const {VEHICLES}=await import('data:text/javascript;base64,'+Buffer.from(outputFiles[0].text.replaceAll('"three"',JSON.stringify(import.meta.resolve('three')))).toString('base64'));
const path='godot/assets/vehicles/';
const existing=JSON.parse(await readFile(path+'catalog.json','utf8'));
const metadata=d=>({id:d.id,name:d.name,kind:d.kind,length:d.length,width:d.width,height:d.height,bogieCenters:d.bogieCenters,physics:d.physics,
 sections:d.sections?.map(s=>({offset:s.offset,definition:metadata(s.definition)}))});
await mkdir(path+'thumbnails',{recursive:true});
for(const d of VEHICLES){
 const entry=existing.find(e=>e.id===d.id);
 Object.assign(entry,metadata(d));
 entry.thumbnail='res://assets/vehicles/thumbnails/'+d.id+'.svg';
 await writeFile(path+'thumbnails/'+d.id+'.svg',d.thumbnailSvg);
}
await writeFile(path+'catalog.json',JSON.stringify(existing,null,2)+'\n');
console.log('Refreshed metadata and thumbnails for '+VEHICLES.length+' vehicles.');
