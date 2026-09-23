const base='https://uphill-cofounder-trident.ngrok-free.dev';
const cookies=new Map();let csrf='';
async function request(path,body){
 const response=await fetch(base+path,{method:body===undefined?'GET':'POST',headers:{Connection:'close','ngrok-skip-browser-warning':'true','Content-Type':'application/json','X-CSRF-TOKEN':csrf,Cookie:[...cookies].map(([k,v])=>k+'='+v).join('; ')},body:body===undefined?undefined:JSON.stringify(body)});
 for(const raw of response.headers.getSetCookie()){const first=raw.split(';')[0];const at=first.indexOf('=');cookies.set(first.slice(0,at),first.slice(at+1));}
 const data=await response.json();if(!response.ok)throw new Error('HTTP '+response.status+': '+(data.error||'Request failed'));return data;
}
csrf=(await request('/device-admin/api/session')).csrfToken;
await request('/device-admin/api/login',{username:'root',password:process.env.MILIFE_DASHBOARD_PASSWORD});
csrf=(await request('/device-admin/api/session')).csrfToken;
const devices=await request('/device-admin/api/devices?search=IT%20Acceptance%20Test');
if(devices.items.length!==1)throw new Error('Expected one acceptance-test device');
const agents=await request('/device-admin/api/agents?serialNumber='+encodeURIComponent(devices.items[0].serialNumber));
const agent=agents.items.find(a=>a.employeeName==='IT Acceptance Test'&&a.agentVersion==='1.3.0');
if(!agent)throw new Error('Test agent unavailable');
const route='/device-admin/api/agents/'+agent.id+'/actions';
for(const action of ['Disable','Remove']){
 const previous=(await request(route)).items.find(a=>a.action===action&&a.status!=='Failed');
 if(previous?.status==='Completed'){console.log('Already verified: '+action);continue;}
 const job=previous||await request(route,{action});
 console.log('Requested '+action+' for acceptance-test agent only.');
 if(action==='Remove')console.log('ACTION: Start the disabled acceptance-test agent to deliver queued removal.');
 let done=false;
 for(let i=0;i<90;i++){
  await new Promise(r=>setTimeout(r,2000));
  const current=(await request(route)).items.find(a=>a.id===job.id);
  if(current.status==='Completed'){console.log('PASS: '+action+' completed and audited.');done=true;break;}
  if(current.status==='Failed')throw new Error(current.error||'Action failed');
 }
 if(!done)throw new Error(action+' did not complete');
}
await request('/device-admin/api/logout',{});
