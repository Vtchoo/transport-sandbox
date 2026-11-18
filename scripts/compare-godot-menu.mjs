/** Capture the original browser and native Godot menus at identical sizes/presets/time. */
import {createServer} from 'vite';
import {spawn} from 'node:child_process';
import {mkdir,readFile,writeFile,unlink} from 'node:fs/promises';
import path from 'node:path';
const options=Object.fromEntries(process.argv.slice(2).map(s=>s.replace(/^--/,'').split('=')));
const preset=Number(options.preset??2), state=options.state??'menu';
if(![0,1,2].includes(preset)||!['menu','recommended','advanced','game','build','view','settings'].includes(state))throw Error('Invalid capture options');
const gameplay=['game','build','view','settings'].includes(state);
const [width,height]=(options.size??'1280x800').split('x').map(Number);
const output=path.resolve('godot/.godot/menu-review');await mkdir(output,{recursive:true});
const profile=path.join(output,'chrome-profile');await mkdir(profile,{recursive:true});
const server=await createServer({server:{host:'127.0.0.1',port:5187,strictPort:true},plugins:[{name:'fixed-menu-preset',transform(code,id){if(id.endsWith('/src/menu-scene.ts'))return code.replace('Math.floor(Math.random() * MENU_TRAINS.length)',String(preset));}}]});
let chrome, socket, godot;
const pending=new Map();let nextId=0;const errors=[];
const command=(method,params={})=>new Promise((resolve,reject)=>{
 const id=++nextId;const timer=setTimeout(()=>{pending.delete(id);reject(Error('CDP timed out: '+method));},20000);
 pending.set(id,{resolve:v=>{clearTimeout(timer);resolve(v);},reject});socket.send(JSON.stringify({id,method,params}));
});
try{
 await server.listen();
 await unlink(path.join(profile,'DevToolsActivePort')).catch(error=>{if(error.code!=='ENOENT')throw error;});
 chrome=spawn(process.env.CHROME_PATH??'C:/Program Files/Google/Chrome/Application/chrome.exe',['--headless=new','--no-first-run','--disable-background-networking','--user-data-dir='+profile,'--remote-debugging-port=0','about:blank'],{windowsHide:true,stdio:'ignore'});
 chrome.on('error',error=>errors.push(String(error)));
 // Poll only our browser's own debugging-port file.
 let port;
 for(let i=0;i<100;i++){try{port=Number((await readFile(path.join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0]);if(port)break;}catch{}await new Promise(r=>setTimeout(r,100));}
 if(!port)throw Error('Chrome did not expose its local debug port');
 const targets=await(await fetch('http://127.0.0.1:'+port+'/json/list')).json();
 socket=new WebSocket(targets.find(t=>t.type==='page').webSocketDebuggerUrl);
 await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
 socket.onmessage=event=>{const m=JSON.parse(event.data);if(m.id&&pending.has(m.id)){const p=pending.get(m.id);pending.delete(m.id);if(m.error)p.reject(Error(JSON.stringify(m.error)));else p.resolve(m.result);}else if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails);};
 await command('Page.enable');await command('Runtime.enable');
 await command('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
 await command('Page.addScriptToEvaluateOnNewDocument',{source:'localStorage.clear();const frame=requestAnimationFrame;window.requestAnimationFrame=callback=>frame.call(window,()=>callback(0));'});
 await command('Page.navigate',{url:'http://127.0.0.1:5187/'});
 for(let i=0;i<100;i++){
  const r=await command('Runtime.evaluate',{expression:'document.querySelector("#new-game-open") && document.querySelector("#world")?.width>0 && document.readyState==="complete"',returnByValue:true});
  if(r.result.value)break;await new Promise(r=>setTimeout(r,100));
 }
 await new Promise(r=>setTimeout(r,600));
 if(state!=='menu')await command('Runtime.evaluate',{expression:'document.querySelector("#new-game-open").click()'});
 if(gameplay){
  await command('Runtime.evaluate',{expression:'document.querySelector("#start").click()'});
  for(let i=0;i<150;i++){
   const r=await command('Runtime.evaluate',{expression:'!document.querySelector("#build-track").disabled',returnByValue:true});
   if(r.result.value)break;await new Promise(r=>setTimeout(r,100));
  }
  if(state==='build')await command('Runtime.evaluate',{expression:'document.querySelector("#build-track").click()'});
  if(state==='view')await command('Runtime.evaluate',{expression:'document.querySelector("#view-options-open").click()'});
  if(state==='settings')await command('Runtime.evaluate',{expression:'document.querySelector(".settings-open").click()'});
 }
 if(state==='advanced')await command('Runtime.evaluate',{expression:'const custom=document.querySelector("input[value=advanced]");custom.checked=true;custom.dispatchEvent(new Event("change",{bubbles:true}));'});
 const metrics=await command('Runtime.evaluate',{expression:'Object.fromEntries([["title","#menu h1"],["start","#new-game-open"],["dialog","#new-game"]].map(([key,selector])=>[key,document.querySelector(selector).getBoundingClientRect().toJSON()]))',returnByValue:true});
 const prefix=path.join(output,'browser-'+preset+'-'+width+'x'+height+'-'+state);
 const shot=await command('Page.captureScreenshot',{format:'png'});
 await writeFile(prefix+'.png',Buffer.from(shot.data,'base64'));await writeFile(prefix+'.json',JSON.stringify(metrics.result.value,null,2));
 if(errors.length)throw Error('Browser errors: '+JSON.stringify(errors));
 godot=spawn(process.env.GODOT_PATH??'D:/Godot/Godot_v4.7.2-stable_mono_win64_console.exe',[
  '--path','godot','--rendering-method','gl_compatibility','--resolution',width+'x'+height,'--position','-10000,-10000','--audio-driver','Dummy',
  'tests/runner.tscn','--log-file','.godot/menu-review/capture.log','--','--suite=capture','--preset='+preset,'--state='+state,...(options.hover?['--hover='+options.hover]:[])
 ],{windowsHide:true,stdio:['ignore','pipe','pipe']});
 let logs='';godot.stdout.on('data',d=>logs+=d);godot.stderr.on('data',d=>logs+=d);
 let timer;try{await Promise.race([new Promise((resolve,reject)=>{godot.on('error',reject);godot.on('exit',code=>code===0?resolve():reject(Error(logs)));}),new Promise((_,reject)=>{timer=setTimeout(()=>reject(Error('Godot capture timed out: '+logs)),45000);})]);}finally{clearTimeout(timer);}
 if(/SCRIPT ERROR|SHADER ERROR|Error compiling shader|Missing exported/.test(logs))throw Error(logs);
 console.log('Captured browser and Godot: '+preset+' / '+width+'x'+height+' / '+state+'\n'+output);
}finally{
 if(socket?.readyState===WebSocket.OPEN){socket.send(JSON.stringify({id:++nextId,method:'Browser.close'}));await new Promise(resolve=>setTimeout(resolve,300));socket.close();}
 chrome?.kill();godot?.kill();await server.close();
}
