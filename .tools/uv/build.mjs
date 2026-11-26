// Produce the single, offline HTML file users open directly. No runtime server.
import {createRequire} from 'node:module';
import {fileURLToPath} from 'node:url';
import {readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';
const here=path.dirname(fileURLToPath(import.meta.url)),root=path.resolve(here,'../..');
const require=createRequire(path.join(root,'web/package.json'));
const {build}=require('esbuild');
const common={bundle:true,write:false,format:'iife',platform:'browser',target:'es2022',minify:true,legalComments:'inline',nodePaths:[path.join(root,'web/node_modules')]};
const worker=await build({...common,entryPoints:[path.join(here,'worker.mjs')]});
const app=await build({...common,entryPoints:[path.join(here,'app.mjs')],plugins:[{
  name:'inline-worker',setup(plugin){
    plugin.onResolve({filter:/worker\.mjs\?inline$/},()=>({path:'uv-worker',namespace:'inline-worker'}));
    plugin.onLoad({filter:/.*/,namespace:'inline-worker'},()=>({contents:`export default ${JSON.stringify(worker.outputFiles[0].text)};`,loader:'js'}));
  }
}]});
const template=await readFile(path.join(here,'uv.template.html'),'utf8'),css=await readFile(path.join(here,'uv.css'),'utf8');
const html=template.replace('<!-- UV_STYLES -->',()=>`<style>${css}</style>`).replace('<!-- UV_SCRIPT -->',()=>`<script>${app.outputFiles[0].text.replace(/<\/script/gi,'<\\/script')}</script>`);
await writeFile(path.join(here,'uv.html'),html);
console.log(`Built standalone uv.html (${(Buffer.byteLength(html)/1024).toFixed(0)} KiB). Open it directly in a browser.`);
