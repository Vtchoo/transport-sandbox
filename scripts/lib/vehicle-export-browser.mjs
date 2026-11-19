import { buildSync } from 'esbuild';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdir, readFile, unlink } from 'node:fs/promises';
import path from 'node:path';

/** A private, local browser page for Canvas2D-based model exports; no game boot. */
export async function vehicleExportBrowser(entry = 'scripts/lib/browser-vehicle-export.ts') {
  const { outputFiles } = buildSync({ entryPoints: [entry], bundle: true, write: false, format: 'esm', platform: 'browser' });
  const bundle = outputFiles[0].text;
  const server = createServer((request, response) => {
    response.setHeader('Content-Type', request.url === '/export.js' ? 'text/javascript' : 'text/html');
    response.end(request.url === '/export.js' ? bundle : '<!doctype html><title>Vehicle export</title>');
  });
  let chrome, socket;
  const pending = new Map();
  let nextId = 0;
  const close = async () => {
    if (socket?.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify({ id: ++nextId, method: 'Browser.close' }));
      socket.close();
    }
    chrome?.kill();
    await new Promise(resolve => server.close(resolve));
  };
  try {
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
    const url = `http://127.0.0.1:${server.address().port}`;
    const profile = path.resolve('godot/.godot/vehicle-export-browser');
    await mkdir(profile, { recursive: true });
    await unlink(path.join(profile, 'DevToolsActivePort')).catch(error => { if (error.code !== 'ENOENT') throw error; });
    let spawnError;
    chrome = spawn(process.env.CHROME_PATH ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe', [
      '--headless=new', '--no-first-run', '--disable-background-networking', '--user-data-dir=' + profile, '--remote-debugging-port=0', 'about:blank',
    ], { windowsHide: true, stdio: 'ignore' });
    chrome.on('error', error => { spawnError = error; });
    let port;
    for (let i = 0; i < 100; i++) {
      if (spawnError) throw spawnError;
      try { port = Number((await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]); if (port) break; } catch {}
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    if (!port) throw Error('Chrome did not expose its local debug port (set CHROME_PATH if needed).');
    const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    socket = new WebSocket(targets.find(target => target.type === 'page').webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
    socket.onmessage = event => {
      const message = JSON.parse(event.data), waiting = pending.get(message.id);
      if (!waiting) return;
      pending.delete(message.id);
      if (message.error) waiting.reject(Error(JSON.stringify(message.error))); else waiting.resolve(message.result);
    };
    const command = (method, params = {}) => new Promise((resolve, reject) => {
      const id = ++nextId;
      const timer = setTimeout(() => { pending.delete(id); reject(Error(`Browser export timed out: ${method}`)); }, 30000);
      pending.set(id, { resolve: value => { clearTimeout(timer); resolve(value); }, reject: error => { clearTimeout(timer); reject(error); } });
      socket.send(JSON.stringify({ id, method, params }));
    });
    await command('Page.enable');
    await command('Page.navigate', { url });
    const evaluate = async expression => {
      const result = await command('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
      if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
      return result.result.value;
    };
    // Wait for navigation before importing the same-origin module.
    for (let i = 0; i < 100; i++) {
      if (await evaluate(`location.origin === ${JSON.stringify(url)} && document.readyState === 'complete'`)) break;
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    await evaluate(`import('/export.js').then(module => { window.exportModule = module; })`);
    return { exportVehicle: (id, gauge) => evaluate(`window.exportModule.exportVehicle(${JSON.stringify(id)}, ${gauge})`), evaluate, close };
  } catch (error) {
    await close();
    throw error;
  }
}
