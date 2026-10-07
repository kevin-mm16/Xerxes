import { spawn } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '..');
const output = path.join(root, 'artifacts', 'react-browser-' + Date.now());
const base = process.env.MILIFE_BASE_URL || 'https://127.0.0.1:7098';
const password = process.env.MILIFE_DASHBOARD_PASSWORD;
const requireSilent = process.env.MILIFE_REQUIRE_SILENT !== 'false';
if (!password) throw new Error('Set MILIFE_DASHBOARD_PASSWORD.');
await mkdir(output, { recursive: true });
const browser = spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', [
  '--headless=new', '--remote-debugging-port=0', '--no-first-run', '--no-default-browser-check',
  '--ignore-certificate-errors', '--disable-background-networking', '--disable-component-update',
  '--user-data-dir=' + output, 'about:blank'
], { windowsHide: true, stdio: 'ignore' });
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
let socket; let sequence = 0; const pending = new Map(); const errors = [];
try {
  let port;
  for (let attempt = 0; attempt < 60; attempt++) {
    try { port = (await readFile(path.join(output, 'DevToolsActivePort'), 'utf8')).split('\n')[0]; break; }
    catch { await sleep(250); }
  }
  if (!port) throw new Error('Headless browser did not start.');
  const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' })).json();
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  socket.onmessage = event => {
    const data = JSON.parse(event.data);
    if (data.id) { const call = pending.get(data.id); pending.delete(data.id); data.error ? call?.reject(new Error(data.error.message)) : call?.resolve(data.result); }
    else if (data.method === 'Runtime.exceptionThrown') errors.push(data.params.exceptionDetails.text);
  };
  const send = (method, params = {}) => new Promise((resolve, reject) => { const id = ++sequence; pending.set(id, { resolve, reject }); socket.send(JSON.stringify({ id, method, params })); });
  const evaluate = async expression => { const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true }); if (result.exceptionDetails) throw new Error('Browser evaluation failed.'); return result.result.value; };
  await send('Runtime.enable'); await send('Network.enable');
  await send('Network.setExtraHTTPHeaders', { headers: { 'ngrok-skip-browser-warning': 'true' } });
  await send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 980, deviceScaleFactor: 1, mobile: false });
  await send('Page.navigate', { url: base + '/device-admin' });
  for (let attempt = 0; attempt < 80; attempt++) { if (await evaluate("Boolean(document.querySelector('input[name=password]'))")) break; await sleep(250); }
  if (!await evaluate("document.querySelector('img[alt=\"MiLife Insurance\"]')?.naturalWidth > 0")) throw new Error('MiLife logo did not load.');
  await evaluate(`document.querySelector('input[name=username]').value='root';document.querySelector('input[name=username]').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('input[name=password]').value=${JSON.stringify(password)};document.querySelector('input[name=password]').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('form').requestSubmit();`);
  let loaded = false;
  for (let attempt = 0; attempt < 100; attempt++) { loaded = await evaluate("document.body.innerText.includes('Company devices') && Boolean(document.querySelector('tbody tr'))"); if (loaded) break; await sleep(300); }
  if (!loaded) throw new Error('React dashboard did not render a device row after login.');
  let live = false;
  for (let attempt = 0; attempt < 40; attempt++) { live = await evaluate("document.body.innerText.includes('Live updates connected')"); if (live) break; await sleep(250); }
  if (!live) throw new Error('Live update stream did not connect.');
  if (!await evaluate("[...document.querySelectorAll('select')].some(x => x.getAttribute('aria-label') === 'Device type')")) throw new Error('Device type filter is missing.');
  await writeFile(path.join(output, 'desktop.png'), Buffer.from((await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true })).data, 'base64'));
  await evaluate("document.querySelector('tbody tr').click()");
  for (let attempt = 0; attempt < 60; attempt++) { if (await evaluate("document.body.innerText.includes('Remote support') && document.body.innerText.includes('Agent management')")) break; await sleep(250); }
  if (requireSilent && !await evaluate("document.body.innerText.includes('Silent support ready')")) throw new Error('Silent support readiness was not shown.');
  await writeFile(path.join(output, 'device-support.png'), Buffer.from((await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true })).data, 'base64'));
  await evaluate("document.querySelector('.drawer .close').click()");
  await evaluate("document.querySelector('tbody input[type=checkbox]').click();[...document.querySelectorAll('button')].find(x => x.textContent.trim() === 'Run script').click()");
  for (let attempt = 0; attempt < 60; attempt++) { if (await evaluate("document.body.innerText.includes('Run a script') && document.body.innerText.includes('Targets')")) break; await sleep(250); }
  if (!await evaluate("document.body.innerText.includes('REACT-PREVIEW-001')")) throw new Error('Selected script target was not shown.');
  await evaluate("document.querySelector('.script-drawer .close').click()");
  await evaluate("[...document.querySelectorAll('.sidebar nav button')].find(x => x.textContent.includes('Script activity')).click()");
  for (let attempt = 0; attempt < 40; attempt++) { if (await evaluate("document.body.innerText.includes('Latest 100 commands')")) break; await sleep(250); }
  if (!await evaluate("document.body.innerText.includes('Script activity') && document.body.innerText.includes('Latest 100 commands')")) throw new Error('Global command activity did not load.');
  await evaluate("[...document.querySelectorAll('.sidebar nav button')].find(x => x.textContent.includes('Server health')).click()");
  for (let attempt = 0; attempt < 40; attempt++) { if (await evaluate("document.body.innerText.includes('API & database')")) break; await sleep(250); }
  if (!await evaluate("document.body.innerText.includes('Root disk free') && document.body.innerText.includes('Database backup')")) throw new Error('Server health cards did not load.');
  await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true }); await sleep(500);
  if (await evaluate('document.documentElement.scrollWidth > document.documentElement.clientWidth')) throw new Error('Mobile layout has horizontal page overflow.');
  await writeFile(path.join(output, 'mobile.png'), Buffer.from((await send('Page.captureScreenshot', { format: 'png' })).data, 'base64'));
  if (errors.length) throw new Error('Browser runtime errors: ' + errors.join('; '));
  console.log('PASS: production React login, logo, filters, support drawer, script selection, activity, health and mobile layout.');
  console.log('Screenshots: ' + output);
  await send('Browser.close').catch(() => {});
} finally { socket?.close(); browser.kill(); }
