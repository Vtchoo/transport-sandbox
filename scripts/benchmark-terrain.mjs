/** Same-machine C#/Chrome benchmarks of the original terrain code and native port. */
import {buildSync} from 'esbuild';
import {createServer} from 'node:http';
import {spawn,execFileSync} from 'node:child_process';
import {mkdir,readFile,writeFile,unlink} from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
const output=path.resolve('godot/.godot/performance');await mkdir(output,{recursive:true});
const optimized=process.argv.includes('--optimized');
execFileSync('dotnet',['build','godot/Transport Sandbox.csproj','--no-restore','-t:Rebuild',optimized?'-p:Optimize=true':'-p:Optimize=false'],{stdio:'inherit',windowsHide:true});
const godotPath=process.env.GODOT_PATH??'D:/Godot/Godot_v4.7.2-stable_mono_win64_console.exe';
await new Promise((resolve,reject)=>{
 const process=spawn(godotPath,['--headless','--path','godot','tests/runner.tscn','--log-file','.godot/performance/godot.log','--','--suite=performance'],{windowsHide:true,stdio:'inherit'});
 process.on('error',reject);process.on('exit',code=>code===0?resolve():reject(Error('Godot benchmark failed: '+code)));
});
const fixture=await readFile('godot/tests/earthwork-mesh-fixtures.json','utf8');
const source=buildSync({stdin:{contents:`export {TerrainGenerator,DEFAULT_SETTINGS,buildChunk} from './src/terrain/generator.ts';export {EarthworkField,prepareEarthPaths} from './src/track/earthworks.ts';export {generateTrees} from './src/terrain/vegetation.ts';`,resolveDir:process.cwd()},bundle:true,write:false,format:'esm',platform:'browser'}).outputFiles[0].text;
const worker=buildSync({entryPoints:['src/terrain/terrain.worker.ts'],bundle:true,write:false,format:'iife',platform:'browser'}).outputFiles[0].text;
const harness=`
import {TerrainGenerator,buildChunk,EarthworkField,prepareEarthPaths,generateTrees} from '/source.js';
try {
const fixture=await(await fetch('/fixture.json')).json(), generator=new TerrainGenerator(fixture.settings);
const natural=(x,z)=>generator.baseHeight(x,z);
const prepare=()=>prepareEarthPaths(fixture.paths,natural,fixture.settings.mountainHeight);
const prepared=prepare(), field=new EarthworkField(prepared),results={};
async function measure(name,work){for(let i=0;i<2;i++)await work();const times=[];let checksum;for(let i=0;i<7;i++){const start=performance.now();checksum=await work();times.push(performance.now()-start);}results[name]={samples_ms:times,checksum};}
await measure('height_10000',()=>{let sum=0;for(let i=0;i<10000;i++)sum+=natural(600+i%100*8,900+Math.floor(i/100)*8);return sum;});
await measure('trees_chunk',()=>generateTrees(generator,1,1).length);
await measure('prepare_shoulders',()=>prepare().reduce((n,p)=>n+p.points.reduce((s,v)=>s+v.left+v.right,0),0));
for(const [name,segments,earth] of [['natural_64',64,false],['natural_128',128,false],['earthwork_64',64,true],['earthwork_16',16,true]]){
generator.earthworks=earth?field:undefined;
await measure(name,()=>{const data=buildChunk(generator,{id:1,x:1,z:1,segments,settings:fixture.settings});return data.positions.length/3+data.indices.length;});}
const workers=Array.from({length:3},()=>new Worker('/worker.js'));
function batch(earth){const keys=earth?[[0,1],[1,1]]:Array.from({length:9},(_,i)=>[i%3,Math.floor(i/3)]);let next=0,done=0,checksum=0;
return new Promise((resolve,reject)=>{const send=worker=>{if(next>=keys.length)return;const[x,z]=keys[next++];worker.postMessage({id:next,x,z,segments:64,settings:fixture.settings,earthPaths:earth?prepared:[],revision:earth?1:0});};for(const worker of workers){worker.onerror=reject;worker.onmessage=({data})=>{checksum+=data.positions.length/3+data.indices.length;if(++done===keys.length)resolve(checksum);else send(worker);};send(worker);}});}
await measure('workers_9_natural',()=>batch(false));await measure('workers_2_earthwork',()=>batch(true));workers.forEach(w=>w.terminate());
window.benchmarkResults={runtime:navigator.userAgent,hardwareConcurrency:navigator.hardwareConcurrency,warmups:2,repeats:7,workers:3,results};
}catch(error){window.benchmarkError=error.stack??String(error);}
`;
const server=createServer((req,res)=>{const routes={'/source.js':source,'/worker.js':worker,'/harness.js':harness,'/fixture.json':fixture};res.setHeader('Content-Type',req.url?.endsWith('.json')?'application/json':req.url?.endsWith('.js')?'text/javascript':'text/html');res.end(routes[req.url]??'<script type="module" src="/harness.js"></script>');});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const profile=path.join(output,'chrome-profile');await mkdir(profile,{recursive:true});await unlink(path.join(profile,'DevToolsActivePort')).catch(e=>{if(e.code!=='ENOENT')throw e;});
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
 for(let i=0;i<1200;i++){const r=await command('Runtime.evaluate',{expression:'({results:window.benchmarkResults,error:window.benchmarkError})',returnByValue:true});if(r.result.value?.error)throw Error(r.result.value.error);if(r.result.value?.results){browser=r.result.value.results;break;}await new Promise(r=>setTimeout(r,100));}
 if(!browser)throw Error('Browser benchmark timed out');
 await writeFile(path.join(output,'browser.json'),JSON.stringify(browser,null,2)+'\n');
 const native=JSON.parse(await readFile(path.join(output,'godot.json'),'utf8'));
 await writeFile(path.join(output,optimized?'godot-optimized.json':'godot-debug.json'),JSON.stringify(native,null,2)+'\n');
 const median=s=>s.toSorted((a,b)=>a-b)[Math.floor(s.length/2)];
 const rows=[];
 for(const [name,a] of Object.entries(native.results)){
  const b=browser.results[name];if(Math.abs(a.checksum-b.checksum)>Math.max(.00001,Math.abs(a.checksum)*1e-10))throw Error('Workload checksum mismatch: '+name+' '+a.checksum+' / '+b.checksum);
  rows.push({name,godot_ms:median(a.samples_ms),chrome_ms:median(b.samples_ms),chrome_over_godot:median(b.samples_ms)/median(a.samples_ms)});
 }
 let debug;try{debug=JSON.parse(await readFile(path.join(output,'godot-debug.json'),'utf8'));}catch{}
 const metadata={cpu:os.cpus()[0].model,logicalProcessors:os.cpus().length,memoryGiB:os.totalmem()/1024**3,os:os.platform()+' '+os.release()+' '+os.arch()};
 await writeFile(path.join(output,'comparison.json'),JSON.stringify({metadata,native,browser,debug,rows},null,2)+'\n');
 await mkdir('docs/performance',{recursive:true});
 const report=['# Terrain and earthwork performance — 2026-11-18','',
 'Measured sequentially on the same machine: '+metadata.cpu+', '+metadata.logicalProcessors+' logical processors, '+metadata.memoryGiB.toFixed(1)+' GiB RAM; '+metadata.os+'.',
 '', 'Godot '+native.godot+' / '+native.runtime+' / '+native.configuration+'. Browser: '+browser.runtime+'. Chrome runs the original TypeScript bundled with esbuild, in an actual headless browser. No Node.js timings are substituted for browser timings.',
 '', 'Two warmups, then seven timed runs per case; table shows median milliseconds. Same seed, settings, source fixture paths and chunk coordinates. Workload checksums must agree. Runs are sequential across engines to avoid competing benchmark loads.',
 '', '| Workload | Godot current run (ms) | Godot Debug baseline (ms) | Chrome (ms) | Chrome / current Godot |','|---|---:|---:|---:|---:|',
 ...rows.map(r=>'| '+r.name+' | '+r.godot_ms.toFixed(2)+' | '+(debug?median(debug.results[r.name].samples_ms).toFixed(2):'?')+' | '+r.chrome_ms.toFixed(2)+' | '+r.chrome_over_godot.toFixed(2)+'× |'),
 '', 'A ratio above 1 means Godot was faster. These are CPU/job benchmarks, not FPS or complete track-edit latency.',
 '', 'Single-mesh cases build vertex/normal/color/index buffers without vegetation or GPU uploads. Both native and browser use their real buffer-building paths; Godot also converts vertex colors for its rendering pipeline. Adaptive cases contain both cut and fill. Shoulder preparation is measured separately.',
 '', 'Worker cases use three warm workers and include production chunk jobs, vegetation, result transfer/packing and scheduling. They exclude GPU upload, scene/instance updates, edit debounce and shoulder preparation. The browser worker also filters trees against earthworks and seats roots on regular mesh triangles; native clearance is applied separately in TerrainWorld. Consequently worker totals are representative pipeline timings, not identical instruction workloads.',
 '', 'This is one machine and a small workload set. Debug C# results do not establish release-build performance, large-network performance or graphics performance. No statistical significance is claimed from seven samples.',
 '', 'Reproduce: `node scripts/benchmark-terrain.mjs`, then `node scripts/benchmark-terrain.mjs --optimized`. The optimized pass enables C# compiler/JIT optimization in the same Godot assembly and restores the ordinary development build afterward; it is not a packaged export. Raw samples and runtime metadata: `godot/.godot/performance/comparison.json` (ignored local output). The script uses installed Chrome and Godot; override CHROME_PATH/GODOT_PATH if needed.', ''];
 await writeFile('docs/performance/terrain-2026-11-18.md',report.join('\n'));await writeFile('docs/performance/terrain-2026-11-18.json',JSON.stringify({metadata,native,browser,debug,rows},null,2)+'\n');console.table(rows);
}finally{if(socket?.readyState===WebSocket.OPEN){socket.send(JSON.stringify({id:++id,method:'Browser.close'}));await new Promise(r=>setTimeout(r,300));socket.close();}chrome.kill();server.close();if(optimized)execFileSync('dotnet',['build','godot/Transport Sandbox.csproj','--no-restore','-t:Rebuild','-p:Optimize=false'],{stdio:'inherit',windowsHide:true});}
