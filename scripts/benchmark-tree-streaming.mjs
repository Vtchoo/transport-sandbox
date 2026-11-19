/** Actual Chrome/WebGL CPU submission benchmark; excludes GPU completion and placement work. */
import {buildSync} from 'esbuild';
import {createServer} from 'node:http';
import {spawn} from 'node:child_process';
import {mkdir,readFile,writeFile,unlink} from 'node:fs/promises';
import path from 'node:path';
const output=path.resolve('godot/.godot/performance');await mkdir(output,{recursive:true});
const source=buildSync({stdin:{contents:`export * as THREE from 'three';export {Trees} from './src/terrain/trees.ts';export {prepareTrees} from './src/terrain/tree-data.ts';export {TreeStreaming} from './src/terrain/tree-streaming.ts';export {TerrainWorld} from './src/terrain/world.ts';export {DEFAULT_SETTINGS} from './src/terrain/generator.ts';`,resolveDir:process.cwd()},bundle:true,write:false,format:'esm',platform:'browser'}).outputFiles[0].text;
const worker=buildSync({entryPoints:['src/terrain/terrain.worker.ts'],bundle:true,write:false,format:'esm',platform:'browser'}).outputFiles[0].text;
const harness=`
import {THREE,Trees,prepareTrees,TreeStreaming,TerrainWorld,DEFAULT_SETTINGS} from '/source.js';
try {
const trees=new Trees(), data=new Float32Array(2048*7);
for(let i=0;i<2048;i++)data.set([(i%64)*8,100,Math.floor(i/64)*8,15+i%10,3+i%3,i*.71,i%2],i*7);
const prepared=prepareTrees(data), scene=new THREE.Scene(),camera=new THREE.PerspectiveCamera(60,1,1,4000);
camera.position.set(600,500,600);camera.lookAt(256,100,128);scene.add(new THREE.HemisphereLight(0xffffff,0x555555,3));
const renderer=new THREE.WebGLRenderer();renderer.setSize(600,600);document.body.append(renderer.domElement);
// Original per-instance creation path, using the same unchanged geometry and materials.
function legacyCreate(){
 const group=new THREE.Group(),meshes=[],transform=new THREE.Object3D(),color=new THREE.Color();
 for(let species=0;species<2;species++){
  const indices=[];for(let i=0;i<data.length;i+=7)if(data[i+6]===species)indices.push(i);
  const trunk=new THREE.InstancedMesh(trees.trunk,trees.bark,indices.length),crown=new THREE.InstancedMesh(trees.crowns[species],trees.leaves,indices.length);
  indices.forEach((i,index)=>{
   const[x,y,z,height,radius,yaw]=data.subarray(i,i+7);
   transform.position.set(x,y-.8,z);transform.rotation.set(0,yaw,0);transform.scale.set(height,height,height);transform.updateMatrix();trunk.setMatrixAt(index,transform.matrix);
   transform.scale.set(radius,height,radius);transform.updateMatrix();crown.setMatrixAt(index,transform.matrix);
   color.setHSL((species ? .32 : .28)+Math.sin(yaw)*.018,.42+Math.cos(yaw)*.06,species ? .14 : .19);crown.setColorAt(index,color);
  });
  for(const mesh of[trunk,crown]){mesh.computeBoundingSphere();mesh.computeBoundingBox();meshes.push(mesh);group.add(mesh);}
 }
 return{group,meshes,count:2048};
}
const results={};
function measure(name,create,release){
 const samples=[];
 for(let i=0;i<30;i++){
  const start=performance.now(),chunk=create();scene.add(chunk.group);const elapsed=performance.now()-start;
  renderer.render(scene,camera);if(i>=5)samples.push(elapsed);release(chunk);
 }
 const ordered=samples.toSorted((a,b)=>a-b);results[name]={median_ms:ordered[12],p95_ms:ordered[23],samples_ms:samples};
}
measure('before_create_attach',legacyCreate,chunk=>trees.release(chunk));
measure('after_prepared_create_attach',()=>trees.createPrepared(prepared),chunk=>trees.release(chunk));
const stream=new TreeStreaming(trees);
measure('after_cached_return',()=>stream.activate('0,0',scene,prepared,false),()=>stream.deactivate('0,0'));
const moved=data.slice();for(let i=1;i<moved.length;i+=7)moved[i]+=1;
const alternate=prepareTrees(moved);let count=0;
measure('after_lod_reuse',()=>stream.activate('0,0',scene,(++count%2)?alternate:prepared,false),()=>stream.deactivate('0,0'));
const gl=renderer.getContext(),debug=gl.getExtension('WEBGL_debug_renderer_info');
const benchmarkResults={runtime:navigator.userAgent,renderer:debug?gl.getParameter(debug.UNMASKED_RENDERER_WEBGL):gl.getParameter(gl.RENDERER),trees:2048,warmups:5,repeats:25,scope:'CPU create/attach submission only; rendered after timing; no GPU completion/FPS claim',results};
stream.dispose();trees.dispose();
const world=new TerrainWorld(DEFAULT_SETTINGS);scene.add(world.group);
const altitude=world.generator.height(256,256)+250;
let frames=0;
while(frames++<600){
 await new Promise(requestAnimationFrame);world.update(256,256,altitude);renderer.render(scene,camera);
 if(world.error)throw Error(world.error);
 if(world.stats.nearby>=9&&world.stats.trees>0)break;
}
const stats=world.stats;world.dispose();
if(stats.nearby<9||stats.trees===0)throw Error('Real-worker world streaming did not become ready: '+JSON.stringify(stats));
if(gl.getError()!==gl.NO_ERROR)throw Error('WebGL error after streamed tree uploads');
benchmarkResults.smoke={frames,stats};window.benchmarkResults=benchmarkResults;renderer.dispose();
}catch(error){window.benchmarkError=error.stack??String(error);}
`;
const server=createServer((req,res)=>{const routes={'/source.js':source,'/harness.js':harness,'/terrain.worker.ts':worker};res.setHeader('Content-Type',req.url?.endsWith('.json')?'application/json':(req.url?.endsWith('.js')||req.url?.endsWith('.ts'))?'text/javascript':'text/html');res.end(routes[req.url]??'<script type="module" src="/harness.js"></script>');});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const profile=path.join(output,'tree-chrome-profile');await mkdir(profile,{recursive:true});await unlink(path.join(profile,'DevToolsActivePort')).catch(e=>{if(e.code!=='ENOENT')throw e;});
const chrome=spawn(process.env.CHROME_PATH??'C:/Program Files/Google/Chrome/Application/chrome.exe',['--headless=new','--no-first-run','--disable-background-networking','--user-data-dir='+profile,'--remote-debugging-port=0','about:blank'],{windowsHide:true,stdio:'ignore'});
let socket;const pending=new Map();let id=0;
function command(method,params={}){return new Promise((resolve,reject)=>{const key=++id;pending.set(key,{resolve,reject});socket.send(JSON.stringify({id:key,method,params}));});}
try{
 let port;for(let i=0;i<100;i++){try{port=Number((await readFile(path.join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0]);if(port)break;}catch{}await new Promise(r=>setTimeout(r,100));}
 if(!port)throw Error('Chrome debugging port unavailable');
 const targets=await(await fetch('http://127.0.0.1:'+port+'/json/list')).json();socket=new WebSocket(targets.find(t=>t.type==='page').webSocketDebuggerUrl);
 await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
 socket.onmessage=event=>{const m=JSON.parse(event.data);if(m.id&&pending.has(m.id)){const p=pending.get(m.id);pending.delete(m.id);m.error?p.reject(Error(JSON.stringify(m.error))):p.resolve(m.result);}};
 await command('Page.enable');await command('Runtime.enable');await command('Page.navigate',{url:'http://127.0.0.1:'+server.address().port});
 let browser;
 for(let i=0;i<600;i++){const r=await command('Runtime.evaluate',{expression:'({results:window.benchmarkResults,error:window.benchmarkError})',returnByValue:true});if(r.result.value?.error)throw Error(r.result.value.error);if(r.result.value?.results){browser=r.result.value.results;break;}await new Promise(r=>setTimeout(r,100));}
 if(!browser)throw Error('Browser benchmark timed out');
 await writeFile(path.join(output,'browser-tree-streaming.json'),JSON.stringify(browser,null,2)+'\n');
 console.log(JSON.stringify(browser,null,2));
}finally{socket?.close();chrome.kill();server.close();}
