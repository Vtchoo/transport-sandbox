import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { ColladaLoader } from 'three/addons/loaders/ColladaLoader.js';
import { OBJLoader } from 'three/addons/loaders/OBJLoader.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { STLLoader } from 'three/addons/loaders/STLLoader.js';
import { GLTFExporter } from 'three/addons/exporters/GLTFExporter.js';
import { buildTopology, sharpSeams, connectedFaces, pack, updateUVs, atlasIssues, snapIsland, islandsInBox, islandTouchesBox } from './core.mjs';
import workerSource from './worker.mjs?inline';

const $=id=>document.getElementById(id), viewport=$('viewport'),canvas=$('atlas');
let faces=[],materials=[],source=null,sourceMeshes=[],sourceName='',topology=null,seams=new Set(),selected=new Set(),result=null;
let mode='face',activeIsland=-1,worker=null,history=[],paintedImage=null,paintedTexture=null,blobURLs=[];
let workerURL=null;
let snapGuides=[];
let selectedIslands=new Set(),atlasDrag=null;
function stopWorker(){worker?.terminate();worker=null;if(workerURL){URL.revokeObjectURL(workerURL);workerURL=null;}}
let tolerance=0,acrossMeshes=false,drag=null,busy=false,displayMesh=null,wire=null,seamLines=null;
const scene=new THREE.Scene(),camera=new THREE.PerspectiveCamera(42,1,.001,1e7);
const renderer=new THREE.WebGLRenderer({antialias:true,alpha:true,preserveDrawingBuffer:false});
renderer.setPixelRatio(Math.min(devicePixelRatio,2));viewport.appendChild(renderer.domElement);
renderer.outputColorSpace=THREE.SRGBColorSpace;renderer.setClearColor(0,0);
const controls=new OrbitControls(camera,renderer.domElement);controls.enableDamping=true;
controls.mouseButtons={LEFT:null,MIDDLE:THREE.MOUSE.PAN,RIGHT:THREE.MOUSE.ROTATE};
scene.add(new THREE.HemisphereLight(0xe7f3ff,0x566268,2.4));
const sun=new THREE.DirectionalLight(0xffffff,2.2);sun.position.set(3,6,5);scene.add(sun);
const raycaster=new THREE.Raycaster(),pointer=new THREE.Vector2(),atlasTexture=new THREE.CanvasTexture(canvas);
atlasTexture.colorSpace=THREE.SRGBColorSpace;atlasTexture.flipY=true;
const materialBase=new THREE.MeshStandardMaterial({vertexColors:true,side:THREE.DoubleSide,roughness:.75});
const materialTextured=new THREE.MeshStandardMaterial({vertexColors:true,side:THREE.DoubleSide,roughness:.75,map:atlasTexture});
const sceneRoot=new THREE.Group();scene.add(sceneRoot);
const status=(message,error=false)=>{$('status').textContent=message;if(error)console.error(message);};
const warn=messages=>{$('warnings').hidden=!messages.length;$('warnings').textContent=messages.slice(0,8).join('\n')+(messages.length>8?`\n… ${messages.length-8} more warnings.`:'');};
const colorFor=i=>new THREE.Color().setHSL((i*.61803398875+.08)%1,.43,.61);
const download=(blob,name)=>{const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);};
const stem=()=>sourceName.replace(/\.[^.]+$/,'')||'model';
const jsonDownload=(data,name)=>download(new Blob([JSON.stringify(data,null,2)],{type:'application/json'}),name);
function saveHistory(){history.push({selected:[...selected],seams:[...seams],tolerance,acrossMeshes,result:result?structuredClone(result):null,islandSelection:[...selectedIslands],activeIsland});if(history.length>20)history.shift();}
function invalidate(){result=null;activeIsland=-1;selectedIslands.clear();atlasDrag=null;snapGuides=[];paintedImage?.close?.();paintedImage=null;if(paintedTexture){paintedTexture.dispose();paintedTexture=null;}$('preview-texture').checked=false;drawAtlas();}
function changed(message){invalidate();refresh();status(message);}
function resize(){const w=viewport.clientWidth,h=viewport.clientHeight;renderer.setSize(w,h);camera.aspect=w/Math.max(h,1);camera.updateProjectionMatrix();sizeAtlas();}
new ResizeObserver(resize).observe(viewport);new ResizeObserver(()=>sizeAtlas()).observe($('atlas-wrap'));
function animate(){requestAnimationFrame(animate);controls.update();renderer.render(scene,camera);}animate();
function fit(direction=null){if(!faces.length)return;const box=new THREE.Box3();for(const f of faces)for(const p of f.p)box.expandByPoint(new THREE.Vector3(...p));const center=box.getCenter(new THREE.Vector3()),r=Math.max(box.getSize(new THREE.Vector3()).length()/2,.001);const dir=direction||new THREE.Vector3(1,.65,1).normalize();controls.target.copy(center);camera.position.copy(center).addScaledVector(dir,r/Math.sin(camera.fov*Math.PI/360)*Math.max(1,1/camera.aspect)*1.15);camera.near=r/10000;camera.far=r*10000;camera.updateProjectionMatrix();controls.update();}
function sizeAtlas(){const wrap=$('atlas-wrap'),size=Math.max(1,Math.min(wrap.clientWidth-36,wrap.clientHeight-36));canvas.style.width=size+'px';canvas.style.height=size+'px';$('snap-guides').style.width=size+'px';$('snap-guides').style.height=size+'px';}
function clearObject(object){if(!object)return;sceneRoot.remove(object);object.geometry?.dispose();if(object!==displayMesh)object.material?.dispose();}
function buildDisplay(){
  clearObject(displayMesh);clearObject(wire);clearObject(seamLines);
  const geometry=new THREE.BufferGeometry(),positions=new Float32Array(faces.length*9);
  faces.forEach((f,i)=>f.p.forEach((p,c)=>positions.set(p,i*9+c*3)));
  geometry.setAttribute('position',new THREE.BufferAttribute(positions,3));geometry.setAttribute('color',new THREE.BufferAttribute(new Float32Array(positions.length),3));geometry.setAttribute('uv',new THREE.BufferAttribute(new Float32Array(faces.length*6),2));geometry.computeVertexNormals();
  displayMesh=new THREE.Mesh(geometry,[materialBase,materialTextured]);sceneRoot.add(displayMesh);
  const segments=[];for(const e of topology.edges.values())segments.push(...topology.vertices[e.a],...topology.vertices[e.b]);
  const lineGeometry=new THREE.BufferGeometry();lineGeometry.setAttribute('position',new THREE.Float32BufferAttribute(segments,3));
  wire=new THREE.LineSegments(lineGeometry,new THREE.LineBasicMaterial({color:0x91a9ba,transparent:true,opacity:.24,depthTest:true}));wire.renderOrder=1;sceneRoot.add(wire);refresh();
}
function refresh(){
  $('counts').textContent=`${faces.length.toLocaleString()} faces · ${selected.size.toLocaleString()} selected · ${seams.size.toLocaleString()} seams`;
  $('project-save').disabled=!faces.length;
  for(const id of ['unwrap','weld','auto-seams','clear-seams','select-all','select-none','invert'])$(id).disabled=!faces.length||busy;
  for(const id of ['export-png','export-svg','export-json','export-glb','repack','rotate-island','texture-open'])$(id).disabled=!result;
  $('undo').disabled=!history.length||busy;
  $('stats').textContent=result?`${result.islands.length} islands · ${selected.size} selected faces\n${result.pixelsPerUnit.toFixed(2)} pixels / model unit · ${result.warnings.length} solver warnings`:`${topology?.vertices.length.toLocaleString()||0} topology vertices · ${faces.length.toLocaleString()} triangles`;
  $('stats').style.whiteSpace='pre-line';
  if(!displayMesh)return;
  wire.visible=$('wireframe').checked;
  const color=displayMesh.geometry.getAttribute('color'),uv=displayMesh.geometry.getAttribute('uv');
  const activeFaces=new Set(result?.islands.filter(i=>selectedIslands.has(i.id)).flatMap(i=>i.faceIds)||[]);
  const textured=$('preview-texture').checked&&result;
  displayMesh.geometry.clearGroups();let groupStart=0,groupMaterial=-1;
  faces.forEach((face,i)=>{
    const mapped=!!result?.uvByFace[i],useTexture=textured&&mapped;
    let c=new THREE.Color(materials[face.material]?.color||'#95a4af');
    if(!selected.has(i))c.multiplyScalar(.4);else if(useTexture)c.set(0xffffff);else c.lerp(new THREE.Color('#c9f48a'),.55);
    if(activeFaces.has(i))c.lerp(new THREE.Color('#63c8ff'),.65);
    for(let j=0;j<3;j++){color.setXYZ(i*3+j,c.r,c.g,c.b);const u=result?.uvByFace[i]?.[j]||[0,0];uv.setXY(i*3+j,u[0],u[1]);}
    const m=useTexture?1:0;if(m!==groupMaterial){if(groupMaterial>=0)displayMesh.geometry.addGroup(groupStart,(i*3)-groupStart,groupMaterial);groupStart=i*3;groupMaterial=m;}
  });
  if(faces.length)displayMesh.geometry.addGroup(groupStart,faces.length*3-groupStart,groupMaterial);
  color.needsUpdate=true;uv.needsUpdate=true;
  materialTextured.map=paintedTexture||atlasTexture;materialTextured.needsUpdate=true;
  clearObject(seamLines);const lines=[];for(const key of seams){const e=topology.edges.get(key);if(e&&e.uses.some(u=>selected.has(u.face)))lines.push(...topology.vertices[e.a],...topology.vertices[e.b]);}
  const g=new THREE.BufferGeometry();g.setAttribute('position',new THREE.Float32BufferAttribute(lines,3));
  seamLines=new THREE.LineSegments(g,new THREE.LineBasicMaterial({color:0xffa65d,depthTest:false,transparent:true,opacity:.95}));seamLines.renderOrder=3;sceneRoot.add(seamLines);
  syncMaterialChecks();for(const input of $('materials').querySelectorAll('input'))input.disabled=busy;
}
function syncMaterialChecks(){for(const input of $('materials').querySelectorAll('input')){const ids=faces.map((f,i)=>f.material===Number(input.dataset.id)?i:-1).filter(i=>i>=0),count=ids.filter(i=>selected.has(i)).length;input.checked=count===ids.length;input.indeterminate=count>0&&count<ids.length;}}
function populateMaterials(){
  $('materials').replaceChildren();materials.forEach((m,id)=>{
    const label=document.createElement('label'),input=document.createElement('input'),swatch=document.createElement('span'),text=document.createElement('span');input.type='checkbox';input.dataset.id=id;input.checked=true;swatch.className='swatch';swatch.style.background=m.color;text.textContent=`${m.name} (${faces.filter(f=>f.material===id).length})`;label.append(input,swatch,text);$('materials').append(label);
    input.onchange=()=>{saveHistory();faces.forEach((f,i)=>{if(f.material===id){if(input.checked)selected.add(i);else selected.delete(i);}});changed('Face selection changed. Generate the atlas again.');};
  });
}
function setModel(root,name){
  stopWorker();busy=false;source=root;sourceName=name;sourceMeshes=[];faces=[];materials=[];history=[];seams.clear();tolerance=0;acrossMeshes=false;invalidate();
  source.updateMatrixWorld(true);
  const materialMap=new Map();let meshId=0;
  root.traverse(node=>{if(!node.isMesh||!node.geometry?.attributes.position)return;
    const mesh=meshId++;sourceMeshes.push(node);const geometry=node.geometry,pos=geometry.getAttribute('position'),idx=geometry.getIndex(),localMaterials=Array.isArray(node.material)?node.material:[node.material];
    const localIds=localMaterials.map((m,i)=>{if(!materialMap.has(m)){materialMap.set(m,materials.length);materials.push({name:m?.name||`${node.name||'Mesh '+mesh} / Material ${i+1}`,color:'#'+(m?.color?.getHexString()||'95a4af')});}return materialMap.get(m);});
    const count=idx?idx.count:pos.count;
    for(let offset=0;offset+2<count;offset+=3){const group=geometry.groups.find(g=>offset>=g.start&&offset<g.start+g.count),material=localIds[group?.materialIndex||0]??localIds[0];
      const p=[0,1,2].map(c=>new THREE.Vector3().fromBufferAttribute(pos,idx?idx.getX(offset+c):offset+c).applyMatrix4(node.matrixWorld).toArray());
      if(p.some(v=>v.some(n=>!Number.isFinite(n))))continue;
      faces.push({p,mesh,triangle:offset/3,meshName:node.name||`Mesh ${mesh}`,material});
    }
  });
  if(!faces.length)throw new Error('No triangle meshes found in this model.');
  selected=new Set(faces.map((_,i)=>i));topology=buildTopology(faces);populateMaterials();buildDisplay();fit();
  $('filename').textContent=name;$('empty').hidden=true;$('cancel').hidden=true;
  status(`Loaded ${name}. Select paint faces, mark seams, then generate.`);
  warn(faces.length>150000?['Large model: face picking and topology editing may be slow. Unwrapping runs in a worker.']:[]);
}

async function openFiles(fileList){
  const files=[...fileList],primary=files.find(f=>/\.(dae|obj|glb|gltf|stl)$/i.test(f.name));if(!primary)throw new Error('Choose a DAE, OBJ, GLB, glTF or STL model. Include companion files with glTF.');
  if(busy)throw new Error('Cancel the current generation before opening another model.');
  status('Loading '+primary.name+'…');
  const urls=new Map(),newURLs=[];for(const file of files){const url=URL.createObjectURL(file);urls.set(file.name,url);urls.set(file.webkitRelativePath||file.name,url);newURLs.push(url);}
  const manager=new THREE.LoadingManager();let missing=false;
  manager.setURLModifier(url=>{const clean=decodeURIComponent(url).replaceAll('\\','/'),name=clean.split('/').pop();if(url.startsWith('blob:')||url.startsWith('data:'))return url;if(urls.has(clean))return urls.get(clean);if(urls.has(name))return urls.get(name);missing=true;return 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jYioAAAAASUVORK5CYII=';});
  try{
    let root;const ext=primary.name.split('.').pop().toLowerCase();
    if(ext==='dae')root=new ColladaLoader(manager).parse(await primary.text(),'').scene;
    if(ext==='obj')root=new OBJLoader(manager).parse(await primary.text());
    if(ext==='glb'||ext==='gltf'){const payload=ext==='glb'?await primary.arrayBuffer():await primary.text();root=(await new Promise((resolve,reject)=>new GLTFLoader(manager).parse(payload,'',resolve,reject))).scene;}
    if(ext==='stl'){root=new THREE.Group();root.add(new THREE.Mesh(new STLLoader(manager).parse(await primary.arrayBuffer()),new THREE.MeshStandardMaterial({color:0xa6b4bd})));}
    setModel(root,primary.name);for(const url of blobURLs)URL.revokeObjectURL(url);blobURLs=newURLs;
    if(missing)warn(['Some companion textures were missing. Geometry and UV authoring are still available. Include the model’s textures when opening.']);
  }catch(error){for(const url of newURLs)URL.revokeObjectURL(url);throw error;}
}

function hit(event){if(!displayMesh)return null;const r=renderer.domElement.getBoundingClientRect();pointer.set((event.clientX-r.left)/r.width*2-1,-(event.clientY-r.top)/r.height*2+1);raycaster.setFromCamera(pointer,camera);return raycaster.intersectObject(displayMesh)[0]||null;}
function nearestEdge(event,intersection){
  const f=intersection.faceIndex,r=renderer.domElement.getBoundingClientRect(),p=faces[f].p.map(v=>{const q=new THREE.Vector3(...v).project(camera);return [(q.x+1)*r.width/2+r.left,(1-q.y)*r.height/2+r.top];});let best=null,distance=15;
  for(let c=0;c<3;c++){const a=p[c],b=p[(c+1)%3],dx=b[0]-a[0],dy=b[1]-a[1],t=Math.max(0,Math.min(1,((event.clientX-a[0])*dx+(event.clientY-a[1])*dy)/(dx*dx+dy*dy||1))),d=Math.hypot(event.clientX-a[0]-t*dx,event.clientY-a[1]-t*dy);if(d<distance){distance=d;const ids=topology.indices[f];best=ids[c]<ids[(c+1)%3]?`${ids[c]}:${ids[(c+1)%3]}`:`${ids[(c+1)%3]}:${ids[c]}`;}}
  return best;
}
renderer.domElement.addEventListener('contextmenu',e=>e.preventDefault());
renderer.domElement.addEventListener('pointerdown',e=>{if(e.button===0)drag={x:e.clientX,y:e.clientY};});
renderer.domElement.addEventListener('pointerup',e=>{
  if(e.button!==0||!drag)return;const moved=Math.hypot(e.clientX-drag.x,e.clientY-drag.y);drag=null;if(moved>5||busy)return;const intersection=hit(e);if(!intersection)return;
  if(mode==='seam'){const edge=nearestEdge(e,intersection);if(!edge){status('Click closer to an edge (within 15 pixels).');return;}saveHistory();if(e.altKey)seams.delete(edge);else if(e.shiftKey)seams.add(edge);else if(seams.has(edge))seams.delete(edge);else seams.add(edge);changed('Seam updated. Orange edges will be cut.');}
  else{saveHistory();const f=intersection.faceIndex;if(e.altKey)selected.delete(f);else if(e.shiftKey)selected.add(f);else if(selected.has(f))selected.delete(f);else selected.add(f);changed(`Face ${f}: ${faces[f].meshName} · ${materials[faces[f].material].name}`);}
});
renderer.domElement.addEventListener('dblclick',e=>{if(mode!=='face'||busy)return;const intersection=hit(e);if(!intersection)return;saveHistory();const group=connectedFaces(intersection.faceIndex,topology,seams);for(const f of group)if(e.altKey)selected.delete(f);else selected.add(f);changed(`Selected connected surface (${group.size} faces).`);});
function setMode(value){mode=value;$('face-mode').classList.toggle('active',mode==='face');$('seam-mode').classList.toggle('active',mode==='seam');$('mode-label').textContent=mode==='face'?'FACE SELECTION':'SEAM SELECTION · ORANGE = CUT';}

function makeAtlas(style=$('atlas-style').value,inspection=true){
  const out=document.createElement('canvas');out.width=out.height=result?.size||Number($('size').value);const ctx=out.getContext('2d');ctx.fillStyle=style==='outline'?'#ffffff':'#202a32';ctx.fillRect(0,0,out.width,out.height);if(!result)return out;
  if(paintedImage){ctx.drawImage(paintedImage,0,0,out.width,out.height);return out;}
  if(style==='checker'){const cell=out.width/32;for(let y=0;y<32;y++)for(let x=0;x<32;x++){ctx.fillStyle=(x+y)%2?'#abbac2':'#364b58';ctx.fillRect(x*cell,y*cell,cell,cell);}}
  for(const island of result.islands){ctx.fillStyle=style==='guide'?'#'+colorFor(island.id).getHexString():'#ffffff';ctx.strokeStyle=inspection&&selectedIslands.has(island.id)?'#5acbff':style==='outline'?'#20272b':'#15212a';ctx.lineWidth=Math.max(1,result.size/4096)*(inspection&&selectedIslands.has(island.id)?3:1);
    for(const f of island.faceIds){const uv=result.uvByFace[f];ctx.beginPath();uv.forEach(([u,v],i)=>{const p=[u*out.width,(1-v)*out.height];if(i)ctx.lineTo(...p);else ctx.moveTo(...p);});ctx.closePath();if(style!=='checker')ctx.fill();if($('triangle-edges').checked)ctx.stroke();}
    if(!$('triangle-edges').checked){ctx.beginPath();for(const [a,b]of outlineEdges(island)){ctx.moveTo(...a);ctx.lineTo(...b);}ctx.stroke();}
    if(style==='guide'){const r=island.rect;ctx.fillStyle='#0f202b';ctx.font=`${Math.max(16,result.size/160)}px sans-serif`;ctx.fillText(String(island.id+1),r.x+5,r.y+26);}
  }return out;
}
function drawAtlas(){const out=makeAtlas();canvas.width=out.width;canvas.height=out.height;canvas.getContext('2d').drawImage(out,0,0);$('atlas-empty').hidden=!!result;$('atlas-size').textContent=out.width+'²';atlasTexture.needsUpdate=true;sizeAtlas();drawSnapGuides();}
function drawSnapGuides(){const overlay=$('snap-guides');overlay.replaceChildren();$('island-selection-count').textContent=`${selectedIslands.size} selected`;overlay.setAttribute('viewBox',`0 0 ${result?.size||4096} ${result?.size||4096}`);if(!result)return;for(const guide of snapGuides){const line=document.createElementNS('http://www.w3.org/2000/svg','line'),vertical=guide.axis==='x';line.setAttribute('x1',vertical?guide.position:0);line.setAttribute('x2',vertical?guide.position:result.size);line.setAttribute('y1',vertical?0:guide.position);line.setAttribute('y2',vertical?result.size:guide.position);overlay.append(line);}
  if(atlasDrag?.kind==='box'){const selection=islandsInBox(result,atlasDrag.start,atlasDrag.end),rect=document.createElementNS('http://www.w3.org/2000/svg','rect');for(const key of ['x','y','width','height'])rect.setAttribute(key,selection.box[{width:'w',height:'h'}[key]||key]);rect.setAttribute('class',selection.crossing?'crossing-selection':'window-selection');overlay.append(rect);}
}
function atlasPoint(event){const r=canvas.getBoundingClientRect();return {x:(event.clientX-r.left)/r.width*result.size,y:(event.clientY-r.top)/r.height*result.size};}
function islandAt(event){if(!result)return null;const {x,y}=atlasPoint(event);for(const island of result.islands.slice().reverse())if(islandTouchesBox(island,{x,y,w:0,h:0}))return {island,x,y};return null;}
function selectionBounds(){const items=result.islands.filter(i=>selectedIslands.has(i.id)),x=Math.min(...items.map(i=>i.rect.x)),y=Math.min(...items.map(i=>i.rect.y));return {x,y,w:Math.max(...items.map(i=>i.rect.x+i.rect.w))-x,h:Math.max(...items.map(i=>i.rect.y+i.rect.h))-y};}
canvas.addEventListener('pointerdown',e=>{if(busy||!result||e.button!==0)return;e.preventDefault();const picked=islandAt(e),start=atlasPoint(e);snapGuides=[];
  if(!picked||e.shiftKey||e.ctrlKey||e.metaKey){atlasDrag={kind:'box',start,end:start,base:new Set(selectedIslands),selectionMode:e.ctrlKey||e.metaKey?'subtract':e.shiftKey?'add':'replace',moved:false};if(picked){if(e.ctrlKey||e.metaKey)selectedIslands.delete(picked.island.id);else selectedIslands.add(picked.island.id);}else if(atlasDrag.selectionMode==='replace')selectedIslands.clear();}
  else{if(!selectedIslands.has(picked.island.id))selectedIslands=new Set([picked.island.id]);activeIsland=picked.island.id;saveHistory();atlasDrag={kind:'move',start,bounds:selectionBounds(),origins:new Map(result.islands.filter(i=>selectedIslands.has(i.id)).map(i=>[i.id,{x:i.rect.x,y:i.rect.y}]))};}
  canvas.setPointerCapture(e.pointerId);drawAtlas();refresh();
});
canvas.addEventListener('pointermove',e=>{if(!atlasDrag)return;const a=atlasDrag,p=atlasPoint(e),r=canvas.getBoundingClientRect();
  if(a.kind==='box'){a.end=p;a.moved=Math.hypot(p.x-a.start.x,p.y-a.start.y)>3*result.size/r.width;if(a.moved){const box=islandsInBox(result,a.start,p),ids=new Set(box.ids);selectedIslands=a.selectionMode==='replace'?ids:new Set(a.base);if(a.selectionMode==='add')for(const id of ids)selectedIslands.add(id);if(a.selectionMode==='subtract')for(const id of ids)selectedIslands.delete(id);activeIsland=[...selectedIslands].at(-1)??-1;status(`${box.crossing?'Crossing: touching islands':'Window: fully enclosed islands'} · ${selectedIslands.size} selected`);}drawAtlas();refresh();return;}
  const proposed={...a.bounds,x:a.bounds.x+p.x-a.start.x,y:a.bounds.y+p.y-a.start.y},targets={...result,islands:result.islands.filter(i=>!selectedIslands.has(i.id))};const snapped=$('island-snap').checked&&!e.altKey?snapIsland(targets,-1,proposed,8*result.size/r.width):{rect:proposed,guides:[]};for(const [id,origin]of a.origins){const island=result.islands.find(i=>i.id===id);island.rect.x=origin.x+snapped.rect.x-a.bounds.x;island.rect.y=origin.y+snapped.rect.y-a.bounds.y;}snapGuides=snapped.guides;updateUVs(result);drawAtlas();refresh();
});
function finishIslandDrag(){if(atlasDrag){const moved=atlasDrag.kind==='move';atlasDrag=null;snapGuides=[];drawSnapGuides();if(moved){warn([...result.warnings,...atlasIssues(result)]);status('Islands moved. Check padding and overlaps before export.');}else status(`${selectedIslands.size} islands selected. Drag a selected island to move the group.`);}}
canvas.addEventListener('pointerup',finishIslandDrag);canvas.addEventListener('pointercancel',finishIslandDrag);canvas.addEventListener('lostpointercapture',finishIslandDrag);

function runUnwrap(){
  if(!selected.size){warn(['Select at least one face first.']);return;}saveHistory();stopWorker();workerURL=URL.createObjectURL(new Blob([workerSource],{type:'text/javascript'}));worker=new Worker(workerURL);busy=true;$('cancel').hidden=false;refresh();status('Preparing unwrap…');
  worker.onmessage=({data})=>{if(data.progress){status(data.progress);return;}busy=false;$('cancel').hidden=true;stopWorker();if(data.error){warn([data.error]);status('Unwrap failed. Adjust seams and retry.');refresh();return;}result=data.result;activeIsland=-1;selectedIslands.clear();atlasDrag=null;paintedImage=null;paintedTexture?.dispose();paintedTexture=null;$('preview-texture').checked=true;drawAtlas();refresh();warn(result.warnings);status(`Generated ${result.islands.length} islands. Inspect checker distortion before painting.`);};
  worker.onerror=e=>{busy=false;$('cancel').hidden=true;stopWorker();refresh();warn([e.message]);};
  worker.postMessage({faces,tolerance,acrossMeshes,seams:[...seams],selected:[...selected],options:{size:Number($('size').value),padding:Number($('padding').value),method:$('method').value}});
}
function mapping(){return {format:'transport-sandbox-uv',version:1,source:sourceName,coordinateSpace:'world, same units as imported model',uvOrigin:'bottom-left',faceAddress:'mesh traversal index + original triangle index (before splitting)',atlas:{width:result.size,height:result.size,padding:result.padding,pixelsPerUnit:result.pixelsPerUnit},materials,topology:{tolerance,acrossMeshes,seams:[...seams]},faces:faces.map((f,i)=>({...f,id:i,selected:selected.has(i),uv:result.uvByFace[i]})),islands:result.islands.map(i=>({id:i.id,faceIds:i.faceIds,rect:i.rect})),warnings:[...result.warnings,...atlasIssues(result)]};}
async function exportGLB(){
  if(!result)return;status('Exporting UV-mapped GLB…');
  const root=source.clone(true),meshes=[];root.traverse(n=>{if(n.isMesh&&n.geometry?.attributes.position)meshes.push(n);});
  const texture=paintedTexture||new THREE.CanvasTexture(makeAtlas($('atlas-style').value,false));texture.colorSpace=THREE.SRGBColorSpace;
  const temporaries=[];
  try{
    meshes.forEach((mesh,meshId)=>{
      const old=mesh.geometry,g=old.index?old.toNonIndexed():old.clone();temporaries.push(g);mesh.geometry=g;
      const originals=Array.isArray(mesh.material)?mesh.material:[mesh.material],mats=originals.slice(),uv=g.attributes.uv?g.attributes.uv.array.slice():new Float32Array(g.attributes.position.count*2),faceMap=source.userData.uvFaceIndicesByMesh?new Map(source.userData.uvFaceIndicesByMesh[meshId].map((f,t)=>[t,f])):new Map(faces.map((f,i)=>[f.mesh===meshId?f.triangle:-1,i])),oldGroups=old.groups.slice();g.clearGroups();
      const clones=new Map();let start=0,previous=-1;
      for(let t=0;t<g.attributes.position.count/3;t++){
        const f=faceMap.get(t),coords=f===undefined?null:result.uvByFace[f];let mi=oldGroups.find(group=>t*3>=group.start&&t*3<group.start+group.count)?.materialIndex||0;
        if(!originals[mi])mi=0;
        if(coords){coords.forEach((p,c)=>uv.set(p,(t*3+c)*2));if(!clones.has(mi)){const m=originals[mi].clone();m.name=(m.name||'Material')+' / UV atlas';m.map=texture;m.color?.set(0xffffff);m.vertexColors=false;clones.set(mi,mats.length);mats.push(m);temporaries.push(m);}mi=clones.get(mi);}
        if(mi!==previous){if(previous>=0)g.addGroup(start,t*3-start,previous);start=t*3;previous=mi;}
      }
      g.addGroup(start,g.attributes.position.count-start,previous);g.setAttribute('uv',new THREE.BufferAttribute(uv,2));mesh.material=mats;
    });
    const binary=await new GLTFExporter().parseAsync(root,{binary:true,onlyVisible:false});download(new Blob([binary],{type:'model/gltf-binary'}),stem()+'-uv.glb');status('Exported UV model. Hierarchy and local transforms retained.');
  }finally{temporaries.forEach(r=>r.dispose());if(!paintedTexture)texture.dispose();}
}
function outlineEdges(island){const edges=new Map();for(const tri of island.tris)for(let c=0;c<3;c++){const a=tri[c],b=tri[(c+1)%3],key=a<b?`${a}:${b}`:`${b}:${a}`;if(!edges.has(key))edges.set(key,{a,b,count:0});edges.get(key).count++;}const r=island.rect;return [...edges.values()].filter(e=>e.count===1||$('triangle-edges').checked).map(e=>[e.a,e.b].map(id=>[r.x+island.points[id][0]*r.scale,r.y+island.points[id][1]*r.scale]));}
function svg(){const size=result.size,lines=[`<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}"><rect width="100%" height="100%" fill="white"/><g fill="none" stroke="#273642" stroke-width="1">`];for(const island of result.islands)for(const [a,b]of outlineEdges(island))lines.push(`<path d="M${a[0].toFixed(3)},${a[1].toFixed(3)} L${b[0].toFixed(3)},${b[1].toFixed(3)}"/>`);lines.push('</g></svg>');return lines.join('\n');}

function saveProject(){jsonDownload({format:'uv-workshop-project',version:1,sourceName,faces,materials,selected:[...selected],seams:[...seams],tolerance,acrossMeshes,result,options:{size:$('size').value,padding:$('padding').value,method:$('method').value}},stem()+'.uv-project.json');status('Project saved. Painted image files are separate; reload them after reopening.');}
function openProject(data){
  if(busy)throw new Error('Cancel the current generation before opening a project.');
  if(data.format!=='uv-workshop-project'||data.version!==1||!Array.isArray(data.faces)||!Array.isArray(data.materials))throw new Error('Not a supported UV Workshop project.');
  if(!data.faces.length||data.faces.some(f=>!Array.isArray(f.p)||f.p.length!==3||f.p.some(p=>p.length!==3||p.some(n=>!Number.isFinite(n)))))throw new Error('Project contains invalid face coordinates.');
  // Self-contained project reconstruction; original mesh addresses stay in faces.
  const root=new THREE.Group(),meshIds=[...new Set(data.faces.map(f=>f.mesh))].sort((a,b)=>a-b);
  for(const id of meshIds){const list=data.faces.filter(f=>f.mesh===id),g=new THREE.BufferGeometry();g.setAttribute('position',new THREE.Float32BufferAttribute(list.flatMap(f=>f.p.flat()),3));const mats=data.materials.map(m=>new THREE.MeshStandardMaterial({name:m.name,color:m.color}));list.forEach((f,i)=>g.addGroup(i*3,3,f.material));const mesh=new THREE.Mesh(g,mats);mesh.name=list[0].meshName;root.add(mesh);}
  root.userData.uvFaceIndicesByMesh=meshIds.map(id=>data.faces.map((f,i)=>f.mesh===id?i:-1).filter(i=>i>=0));
  setModel(root,data.sourceName);faces=data.faces;materials=data.materials;selected=new Set(data.selected);seams=new Set(data.seams);tolerance=data.tolerance;acrossMeshes=data.acrossMeshes;result=data.result;history=[];
  if(data.options)for(const key of ['size','padding','method'])$(key).value=data.options[key];
  topology=buildTopology(faces,tolerance,acrossMeshes);populateMaterials();buildDisplay();drawAtlas();fit();warn(['Project reopened from world-space triangles. GLB exports from reopened projects have a flattened hierarchy. Reload the original model to preserve its original hierarchy.']);status('Project reopened with selection, seams and atlas.');
}

const safe=fn=>(...args)=>Promise.resolve().then(()=>fn(...args)).catch(e=>{status(e.message,true);warn([e.message]);});
$('open').onclick=()=>$('model-input').click();$('model-input').onchange=safe(e=>openFiles(e.target.files));
$('project-open').onclick=()=>$('project-input').click();$('project-input').onchange=safe(async e=>openProject(JSON.parse(await e.target.files[0].text())));$('project-save').onclick=saveProject;
$('face-mode').onclick=()=>setMode('face');$('seam-mode').onclick=()=>setMode('seam');
for(const [id,action]of [['select-all',()=>selected=new Set(faces.map((_,i)=>i))],['select-none',()=>selected.clear()],['invert',()=>selected=new Set(faces.map((_,i)=>i).filter(i=>!selected.has(i)))]])$(id).onclick=()=>{saveHistory();action();changed('Selection updated.');};
$('wireframe').onchange=refresh;$('preview-texture').onchange=refresh;
$('weld').onclick=safe(()=>{const next=Number($('weld-tolerance').value);if(!Number.isFinite(next)||next<0)throw new Error('Tolerance must be a non-negative number.');saveHistory();tolerance=next;acrossMeshes=true;topology=buildTopology(faces,tolerance,true);seams.clear();invalidate();buildDisplay();warn(['Topology rebuilt across all meshes. Previous seams were cleared; inspect coincident separate parts before unwrapping.']);status(`Collapsed topology to ${topology.vertices.length} vertices. Geometry positions unchanged.`);});
$('auto-seams').onclick=()=>{saveHistory();const automatic=sharpSeams(faces,topology,Number($('seam-angle').value),$('material-seams').checked);for(const key of automatic)if(topology.edges.get(key).uses.some(u=>selected.has(u.face)))seams.add(key);changed('Sharp edges and open/non-manifold borders on the selected surfaces marked.');};
$('clear-seams').onclick=()=>{saveHistory();seams.clear();changed('Seams cleared.');};
$('undo').onclick=()=>{const state=history.pop();if(!state)return;selected=new Set(state.selected);seams=new Set(state.seams);tolerance=state.tolerance;acrossMeshes=state.acrossMeshes;result=state.result;selectedIslands=new Set(state.islandSelection||[]);activeIsland=state.activeIsland??-1;topology=buildTopology(faces,tolerance,acrossMeshes);buildDisplay();drawAtlas();status('Undid last edit.');};
$('unwrap').onclick=runUnwrap;$('cancel').onclick=()=>{stopWorker();busy=false;$('cancel').hidden=true;refresh();status('Generation cancelled.');};
$('fit').onclick=()=>fit();$('view-front').onclick=()=>fit(new THREE.Vector3(0,0,1));$('view-side').onclick=()=>fit(new THREE.Vector3(1,0,0));$('view-top').onclick=()=>fit(new THREE.Vector3(0,1,.0001).normalize());
$('atlas-style').onchange=()=>{drawAtlas();refresh();};
$('triangle-edges').onchange=()=>{drawAtlas();refresh();};
$('repack').onclick=()=>{if(!result)return;saveHistory();result.size=Number($('size').value);result.padding=Number($('padding').value);pack(result);drawAtlas();refresh();warn(result.warnings);status('Repacked islands. Existing painted texture will need repainting.');};
$('rotate-island').onclick=()=>{if(!selectedIslands.size){status('Select islands in the atlas first.');return;}saveHistory();const bounds=selectionBounds();for(const i of result.islands.filter(i=>selectedIslands.has(i.id))){const oldWidth=i.width,r=i.rect,oldX=r.x;i.points=i.points.map(([x,y])=>[y,oldWidth-x]);r.x=bounds.x+r.y-bounds.y;r.y=bounds.y+bounds.w-(oldX-bounds.x)-r.w;[i.width,i.height]=[i.height,i.width];[r.w,r.h]=[r.h,r.w];}updateUVs(result);drawAtlas();refresh();warn([...result.warnings,...atlasIssues(result)]);};
$('texture-open').onclick=()=>$('texture-input').click();$('texture-input').onchange=safe(async e=>{const file=e.target.files[0];if(!file)return;const bitmap=await createImageBitmap(file);paintedImage?.close?.();paintedImage=bitmap;paintedTexture?.dispose();const imageCanvas=document.createElement('canvas');imageCanvas.width=bitmap.width;imageCanvas.height=bitmap.height;imageCanvas.getContext('2d').drawImage(bitmap,0,0);paintedTexture=new THREE.CanvasTexture(imageCanvas);paintedTexture.colorSpace=THREE.SRGBColorSpace;paintedTexture.flipY=true;$('preview-texture').checked=true;drawAtlas();refresh();status(`Previewing ${file.name}. Texture stretched to atlas; UV coordinates unchanged.`);});
$('export-png').onclick=()=>makeAtlas($('atlas-style').value,false).toBlob(blob=>download(blob,stem()+'-atlas.png'));
$('export-svg').onclick=()=>download(new Blob([svg()],{type:'image/svg+xml'}),stem()+'-uv-guide.svg');$('export-json').onclick=()=>jsonDownload(mapping(),stem()+'-uv-map.json');$('export-glb').onclick=safe(exportGLB);
$('demo').onclick=()=>{const root=new THREE.Group(),m=new THREE.MeshStandardMaterial({color:0x91aeb9,name:'paint'});root.add(new THREE.Mesh(new THREE.BoxGeometry(6,2,2),m));setModel(root,'demo-box');seams=sharpSeams(faces,topology,55,false);refresh();status('Demo box ready. Generate atlas to unwrap its six panels.');};
document.addEventListener('keydown',e=>{if(/INPUT|SELECT|TEXTAREA/.test(e.target.tagName))return;if(e.code==='KeyF')setMode('face');if(e.code==='KeyE')setMode('seam');if(e.code==='Home'){e.preventDefault();fit();}if((e.ctrlKey||e.metaKey)&&e.code==='KeyZ'){e.preventDefault();if(!busy)$('undo').click();}});
// A drop's file store is only readable during the event callback. Capture it
// and cancel browser navigation before handing work to the asynchronous loader.
const isFileDrag=e=>Array.from(e.dataTransfer?.types||[]).includes('Files');
document.addEventListener('dragover',e=>{if(!isFileDrag(e))return;e.preventDefault();e.dataTransfer.dropEffect='copy';document.body.classList.add('file-drag');});
document.addEventListener('dragleave',e=>{if(!e.relatedTarget)document.body.classList.remove('file-drag');});
document.addEventListener('drop',e=>{
  if(!isFileDrag(e))return;
  e.preventDefault();e.stopPropagation();document.body.classList.remove('file-drag');
  const files=Array.from(e.dataTransfer.files);
  safe(()=>{if(!files.length)throw new Error('No files received. Drop the model file itself, not its folder.');return openFiles(files);})();
});
camera.position.set(8,6,10);camera.lookAt(0,0,0);refresh();resize();
