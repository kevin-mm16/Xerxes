import {spawn} from 'node:child_process';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';

const base=process.env.MILIFE_BASE_URL||'https://uphill-cofounder-trident.ngrok-free.dev';
const password=process.env.MILIFE_DASHBOARD_PASSWORD;
if(!password)throw new Error('Set MILIFE_DASHBOARD_PASSWORD.');
const root=path.resolve(import.meta.dirname,'..');
const directory=path.join(root,'artifacts','preview-browser-'+Date.now());
await mkdir(directory,{recursive:true});
const browser=spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',[
  '--headless=new','--remote-debugging-port=0','--no-first-run','--no-default-browser-check',
  '--disable-background-networking','--disable-component-update','--user-data-dir='+directory,'about:blank'
],{windowsHide:true,stdio:'ignore'});
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
let socket;let sequence=0;const pending=new Map();const runtimeErrors=[];
try{
  let port;
  for(let attempt=0;attempt<60;attempt++){try{port=(await readFile(path.join(directory,'DevToolsActivePort'),'utf8')).split('\n')[0];break;}catch{await sleep(500);}}
  if(!port)throw new Error('Headless browser did not start.');
  const target=await(await fetch(`http://127.0.0.1:${port}/json/new?about:blank`,{method:'PUT'})).json();
  socket=new WebSocket(target.webSocketDebuggerUrl);await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
  socket.onmessage=event=>{const data=JSON.parse(event.data);if(data.id){const item=pending.get(data.id);pending.delete(data.id);if(data.error)item?.reject(new Error(data.error.message));else item?.resolve(data.result);}else if(data.method==='Runtime.exceptionThrown')runtimeErrors.push(data.params.exceptionDetails.exception?.description||data.params.exceptionDetails.text);};
  const send=(method,params={})=>new Promise((resolve,reject)=>{const id=++sequence;pending.set(id,{resolve,reject});socket.send(JSON.stringify({id,method,params}));});
  const evaluate=async expression=>{const response=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(response.exceptionDetails)throw new Error(response.exceptionDetails.exception?.description||'Browser evaluation failed.');return response.result.value;};
  const waitFor=async(expression,description,attempts=80)=>{for(let i=0;i<attempts;i++){if(await evaluate(expression))return;await sleep(500);}throw new Error('Timed out waiting for '+description);};
  const screenshot=async name=>writeFile(path.join(directory,name),Buffer.from((await send('Page.captureScreenshot',{format:'png'})).data,'base64'));
  await send('Runtime.enable');await send('Network.enable');
  await send('Network.setExtraHTTPHeaders',{headers:{'ngrok-skip-browser-warning':'true'}});
  await send('Emulation.setDeviceMetricsOverride',{width:1440,height:1100,deviceScaleFactor:1,mobile:false});
  await send('Page.navigate',{url:base+'/device-admin/preview'});
  await waitFor('Boolean(document.querySelector("[name=username]"))','preview login');
  await evaluate(`document.querySelector('[name=username]').value='root';document.querySelector('[name=password]').value=${JSON.stringify(password)};document.getElementById('login-form').requestSubmit();`);
  await waitFor('Boolean(!document.getElementById("workspace").hidden && document.querySelector("#device-rows tr"))','device list');
  if(!await evaluate('document.querySelector(".preview-pill")?.textContent==="PREVIEW"'))throw new Error('Preview marker missing.');
  await screenshot('preview-dashboard.png');
  await evaluate(`document.getElementById('search').value='PREVIEW-COMMAND-TEST';document.getElementById('filters').requestSubmit();`);
  await waitFor('document.querySelectorAll("#device-rows tr").length===1','preview test device');
  await evaluate('document.querySelector("#device-rows button").click()');
  await waitFor('document.querySelectorAll(".command-card").length>=6','command catalogue');
  if(!await evaluate('document.querySelector(".support-readiness")?.textContent==="Silent support ready"'))throw new Error('Silent-support readiness was not shown.');
  await screenshot('preview-support.png');
  await evaluate('document.querySelector(".command-card").click();document.querySelector(".mode-row .primary").click()');
  await waitFor('document.querySelector(".command-status")?.textContent==="Queued"','queued command');
  await evaluate('document.querySelector(".command-record .compact").click()');
  await waitFor('document.querySelector(".command-status")?.textContent==="Cancelled"','cancelled command');
  await evaluate('[...document.querySelectorAll(".support-tab")].find(button=>button.textContent.includes("Advanced"))?.click()');
  if(!await evaluate('Boolean(document.querySelector(".support-panel:not([hidden]) textarea"))'))throw new Error('Advanced PowerShell editor missing.');
  await screenshot('preview-advanced.png');
  await evaluate('document.getElementById("close-detail").click()');
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});await sleep(500);
  await screenshot('preview-mobile.png');
  await evaluate('document.getElementById("logout").click()');
  await waitFor('!document.getElementById("login-screen").hidden','logout');
  if(runtimeErrors.length)throw new Error('Browser runtime errors: '+runtimeErrors.join('; '));
  console.log('PASS: preview login, device list, command catalogue, silent readiness, queue/cancel, advanced editor, responsive layout and logout.');
  console.log('Screenshots: '+directory);
  await send('Browser.close').catch(()=>{});
}finally{socket?.close();browser.kill();}
