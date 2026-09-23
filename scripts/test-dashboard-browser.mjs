import {spawn} from 'node:child_process';
import {mkdir,readFile,writeFile,readdir} from 'node:fs/promises';
import path from 'node:path';
const root=path.resolve(import.meta.dirname,'..');
const directory=path.join(root,'artifacts','dashboard-browser-'+Date.now());
await mkdir(directory,{recursive:true});
const password=process.env.MILIFE_DASHBOARD_PASSWORD;
if(!password) throw new Error('Set MILIFE_DASHBOARD_PASSWORD for this browser acceptance test.');
const browser=spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',[
  '--headless=new','--remote-debugging-port=0','--no-first-run','--no-default-browser-check',
  '--disable-background-networking','--disable-component-update','--user-data-dir='+directory,'about:blank'
],{windowsHide:true,stdio:'ignore'});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
let socket;let seq=0;const pending=new Map();const errors=[];
try{
  let port;
  for(let i=0;i<60;i++){try{port=(await readFile(path.join(directory,'DevToolsActivePort'),'utf8')).split('\n')[0];break;}catch{await sleep(500);}}
  if(!port)throw new Error('Headless browser did not start.');
  const target=await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`,{method:'PUT'})).json();
  socket=new WebSocket(target.webSocketDebuggerUrl);await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
  socket.onmessage=event=>{const data=JSON.parse(event.data);if(data.id){const p=pending.get(data.id);pending.delete(data.id);if(data.error)p?.reject(new Error(data.error.message));else p?.resolve(data.result);}else if(data.method==='Runtime.exceptionThrown')errors.push(data.params.exceptionDetails.text);};
  const send=(method,params={})=>new Promise((resolve,reject)=>{const id=++seq;pending.set(id,{resolve,reject});socket.send(JSON.stringify({id,method,params}));});
  const evaluate=async expression=>{const result=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(result.exceptionDetails)throw new Error('Browser evaluation failed.');return result.result.value;};
  await send('Runtime.enable');await send('Network.enable');
  await send('Network.setExtraHTTPHeaders',{headers:{'ngrok-skip-browser-warning':'true'}});
  await send('Emulation.setDeviceMetricsOverride',{width:1440,height:1020,deviceScaleFactor:1,mobile:false});
  await send('Page.navigate',{url:'https://uphill-cofounder-trident.ngrok-free.dev/device-registration'});
  for(let i=0;i<60;i++){if(await evaluate('Boolean(document.querySelector("a.button"))'))break;await sleep(500);}
  if(!await evaluate('document.querySelector("a.button")?.textContent === "Click to download"'))throw new Error('Registration button missing');
  await writeFile(path.join(directory,'registration-page.png'),Buffer.from((await send('Page.captureScreenshot',{format:'png'})).data,'base64'));
  await send('Page.navigate',{url:'https://uphill-cofounder-trident.ngrok-free.dev/device-admin'});
  for(let i=0;i<60;i++){if(await evaluate('Boolean(document.getElementById("login-form") && window.state === undefined && document.querySelector("#login-form button"))'))break;await sleep(500);}
  await sleep(2000);
  if(!await evaluate('Boolean(document.querySelector("[name=username]"))')) {
    console.log(await evaluate('JSON.stringify({url:location.href,title:document.title,body:document.body?.innerText?.slice(0,1200)})'));
    await writeFile(path.join(directory,'navigation-failure.png'),Buffer.from((await send('Page.captureScreenshot',{format:'png'})).data,'base64'));
    throw new Error('Login form was not available after navigation.');
  }
  await evaluate(`document.querySelector('[name=username]').value='root';document.querySelector('[name=password]').value=${JSON.stringify(password)};document.getElementById('login-form').requestSubmit();`);
  let ready=false;
  for(let i=0;i<90;i++){ready=await evaluate('Boolean(document.getElementById("workspace") && !document.getElementById("workspace").hidden && document.querySelector("#device-rows tr"))');if(ready)break;await sleep(500);}
  if(!ready){const error=await evaluate('document.getElementById("login-error")?.textContent || document.getElementById("page-error")?.textContent');throw new Error('Dashboard login/list failed: '+error);}
  await sleep(600);
  await writeFile(path.join(directory,'dashboard-desktop.png'),Buffer.from((await send('Page.captureScreenshot',{format:'png'})).data,'base64'));
  await send('Browser.setDownloadBehavior',{behavior:'allow',downloadPath:directory});
  await evaluate('document.getElementById("export").click()');
  let csvFile;
  for(let i=0;i<60;i++){csvFile=(await readdir(directory)).find(name=>name.endsWith('.csv'));if(csvFile)break;await sleep(500);}
  if(!csvFile)throw new Error('CSV export did not download');
  const csv=await readFile(path.join(directory,csvFile),'utf8');
  if(!csv.includes('Computer name,Serial number,Employee name')||csv.trim().split('\n').length<2)throw new Error('CSV export is missing device rows');
  const count=await evaluate('document.querySelectorAll("#device-rows tr").length');
  await evaluate('document.querySelector("#device-rows button").click()');
  let details=false;
  for(let i=0;i<60;i++){details=await evaluate('document.getElementById("detail-body").textContent.includes("Hardware & Windows")');if(details)break;await sleep(500);}
  if(!details)throw new Error('Device details did not load.');
  if(!await evaluate('document.getElementById("detail-body").textContent.includes("Allow re-enable")'))throw new Error('Recovery button missing');
  if(!await evaluate('document.getElementById("detail-body").textContent.includes("Completely remove agent")'))throw new Error('Agent management buttons missing');
  await writeFile(path.join(directory,'dashboard-details.png'),Buffer.from((await send('Page.captureScreenshot',{format:'png'})).data,'base64'));
  await evaluate('document.getElementById("close-detail").click()');
  await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});await sleep(500);
  await writeFile(path.join(directory,'dashboard-mobile.png'),Buffer.from((await send('Page.captureScreenshot',{format:'png'})).data,'base64'));
  await evaluate('document.getElementById("logout").click()');await sleep(1500);
  if(!await evaluate('!document.getElementById("login-screen").hidden'))throw new Error('Logout did not return to login.');
  if(errors.length)throw new Error('Browser runtime errors: '+errors.join('; '));
  console.log('PASS: registration wording, CSV download, real browser login, '+count+' device rows, hardware details, mobile layout and logout.');
  console.log('Screenshots: '+directory);
  await send('Browser.close').catch(()=>{});
}finally{socket?.close();browser.kill();}
