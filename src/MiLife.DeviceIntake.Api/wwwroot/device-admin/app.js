'use strict';
const $ = id => document.getElementById(id);
const state = { csrf: '', page: 1, total: 0, selected: null };
const statusText = value => ({PendingReview:'Pending review',Approved:'Approved',Rejected:'Rejected',Matched:'Matched'}[value] || value || 'Unknown');
const text = value => value === null || value === undefined || value === '' ? 'Not available' : String(value);
const date = value => value ? new Date(value.endsWith('Z') ? value : value + 'Z').toLocaleString([], {dateStyle:'medium',timeStyle:'short'}) : 'Not available';
const node = (tag, content, className) => { const e = document.createElement(tag); if (content !== undefined) e.textContent = content; if (className) e.className = className; return e; };
function badge(value, label) { return node('span', label || statusText(value), 'badge ' + String(value).toLowerCase()); }
async function api(path, options = {}) {
  const response = await fetch('/device-admin/api/' + path, {credentials:'same-origin', ...options, headers:{'Content-Type':'application/json','X-CSRF-TOKEN':state.csrf,...options.headers}});
  const data = await response.json().catch(() => ({}));
  if (!response.ok) { if (response.status === 401 && path !== 'login') showLogin(); throw new Error(data.error || data.detail || (response.status === 429 ? 'Too many attempts. Please wait five minutes.' : 'The request could not be completed.')); }
  return data;
}
function showLogin() { $('workspace').hidden = true; $('login-screen').hidden = false; $('device-dialog').close(); }
function showWorkspace(username) { $('workspace').hidden = false; $('login-screen').hidden = true; $('account-name').textContent = username; }
async function session() { const data = await api('session'); state.csrf = data.csrfToken; if (data.authenticated) { showWorkspace(data.username); await loadDevices(); } else showLogin(); }
function toast(message) { $('toast').textContent = message; $('toast').hidden = false; setTimeout(() => $('toast').hidden = true, 4000); }
async function loadDevices() {
  $('page-error').textContent = ''; $('refresh').disabled = true;
  try {
    const params = new URLSearchParams({page:state.page,search:$('search').value.trim(),status:$('status-filter').value});
    const data = await api('devices?' + params); state.total = data.total;
    $('stat-devices').textContent = data.summary.devices; $('stat-online').textContent = data.summary.online ?? 0;
    $('stat-pending').textContent = data.summary.pending; $('stat-submissions').textContent = data.summary.submissions;
    $('device-count').textContent = data.total;
    const rows = $('device-rows'); rows.replaceChildren();
    for (const item of data.items) {
      const row = node('tr');
      const nameCell = node('td'); const name = node('div', undefined, 'device-name'); name.append(node('span','▣','device-icon'));
      const names = node('div'); names.append(node('strong',item.computerName || 'Unnamed PC'),node('small',[item.manufacturer,item.model].filter(Boolean).join(' · ') || 'Model unavailable')); name.append(names); nameCell.append(name);
      const serial = node('td'); serial.append(node('strong',text(item.serialNumber)),node('small',item.employeeName || "Name not recorded"));
      const availability = node('td'); availability.append(badge(item.availability || 'NotEnrolled', ({Online:'Online',Offline:'Offline',NotEnrolled:'No heartbeat',Disabled:'Disabled'}[item.availability] || 'No heartbeat')));
      const status = node('td'); status.append(badge(item.status)); const received = node('td'); received.append(node('span',date(item.receivedAtUtc)),node('small',item.submissionCount + (item.submissionCount === 1 ? ' submission' : ' submissions')));
      const action = node('td'); const button = node('button','→','row-button'); button.setAttribute('aria-label','View ' + (item.computerName || item.serialNumber)); button.addEventListener('click',()=>openDevice(item)); action.append(button);
      row.append(nameCell,serial,node('td',item.ramGB == null ? '—' : item.ramGB + ' GB'),availability,status,received,action); rows.append(row);
    }
    $('empty-state').hidden = data.items.length > 0; $('previous').disabled = state.page === 1; $('next').disabled = state.page * 20 >= data.total;
    $('page-summary').textContent = data.total ? `${(state.page-1)*20+1}–${Math.min(state.page*20,data.total)} of ${data.total} devices` : '0 devices';
  } catch (error) { $('page-error').textContent = error.message; } finally { $('refresh').disabled = false; }
}
function section(title) { const element = node('section',undefined,'detail-section'); element.append(node('h3',title)); return element; }
function specs(pairs) { const grid = node('div',undefined,'spec-grid'); for (const [label,value] of pairs) { const dl = node('dl',undefined,'spec'); dl.append(node('dt',label),node('dd',text(value))); grid.append(dl); } return grid; }
function dataTable(headers, values) { const table = node('table'); const head = node('tr'); headers.forEach(h=>head.append(node('th',h))); const thead = node('thead'); thead.append(head); table.append(thead); const body = node('tbody'); for (const row of values) { const tr = node('tr'); row.forEach(v=>tr.append(node('td',text(v)))); body.append(tr); } table.append(body); const wrapper = node('div',undefined,'table-scroll'); wrapper.append(table); return wrapper; }
async function openDevice(item) {
  state.selected = item; $('detail-title').textContent = item.computerName || item.serialNumber; $('detail-subtitle').textContent = item.serialNumber;
  $('detail-body').replaceChildren(node('p','Loading device information…','detail-loading')); if (!$('device-dialog').open) $('device-dialog').showModal();
  try {
    const [data, history, agents] = await Promise.all([api('submissions/' + item.id),api('submissions?serialNumber=' + encodeURIComponent(item.serialNumber)),api('agents?serialNumber=' + encodeURIComponent(item.serialNumber))]);
    const hw = data.hardware; const body = $('detail-body'); body.replaceChildren();
    const identity = section('Computer'); identity.append(specs([['Employee',item.employeeName],['Computer name',hw.computerName],['Serial number',data.serialNumber],['Manufacturer',hw.manufacturer],['Model',hw.model],['Windows user',hw.loggedInUser],['Last inventory received',date(data.receivedAtUtc)]])); body.append(identity);
    const system = section('Hardware & Windows'); system.append(specs([['Processor',hw.processor?.name],['Cores / logical processors',`${hw.processor?.physicalCores ?? '—'} / ${hw.processor?.logicalProcessors ?? '—'}`],['Installed RAM',hw.ram?.totalGB == null ? null : hw.ram.totalGB + ' GB'],['Windows edition',hw.windows?.edition],['Version / build',[hw.windows?.version,hw.windows?.buildNumber].filter(Boolean).join(' / ')],['Architecture',hw.windows?.architecture]])); body.append(system);
    if (hw.ram?.modules?.length) { const memory = section('Memory modules'); memory.append(dataTable(['Capacity (GB)','Manufacturer','Part number','Speed (MHz)'],hw.ram.modules.map(m=>[m.capacityGB,m.manufacturer,m.partNumber,m.speedMHz]))); body.append(memory); }
    const disks = section('Physical storage'); disks.append(hw.disks?.length ? dataTable(['Model','Serial','Capacity (GB)','Type / bus'],hw.disks.map(d=>[d.model,d.serialNumber,d.capacityGB,[d.mediaType,d.busType].filter(Boolean).join(' / ')])) : node('p','Storage information unavailable.','small muted')); body.append(disks);
    if (hw.networkAdapters?.length) { const network = section('Active network adapters at collection'); network.append(dataTable(['Adapter','MAC address'],hw.networkAdapters.map(n=>[n.name,n.macAddress]))); body.append(network); }
    const heartbeat = section('Heartbeat');
    if (agents.items.length) { for (const agent of agents.items) renderAgentControls(heartbeat,agent,item); }
    else heartbeat.append(node('p','No heartbeat agent is enrolled. This PC may be online; a one-time registration cannot tell us.','small muted'));
    body.append(heartbeat); renderManagement(body, agents.items);
    const review = section('Review this submission'); review.append(badge(data.status)); const form = node('form',undefined,'review-form'); const note = node('textarea'); note.placeholder = 'Optional note for the review history'; note.maxLength = 2000; note.setAttribute('aria-label','Review note'); form.append(note); const actions = node('div',undefined,'review-actions'); const error = node('p',undefined,'error'); error.setAttribute('role','alert');
    for (const value of ['Approved','Matched','Rejected']) { const button = node('button',({Approved:'Approve',Matched:'Mark as matched',Rejected:'Reject'}[value]),value==='Approved'?'primary':'secondary'); button.type='button'; button.disabled=data.status===value; button.addEventListener('click',async()=>{ error.textContent=''; actions.querySelectorAll('button').forEach(b=>b.disabled=true); try { await api('submissions/'+data.id+'/review',{method:'POST',body:JSON.stringify({status:value,expectedStatus:data.status,note:note.value})}); toast('Review saved'); await openDevice(item); await loadDevices(); } catch(e) { error.textContent=e.message; actions.querySelectorAll('button').forEach(b=>b.disabled=false); } }); actions.append(button); }
    form.append(actions,error); form.addEventListener('submit',e=>e.preventDefault()); review.append(form); body.append(review);
    const registrations = section('Registration history'); registrations.append(dataTable(['Received','Review status','Collection'],history.items.map(s=>[date(s.receivedAtUtc),statusText(s.status),s.isRepeat?'Repeat submission':'First registration']))); body.append(registrations);
    if (data.reviews.length) { const audit = section('Review history'); audit.append(dataTable(['When','Reviewed by','Decision','Note'],data.reviews.map(r=>[date(r.reviewedAtUtc),r.reviewer,statusText(r.status),r.note]))); body.append(audit); }
  } catch(error) { $('detail-body').replaceChildren(node('p',error.message,'error')); }
}
$('login-form').addEventListener('submit',async event=>{ event.preventDefault(); const form=event.currentTarget; const button=form.querySelector('button'); button.disabled=true; $('login-error').textContent=''; try { const values=new FormData(form); await api('login',{method:'POST',body:JSON.stringify({username:values.get('username'),password:values.get('password')})}); form.elements.password.value=''; await session(); } catch(e) { $('login-error').textContent=e.message; } finally { button.disabled=false; } });
$('logout').addEventListener('click',async()=>{ try { await api('logout',{method:'POST',body:'{}'}); await session(); } catch(e) { toast(e.message); } });
$('filters').addEventListener('submit',e=>{e.preventDefault();state.page=1;loadDevices();}); $('status-filter').addEventListener('change',()=>{state.page=1;loadDevices();});
$('refresh').addEventListener('click',loadDevices); $('previous').addEventListener('click',()=>{state.page--;loadDevices();}); $('next').addEventListener('click',()=>{state.page++;loadDevices();});
$('close-detail').addEventListener('click',()=>$('device-dialog').close());
setInterval(()=>{if (!$('workspace').hidden && !$('device-dialog').open && !document.hidden) loadDevices();},60000);
session().catch(error=>$('login-error').textContent=error.message);

function renderManagement(body, agents) {
  const location = section('Last reported location');
  const positions = agents.filter(a=>a.latitude!=null && a.longitude!=null).sort((a,b)=>new Date(b.locationCapturedAtUtc)-new Date(a.locationCapturedAtUtc));
  if (positions.length) {
    const point=positions[0];
    location.append(specs([['Latitude / longitude',point.latitude.toFixed(5)+', '+point.longitude.toFixed(5)],['Accuracy',Math.round(point.accuracyMeters)+' metres'],['Captured',date(point.locationCapturedAtUtc)],['Source','Windows location services']]));
    location.append(node('p','Last reported position; it may be approximate or outdated.','small muted'));
  } else location.append(node('p','Not available. Windows location services must be enabled and permit desktop apps. No location has been inferred from the IP address.','small muted'));
  body.append(location);
  const support=section('PowerShell support');
  support.append(node('p','Each command requires approval on the PC, runs with the signed-in user’s permissions, and is recorded here. Requests expire after five minutes; execution is limited to 60 seconds.','small muted'));
  const eligible=agents.filter(a=>a.agentVersion!=='1.1.0').sort((a,b)=>Number(a.isRevoked)-Number(b.isRevoked)||Number(b.isEnabled)-Number(a.isEnabled));
  if(!eligible.length) {support.append(node('p','Install the current registration app to enable support.','small muted'));body.append(support);return;}
  const select=node('select');select.setAttribute('aria-label','Support agent');
  for(const a of eligible){const option=node('option',(a.employeeName||'Unnamed user')+' · '+a.availability+' · '+a.id.slice(0,8));option.value=a.id;select.append(option);}
  const form=node('form',undefined,'review-form');const script=node('textarea');script.placeholder='Example: Get-Date';script.maxLength=4096;script.required=true;script.setAttribute('aria-label','PowerShell command');
  const send=node('button','Request approval on PC','primary');send.type='submit'; const updateSend=()=>{const a=eligible.find(a=>a.id===select.value);send.disabled=!a?.isEnabled||a?.isRevoked;};updateSend();
  const refresh=node('button','Refresh command history','secondary');refresh.type='button';
  const error=node('p',undefined,'error');error.setAttribute('role','alert');const history=node('div');
  async function load(){try{const data=await api('agents/'+select.value+'/commands');history.replaceChildren();for(const job of data.items){const card=node('div',undefined,'command-record');card.append(node('p',job.status+' · '+job.requestedBy+' · '+date(job.requestedAtUtc),'small'));card.append(node('pre',job.script));if(job.output!=null)card.append(node('pre',job.output||'(No output)'));if(job.exitCode!=null)card.append(node('small','Exit code: '+job.exitCode));history.append(card);}}catch(e){error.textContent=e.message;}}
  form.append(select,script,send,refresh,error);form.addEventListener('submit',async event=>{event.preventDefault();send.disabled=true;error.textContent='';try{await api('agents/'+select.value+'/commands',{method:'POST',body:JSON.stringify({script:script.value})});toast('Approval requested on the PC');script.value='';await load();}catch(e){error.textContent=e.message;}finally{updateSend();}});
  refresh.addEventListener('click',load);select.addEventListener('change',()=>{updateSend();load();});support.append(form,history);body.append(support);load();
}

$('export').addEventListener('click',async()=>{
  const button=$('export');button.disabled=true;
  try {
    const params=new URLSearchParams({search:$('search').value.trim(),status:$('status-filter').value});
    const response=await fetch('/device-admin/api/devices/export?'+params,{credentials:'same-origin',headers:{'ngrok-skip-browser-warning':'true'}});
    if(response.status===401){showLogin();throw new Error('Please sign in to export devices.');}
    if(!response.ok||!response.headers.get('content-type')?.includes('text/csv'))throw new Error('Export failed. Please try again.');
    const url=URL.createObjectURL(await response.blob());const link=document.createElement('a');
    link.href=url;link.download='milife-devices-'+new Date().toISOString().slice(0,10)+'.csv';document.body.append(link);link.click();link.remove();
    setTimeout(()=>URL.revokeObjectURL(url),30000);toast('Export downloaded');
  }catch(error){toast(error.message);}finally{button.disabled=false;}
});
function renderAgentControls(container, agent, device) {
  const card=node('div',undefined,'agent-card');
  card.append(node('h4',(agent.employeeName||'Unnamed employee')+' · Agent '+agent.agentVersion));
  card.append(node('p',agent.availability+' · Last heartbeat '+date(agent.lastSeenAtUtc),'small muted'));
  const controls=node('div',undefined,'review-actions');
  const disable=node('button','Disable agent','secondary');
  const enable=node('button','Allow re-enable','secondary');
  const remove=node('button','Completely remove agent','secondary danger');
  const refresh=node('button','Refresh status','secondary');
  const history=node('div');const message=node('p',undefined,'small muted');
  const compatible=Number(agent.agentVersion.split('.')[0])>1 || (Number(agent.agentVersion.split('.')[0])===1&&Number(agent.agentVersion.split('.')[1])>=3);
  let pending=false, removed=agent.isRevoked, removal=false;
  function updateButtons(){enable.disabled=removal||(agent.isEnabled&&!agent.isRevoked);disable.disabled=!compatible||!agent.isEnabled||pending||removed;remove.disabled=!compatible||pending||removed;}
  async function load(){
    try {
      const data=await api('agents/'+agent.id+'/actions');
      pending=data.items.some(a=>['Requested','Acknowledged'].includes(a.status));
      removal=data.items.some(a=>a.action==='Remove'&&['Requested','Acknowledged','Completed'].includes(a.status));
      removed=agent.isRevoked||data.items.some(a=>a.action==='Remove'&&a.status==='Completed');
      history.replaceChildren();
      for(const a of data.items){const label=a.status==='Completed'?(a.action==='Remove'?'Removal confirmed':a.action==='Enable'?'Re-enable allowed':'Disabled on PC'):a.status==='Requested'?'Pending — waiting for PC':a.status==='Acknowledged'?'Acknowledged — confirmation pending':a.status;
        history.append(node('p',a.action+' · '+label+' · '+a.requestedBy+' · '+date(a.completedAtUtc||a.acknowledgedAtUtc||a.requestedAtUtc),'small'));if(a.error)history.append(node('p',a.error,'error'));}
      message.textContent=!compatible?'Allow re-enable works with this version. Update to agent 1.3 or later for disable/removal actions.':pending?'Offline or stopped agents cannot act until they reconnect.':removed?'Agent access is revoked. Inventory and audit history are retained.':'Disable keeps local files. Completely remove deletes the installed agent, settings, logs and startup shortcut.';
      updateButtons();
    }catch(error){message.textContent=error.message;}
  }
  async function request(action){
    const explanation=action==='Remove'?'Completely uninstall this agent from '+(device.computerName||device.serialNumber)+'? Its installed files and startup shortcut will be deleted. Inventory and history stay on the server. Offline or disabled agents must reconnect before removal can happen.':'Disable this agent on '+(device.computerName||device.serialNumber)+'? Reporting and automatic startup will stop. Files stay on the PC. Restart it locally before sending a later removal request.';
    if(!confirm(explanation))return;
    disable.disabled=true;remove.disabled=true;
    try{await api('agents/'+agent.id+'/actions',{method:'POST',body:JSON.stringify({action})});agent.isEnabled=false;toast(action==='Remove'?'Removal requested':'Disable requested');await load();await loadDevices();}
    catch(error){toast(error.message);updateButtons();}
  }
  enable.addEventListener('click',async()=>{
    if(!confirm('Allow this installation to check in again? A disabled app must still be opened locally and Check in clicked. This does not reinstall a removed agent.'))return;
    enable.disabled=true;
    try{await api('agents/'+agent.id+'/enable',{method:'POST',body:'{}'});agent.isEnabled=true;agent.isRevoked=false;toast('Access restored. Open the app on the PC and click Check in.');await load();await loadDevices();}
    catch(error){toast(error.message);updateButtons();}
  });
  disable.addEventListener('click',()=>request('Disable'));remove.addEventListener('click',()=>request('Remove'));refresh.addEventListener('click',load);
  controls.append(enable,disable,remove,refresh);card.append(controls,message,history);container.append(card);updateButtons();load();
}
