import {execFileSync} from 'node:child_process';

const base=process.env.MILIFE_BASE_URL||'https://uphill-cofounder-trident.ngrok-free.dev';
const password=process.env.MILIFE_DASHBOARD_PASSWORD;
if(!password)throw new Error('Set MILIFE_DASHBOARD_PASSWORD.');
const cookies=new Map();let csrf='';
async function request(path,body){
  const response=await fetch(base+path,{method:body===undefined?'GET':'POST',headers:{'ngrok-skip-browser-warning':'true','Content-Type':'application/json','X-CSRF-TOKEN':csrf,Cookie:[...cookies].map(([key,value])=>key+'='+value).join('; ')},body:body===undefined?undefined:JSON.stringify(body)});
  for(const raw of response.headers.getSetCookie()){const first=raw.split(';')[0];const separator=first.indexOf('=');cookies.set(first.slice(0,separator),first.slice(separator+1));}
  const data=await response.json().catch(()=>({}));if(!response.ok)throw new Error('HTTP '+response.status+': '+(data.error||'Request failed'));return data;
}
function approvalWindowVisible(){
  const output=execFileSync('powershell.exe',['-NoProfile','-Command',"@((Get-Process | Where-Object MainWindowTitle -eq 'MiLife IT support approval')).Count"],{encoding:'utf8',windowsHide:true});
  return Number(output.trim())>0;
}
const sleep=milliseconds=>new Promise(resolve=>setTimeout(resolve,milliseconds));

csrf=(await request('/device-admin/api/session')).csrfToken;
await request('/device-admin/api/login',{username:'root',password});
csrf=(await request('/device-admin/api/session')).csrfToken;
const devices=await request('/device-admin/api/devices?search=IT%20Acceptance%20Test');
const device=devices.items.find(item=>item.employeeName==='IT Acceptance Test');
if(!device)throw new Error('Acceptance-test device was not found.');
const agents=await request('/device-admin/api/agents?serialNumber='+encodeURIComponent(device.serialNumber));
const agent=agents.items.find(item=>item.employeeName==='IT Acceptance Test'&&item.agentVersion==='1.4.0'&&item.isEnabled&&!item.isRevoked);
if(!agent)throw new Error('Agent 1.4 acceptance-test installation was not found.');
if(agent.consentVersion!=='2026-09-23'||!agent.consentAcceptedAtUtc)throw new Error('Current consent was not recorded.');

const commandRoute='/device-admin/api/agents/'+agent.id+'/commands';
const queued=await request(commandRoute,{script:"Write-Output 'MiLife silent support acceptance'; ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)",runSilently:true});
let completed;
for(let attempt=0;attempt<100;attempt++){
  if(approvalWindowVisible())throw new Error('A user-approval window appeared for a silent command.');
  const current=(await request(commandRoute)).items.find(item=>item.id===queued.id);
  if(current?.status==='Completed'){completed=current;break;}
  if(['Declined','Expired','TimedOut','ResultUnavailable','Cancelled'].includes(current?.status))throw new Error('Unexpected command status: '+current.status);
  await sleep(1000);
}
if(!completed)throw new Error('Silent command did not complete.');
if(completed.exitCode!==0||!completed.output.includes('MiLife silent support acceptance')||!completed.output.includes('False'))throw new Error('Silent command returned unexpected output.');
console.log('PASS: silent non-elevated PowerShell completed without an approval window.');

const actionRoute='/device-admin/api/agents/'+agent.id+'/actions';
const removal=await request(actionRoute,{action:'Remove'});
let removed=false;
for(let attempt=0;attempt<100;attempt++){
  const current=(await request(actionRoute)).items.find(item=>item.id===removal.id);
  if(current?.status==='Completed'){removed=true;break;}
  if(current?.status==='Failed')throw new Error(current.error||'Acceptance-test removal failed.');
  await sleep(2000);
}
if(!removed)throw new Error('Acceptance-test agent was not removed.');
console.log('PASS: acceptance-test agent completed server-directed removal.');
await request('/device-admin/api/logout',{});
