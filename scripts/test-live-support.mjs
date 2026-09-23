const base='https://uphill-cofounder-trident.ngrok-free.dev';
const cookies=new Map();let csrf='';
async function request(path,body){
 const response=await fetch(base+path,{method:body===undefined?'GET':'POST',headers:{'ngrok-skip-browser-warning':'true','Content-Type':'application/json','X-CSRF-TOKEN':csrf,Cookie:[...cookies].map(([k,v])=>k+'='+v).join('; ')},body:body===undefined?undefined:JSON.stringify(body)});
 for(const raw of response.headers.getSetCookie()){const first=raw.split(';')[0];const at=first.indexOf('=');cookies.set(first.slice(0,at),first.slice(at+1));}
 const data=await response.json();if(!response.ok)throw new Error('HTTP '+response.status+': '+(data.error||'Request failed'));return data;
}
const session=await request('/device-admin/api/session');csrf=session.csrfToken;
await request('/device-admin/api/login',{username:'root',password:process.env.MILIFE_DASHBOARD_PASSWORD});
csrf=(await request('/device-admin/api/session')).csrfToken;
const devices=await request('/device-admin/api/devices?search=IT%20Acceptance%20Test');
if(devices.items.length!==1)throw new Error('Expected the dedicated acceptance-test device');
const agents=await request('/device-admin/api/agents?serialNumber='+encodeURIComponent(devices.items[0].serialNumber));
const agent=agents.items.find(a=>a.employeeName==='IT Acceptance Test'&&a.isEnabled&&a.agentVersion==='1.2.0');
if(!agent)throw new Error('Test agent unavailable');
const route='/device-admin/api/agents/'+agent.id+'/commands';
for(const expected of ['Completed','Declined']){
 const job=await request(route,{script:expected==='Completed'?"Write-Output 'MiLife support acceptance'; ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)":"Write-Output 'This declined command must not execute'"});
 console.log('ACTION: '+(expected==='Completed'?'Approve command':'Decline')+' on the test PC.');
 let passed=false;
 for(let i=0;i<100;i++){
  await new Promise(r=>setTimeout(r,2000));
  const current=(await request(route)).items.find(j=>j.id===job.id);
  if(current.status===expected){if(expected==='Completed'&&(!current.output.includes('MiLife support acceptance')||!current.output.includes('False')||current.exitCode!==0))throw new Error('Unexpected execution output');passed=true;console.log('PASS: '+expected+' recorded with requester '+current.requestedBy);break;}
  if(['Expired','TimedOut','ResultUnavailable'].includes(current.status))throw new Error('Unexpected status: '+current.status);
 }
 if(!passed)throw new Error('Support test timed out');
}
await request('/device-admin/api/logout',{});
