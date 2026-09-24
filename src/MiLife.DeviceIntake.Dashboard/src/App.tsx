import { FormEvent, ReactNode, useCallback, useEffect, useMemo, useRef, useState } from 'react';

type Json = Record<string, any>;
type Device = {
  id: string; serialNumber: string; computerName?: string; employeeName?: string; manufacturer?: string;
  model?: string; ramGB?: number; status: string; availability: string; receivedAtUtc: string;
  lastHeartbeatUtc?: string; submissionCount: number;
};
type Agent = {
  id: string; agentVersion: string; employeeName?: string; availability: string; lastSeenAtUtc?: string;
  enrolledAtUtc: string; isEnabled: boolean; isRevoked: boolean; consentVersion?: string;
  consentAcceptedAtUtc?: string; latitude?: number; longitude?: number; accuracyMeters?: number; locationCapturedAtUtc?: string;
};
type CatalogItem = { id: string; name: string; description: string; category: string; defaultRunSilently: boolean };
type CommandJob = { id: string; displayName: string; script: string; runSilently: boolean; requestedBy: string; status: string; requestedAtUtc: string; output?: string; exitCode?: number };

const statusLabel = (value?: string) => ({ PendingReview: 'Pending review', Approved: 'Approved', Rejected: 'Rejected', Matched: 'Matched', NotEnrolled: 'No heartbeat' }[value || ''] || value || 'Unknown');
const value = (item: unknown) => item === null || item === undefined || item === '' ? 'Not available' : String(item);
const date = (item?: string) => item ? new Date(item.endsWith('Z') ? item : item + 'Z').toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : 'Not available';
const supportsSilent = (agent?: Agent) => {
  const parts = String(agent?.agentVersion || '').split('.').map(Number);
  return !!agent && (parts[0] > 1 || (parts[0] === 1 && parts[1] >= 4)) && agent.consentVersion === '2026-09-23' && !!agent.consentAcceptedAtUtc;
};

function Badge({ children, tone }: { children: ReactNode; tone?: string }) {
  return <span className={`badge ${String(tone || children).toLowerCase().replaceAll(' ', '-')}`}>{children}</span>;
}

function App() {
  const csrf = useRef('');
  const [session, setSession] = useState<{ loading: boolean; authenticated: boolean; username?: string }>({ loading: true, authenticated: false });
  const [notice, setNotice] = useState('');
  const [liveTick, setLiveTick] = useState(0);
  const [connection, setConnection] = useState<'connecting' | 'live' | 'fallback'>('connecting');

  const api = useCallback(async (path: string, options: RequestInit = {}) => {
    const response = await fetch('/device-admin/api/' + path, {
      credentials: 'same-origin', ...options,
      headers: { ...(options.body ? { 'Content-Type': 'application/json' } : {}), 'X-CSRF-TOKEN': csrf.current, ...options.headers }
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) {
      if (response.status === 401 && path !== 'login') setSession({ loading: false, authenticated: false });
      throw new Error(data.error || data.detail || (response.status === 429 ? 'Too many requests. Try again shortly.' : 'The request could not be completed.'));
    }
    return data;
  }, []);

  const loadSession = useCallback(async () => {
    try {
      const data = await api('session'); csrf.current = data.csrfToken;
      setSession({ loading: false, authenticated: data.authenticated, username: data.username });
    } catch (error) { setSession({ loading: false, authenticated: false }); setNotice((error as Error).message); }
  }, [api]);

  useEffect(() => { loadSession(); }, [loadSession]);
  useEffect(() => {
    if (!session.authenticated) return;
    const events = new EventSource('/device-admin/api/events');
    events.onopen = () => setConnection('live');
    events.onerror = () => setConnection('fallback');
    events.addEventListener('refresh', () => setLiveTick(tick => tick + 1));
    const fallback = window.setInterval(() => setLiveTick(tick => tick + 1), 30000);
    return () => { events.close(); window.clearInterval(fallback); };
  }, [session.authenticated]);

  async function login(username: string, password: string) {
    await api('login', { method: 'POST', body: JSON.stringify({ username, password }) });
    await loadSession();
  }
  async function logout() {
    await api('logout', { method: 'POST', body: '{}' });
    setSession({ loading: false, authenticated: false });
  }

  if (session.loading) return <div className="boot"><img src="/device-admin/milife-logo.png" alt="MiLife Insurance" /><span className="spinner" />Loading workspace…</div>;
  if (!session.authenticated) return <Login onLogin={login} message={notice} />;
  return <Dashboard api={api} username={session.username || 'Administrator'} logout={logout} liveTick={liveTick} connection={connection} notify={setNotice} />;
}

function Login({ onLogin, message }: { onLogin: (username: string, password: string) => Promise<void>; message: string }) {
  const [error, setError] = useState(message); const [busy, setBusy] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError('');
    const form = new FormData(event.currentTarget);
    try { await onLogin(String(form.get('username')), String(form.get('password'))); }
    catch (exception) { setError((exception as Error).message); setBusy(false); }
  }
  return <main className="login-page">
    <section className="login-brand">
      <img src="/device-admin/milife-logo.png" alt="MiLife Insurance" />
      <div><span className="eyebrow teal">DEVICE OPERATIONS</span><h1>Every company PC.<br />One live workspace.</h1><p>Inventory, availability and controlled remote diagnostics for the IT team.</p></div>
      <small>Internal IT workspace</small>
    </section>
    <section className="login-card"><form onSubmit={submit}>
      <span className="eyebrow">REACT PREVIEW</span><h2>Sign in</h2><p className="muted">Use your device administrator account.</p>
      <label>Username<input name="username" defaultValue="root" autoComplete="username" maxLength={128} required /></label>
      <label>Password<input name="password" type="password" autoComplete="current-password" maxLength={1024} required autoFocus /></label>
      {error && <p className="error" role="alert">{error}</p>}
      <button className="button primary wide" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'} <span>→</span></button>
      <p className="fine muted">Staff can <a href="/device-registration" target="_blank">check in a company PC here</a>.</p>
    </form></section>
  </main>;
}

function Dashboard({ api, username, logout, liveTick, connection, notify }: { api: (path: string, options?: RequestInit) => Promise<any>; username: string; logout: () => Promise<void>; liveTick: number; connection: string; notify: (text: string) => void }) {
  const [devices, setDevices] = useState<Device[]>([]); const [summary, setSummary] = useState<Json>({});
  const [total, setTotal] = useState(0); const [page, setPage] = useState(1); const [query, setQuery] = useState('');
  const [draft, setDraft] = useState(''); const [status, setStatus] = useState(''); const [loading, setLoading] = useState(true);
  const [error, setError] = useState(''); const [selected, setSelected] = useState<Device>(); const [lastUpdated, setLastUpdated] = useState<Date>();
  const requestId = useRef(0);

  const load = useCallback(async () => {
    const id = ++requestId.current; setLoading(true); setError('');
    try {
      const params = new URLSearchParams({ page: String(page), search: query, status });
      const data = await api('devices?' + params);
      if (id !== requestId.current) return;
      setDevices(data.items); setSummary(data.summary); setTotal(data.total); setLastUpdated(new Date());
    } catch (exception) { if (id === requestId.current) setError((exception as Error).message); }
    finally { if (id === requestId.current) setLoading(false); }
  }, [api, page, query, status]);

  useEffect(() => { load(); }, [load, liveTick]);
  useEffect(() => { const visible = () => { if (!document.hidden) load(); }; document.addEventListener('visibilitychange', visible); return () => document.removeEventListener('visibilitychange', visible); }, [load]);

  async function exportCsv() {
    try {
      const params = new URLSearchParams({ search: query, status });
      const response = await fetch('/device-admin/api/devices/export?' + params, { credentials: 'same-origin' });
      if (!response.ok) throw new Error('Export failed. Please try again.');
      const url = URL.createObjectURL(await response.blob()); const link = document.createElement('a');
      link.href = url; link.download = `milife-devices-${new Date().toISOString().slice(0, 10)}.csv`; link.click();
      setTimeout(() => URL.revokeObjectURL(url), 30000); notify('Export downloaded');
    } catch (exception) { setError((exception as Error).message); }
  }

  return <div className="shell">
    <aside className="sidebar">
      <a href="/device-admin/react-preview" className="logo"><img src="/device-admin/milife-logo.png" alt="MiLife Insurance" /></a>
      <nav><span className="nav-label">WORKSPACE</span><a className="nav-active" href="/device-admin/react-preview"><span>▦</span> Devices & support</a><a href="/device-registration" target="_blank"><span>↓</span> PC check-in</a></nav>
      <div className="account"><span className="avatar">IT</span><div><strong>{username}</strong><small>Administrator</small></div><button className="icon-button" onClick={logout} title="Sign out">↪</button></div>
    </aside>
    <main className="content">
      <header className="topbar"><div><span className={`live-dot ${connection}`} />{connection === 'live' ? 'Live updates connected' : 'Automatic refresh active'}</div><span>{lastUpdated ? `Updated ${lastUpdated.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })}` : 'Updating…'}</span></header>
      <div className="page">
        <header className="page-title"><div><span className="eyebrow">DEVICE OPERATIONS</span><h1>Company devices</h1><p className="muted">Live inventory, availability and remote support.</p></div><a className="button primary" href="/device-registration" target="_blank">+ Check in a PC</a></header>
        <section className="metrics">
          <Metric label="Registered devices" count={summary.devices} hint="Unique hardware" icon="▣" />
          <Metric label="Online agents" count={summary.online} hint="Seen in 15 minutes" icon="●" tone="teal" />
          <Metric label="Pending review" count={summary.pending} hint="Awaiting a decision" icon="◷" tone="amber" />
          <Metric label="Total submissions" count={summary.submissions} hint="Registration history" icon="↻" />
        </section>
        <section className="panel">
          <div className="panel-heading"><div><h2>All devices <span className="count">{total}</span></h2><p className="muted small">Open a device to inspect inventory or start support.</p></div><div className="actions"><button className="button secondary" onClick={exportCsv}>Export CSV</button><button className="button secondary" onClick={load} disabled={loading}>{loading ? 'Refreshing…' : 'Refresh'}</button></div></div>
          <form className="filters" onSubmit={event => { event.preventDefault(); setPage(1); setQuery(draft.trim()); }}>
            <label className="search"><span>⌕</span><input value={draft} onChange={event => setDraft(event.target.value)} placeholder="Search computer, serial number or employee…" maxLength={256} aria-label="Search devices" /></label>
            <select value={status} onChange={event => { setPage(1); setStatus(event.target.value); }} aria-label="Review status"><option value="">All review statuses</option><option value="PendingReview">Pending review</option><option value="Approved">Approved</option><option value="Matched">Matched</option><option value="Rejected">Rejected</option></select>
            <button className="button secondary">Search</button>
          </form>
          {error && <p className="error" role="alert">{error}</p>}
          <div className={`table-wrap ${loading ? 'loading' : ''}`}><table><thead><tr><th>Device</th><th>Serial / employee</th><th>Memory</th><th>Availability</th><th>Review</th><th>Last inventory</th><th /></tr></thead><tbody>
            {devices.map(device => <tr key={device.id} onClick={() => setSelected(device)}>
              <td><div className="device"><span className="device-icon">▣</span><div><strong>{device.computerName || 'Unnamed PC'}</strong><small>{[device.manufacturer, device.model].filter(Boolean).join(' · ') || 'Model unavailable'}</small></div></div></td>
              <td><strong>{device.serialNumber}</strong><small>{device.employeeName || 'Name not recorded'}</small></td><td>{device.ramGB == null ? '—' : `${device.ramGB} GB`}</td>
              <td><Badge tone={device.availability}>{statusLabel(device.availability)}</Badge></td><td><Badge tone={device.status}>{statusLabel(device.status)}</Badge></td>
              <td>{date(device.receivedAtUtc)}<small>{device.submissionCount} submission{device.submissionCount === 1 ? '' : 's'}</small></td><td><button className="row-open" aria-label={`Open ${device.computerName || device.serialNumber}`}>→</button></td>
            </tr>)}
          </tbody></table>{!loading && devices.length === 0 && <div className="empty"><span>▦</span><h3>No devices found</h3><p>Try another search or check in a company PC.</p></div>}</div>
          <footer className="pagination"><span>{total ? `${(page - 1) * 20 + 1}–${Math.min(page * 20, total)} of ${total} devices` : '0 devices'}</span><div><button className="button secondary" disabled={page === 1} onClick={() => setPage(value => value - 1)}>Previous</button><button className="button secondary" disabled={page * 20 >= total} onClick={() => setPage(value => value + 1)}>Next</button></div></footer>
        </section>
        <p className="fine muted center">Availability reflects agent check-ins, not employee activity.</p>
      </div>
    </main>
    {selected && <DeviceDrawer device={selected} api={api} liveTick={liveTick} close={() => setSelected(undefined)} refreshDevices={load} />}
  </div>;
}

function Metric({ label, count, hint, icon, tone = '' }: { label: string; count: ReactNode; hint: string; icon: string; tone?: string }) {
  return <article className="metric"><span className={`metric-icon ${tone}`}>{icon}</span><div><span>{label}</span><strong>{count ?? '—'}</strong><small>{hint}</small></div></article>;
}

function DeviceDrawer({ device, api, liveTick, close, refreshDevices }: { device: Device; api: (path: string, options?: RequestInit) => Promise<any>; liveTick: number; close: () => void; refreshDevices: () => Promise<void> }) {
  const [detail, setDetail] = useState<Json>(); const [history, setHistory] = useState<Json[]>([]); const [agents, setAgents] = useState<Agent[]>([]);
  const [catalog, setCatalog] = useState<CatalogItem[]>([]); const [loading, setLoading] = useState(true); const [error, setError] = useState('');
  const load = useCallback(async () => {
    try {
      const [item, submissions, agentData, commands] = await Promise.all([
        api('submissions/' + device.id), api('submissions?serialNumber=' + encodeURIComponent(device.serialNumber)),
        api('agents?serialNumber=' + encodeURIComponent(device.serialNumber)), api('commands/catalog')
      ]);
      setDetail(item); setHistory(submissions.items); setAgents(agentData.items); setCatalog(commands.items); setError('');
    } catch (exception) { setError((exception as Error).message); } finally { setLoading(false); }
  }, [api, device.id, device.serialNumber]);
  useEffect(() => { load(); }, [load, liveTick]);
  useEffect(() => { const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') close(); }; document.addEventListener('keydown', escape); return () => document.removeEventListener('keydown', escape); }, [close]);

  async function review(status: string, note: string) {
    if (!detail) return;
    await api(`submissions/${detail.id}/review`, { method: 'POST', body: JSON.stringify({ status, expectedStatus: detail.status, note }) });
    await Promise.all([load(), refreshDevices()]);
  }
  const hw = detail?.hardware;
  const location = [...agents].filter(agent => agent.latitude != null).sort((a, b) => new Date(b.locationCapturedAtUtc || 0).getTime() - new Date(a.locationCapturedAtUtc || 0).getTime())[0];

  return <div className="drawer-backdrop" onMouseDown={event => { if (event.target === event.currentTarget) close(); }}><aside className="drawer" role="dialog" aria-modal="true" aria-label="Device details">
    <header className="drawer-header"><div><span className="eyebrow">DEVICE DETAILS & SUPPORT</span><h2>{device.computerName || device.serialNumber}</h2><p className="muted">{device.serialNumber}</p></div><button className="icon-button close" onClick={close} aria-label="Close">×</button></header>
    {loading && <div className="drawer-loading"><span className="spinner" />Loading device…</div>}{error && <p className="error pad">{error}</p>}
    {detail && <div className="drawer-body">
      <Support agents={agents} catalog={catalog} api={api} />
      <Section title="Computer"><SpecGrid values={[["Employee", device.employeeName], ["Computer name", hw?.computerName], ["Serial number", detail.serialNumber], ["Manufacturer", hw?.manufacturer], ["Model", hw?.model], ["Windows user", hw?.loggedInUser], ["Last inventory", date(detail.receivedAtUtc)]]} /></Section>
      <Section title="Hardware & Windows"><SpecGrid values={[["Processor", hw?.processor?.name], ["Cores / logical", hw?.processor ? `${hw.processor.physicalCores ?? '—'} / ${hw.processor.logicalProcessors ?? '—'}` : null], ["Installed RAM", hw?.ram?.totalGB == null ? null : `${hw.ram.totalGB} GB`], ["Windows edition", hw?.windows?.edition], ["Version / build", [hw?.windows?.version, hw?.windows?.buildNumber].filter(Boolean).join(' / ')], ["Architecture", hw?.windows?.architecture]]} /></Section>
      {!!hw?.disks?.length && <Section title="Physical storage"><DataTable headers={["Model", "Serial", "Capacity", "Type / bus"]} rows={hw.disks.map((disk: Json) => [disk.model, disk.serialNumber, disk.capacityGB ? `${disk.capacityGB} GB` : null, [disk.mediaType, disk.busType].filter(Boolean).join(' / ')])} /></Section>}
      {!!hw?.networkAdapters?.length && <Section title="Active network adapters"><DataTable headers={["Adapter", "MAC address"]} rows={hw.networkAdapters.map((adapter: Json) => [adapter.name, adapter.macAddress])} /></Section>}
      <Section title="Agent management">{agents.length ? agents.map(agent => <AgentCard key={agent.id} agent={agent} device={device} api={api} reload={async () => { await load(); await refreshDevices(); }} />) : <p className="muted small">No heartbeat agent is enrolled.</p>}</Section>
      <Section title="Last reported location">{location ? <><SpecGrid values={[["Latitude / longitude", `${location.latitude!.toFixed(5)}, ${location.longitude!.toFixed(5)}`], ["Accuracy", `${Math.round(location.accuracyMeters || 0)} metres`], ["Captured", date(location.locationCapturedAtUtc)], ["Source", "Windows location services"]]} /><p className="fine muted">This is the last reported position and may be approximate.</p></> : <p className="muted small">No Windows location has been reported.</p>}</Section>
      <Review status={detail.status} onReview={review} />
      <Section title="Registration history"><DataTable headers={["Received", "Review", "Collection"]} rows={history.map(item => [date(item.receivedAtUtc), statusLabel(item.status), item.isRepeat ? 'Repeat submission' : 'First registration'])} /></Section>
      {!!detail.reviews?.length && <Section title="Review history"><DataTable headers={["When", "Reviewed by", "Decision", "Note"]} rows={detail.reviews.map((item: Json) => [date(item.reviewedAtUtc), item.reviewer, statusLabel(item.status), item.note])} /></Section>}
    </div>}
  </aside></div>;
}

function Section({ title, children }: { title: string; children: ReactNode }) { return <section className="detail-section"><h3>{title}</h3>{children}</section>; }
function SpecGrid({ values }: { values: [string, unknown][] }) { return <div className="spec-grid">{values.map(([label, item]) => <dl key={label}><dt>{label}</dt><dd>{value(item)}</dd></dl>)}</div>; }
function DataTable({ headers, rows }: { headers: string[]; rows: unknown[][] }) { return <div className="mini-table"><table><thead><tr>{headers.map(header => <th key={header}>{header}</th>)}</tr></thead><tbody>{rows.map((row, index) => <tr key={index}>{row.map((item, cell) => <td key={cell}>{value(item)}</td>)}</tr>)}</tbody></table></div>; }

function AgentCard({ agent, device, api, reload }: { agent: Agent; device: Device; api: (path: string, options?: RequestInit) => Promise<any>; reload: () => Promise<void> }) {
  const [actions, setActions] = useState<Json[]>([]); const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  const loadActions = useCallback(async () => { try { setActions((await api(`agents/${agent.id}/actions`)).items); } catch (exception) { setError((exception as Error).message); } }, [agent.id, api]);
  useEffect(() => { loadActions(); }, [loadActions]);
  const pending = actions.some(action => ['Requested', 'Acknowledged'].includes(action.status));
  const removed = agent.isRevoked || actions.some(action => action.action === 'Remove' && ['Requested', 'Acknowledged', 'Completed'].includes(action.status));
  async function lifecycle(action: 'Disable' | 'Remove') {
    const prompt = action === 'Remove' ? `Completely uninstall the agent from ${device.computerName || device.serialNumber}? Inventory history will remain.` : `Disable the agent on ${device.computerName || device.serialNumber}?`;
    if (!window.confirm(prompt)) return; setBusy(true); setError('');
    try { await api(`agents/${agent.id}/actions`, { method: 'POST', body: JSON.stringify({ action }) }); await loadActions(); await reload(); }
    catch (exception) { setError((exception as Error).message); } finally { setBusy(false); }
  }
  async function enable() {
    if (!window.confirm('Allow this installation to check in again? The app must still be opened on the PC.')) return; setBusy(true);
    try { await api(`agents/${agent.id}/enable`, { method: 'POST', body: '{}' }); await loadActions(); await reload(); }
    catch (exception) { setError((exception as Error).message); } finally { setBusy(false); }
  }
  return <article className="agent-card"><div className="agent-title"><div><strong>{agent.employeeName || 'Unnamed employee'} · Agent {agent.agentVersion}</strong><small>{agent.availability} · Last heartbeat {date(agent.lastSeenAtUtc)}</small></div><Badge tone={agent.availability}>{agent.availability}</Badge></div>
    <div className="actions"><button className="button secondary" disabled={busy || !agent.isEnabled || pending || removed} onClick={() => lifecycle('Disable')}>Disable</button><button className="button secondary" disabled={busy || (agent.isEnabled && !agent.isRevoked) || removed} onClick={enable}>Allow re-enable</button><button className="button danger" disabled={busy || pending || removed} onClick={() => lifecycle('Remove')}>Completely remove</button></div>
    {error && <p className="error">{error}</p>}{actions.slice(0, 4).map(action => <p className="fine muted" key={action.id}>{action.action} · {action.status} · {date(action.completedAtUtc || action.acknowledgedAtUtc || action.requestedAtUtc)}</p>)}</article>;
}

function Support({ agents, catalog, api }: { agents: Agent[]; catalog: CatalogItem[]; api: (path: string, options?: RequestInit) => Promise<any> }) {
  const eligible = agents.filter(agent => agent.agentVersion !== '1.1.0'); const [agentId, setAgentId] = useState(eligible[0]?.id || '');
  const [tab, setTab] = useState<'quick' | 'advanced'>('quick'); const [selected, setSelected] = useState(''); const [mode, setMode] = useState<'silent' | 'prompt'>('silent');
  const [script, setScript] = useState(''); const [jobs, setJobs] = useState<CommandJob[]>([]); const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  const agent = eligible.find(item => item.id === agentId); const ready = !!agent?.isEnabled && !agent?.isRevoked; const silent = ready && supportsSilent(agent);
  const load = useCallback(async () => { if (!agentId) return; try { setJobs((await api(`agents/${agentId}/commands`)).items); } catch (exception) { setError((exception as Error).message); } }, [agentId, api]);
  useEffect(() => { load(); const timer = window.setInterval(load, 10000); return () => window.clearInterval(timer); }, [load]);
  useEffect(() => { if (!silent) setMode('prompt'); }, [silent]);
  async function queue() {
    setBusy(true); setError('');
    try {
      const payload = tab === 'quick' ? { templateId: selected, runSilently: mode === 'silent' } : { script, runSilently: mode === 'silent' };
      await api(`agents/${agentId}/commands`, { method: 'POST', body: JSON.stringify(payload) }); setScript(''); await load();
    } catch (exception) { setError((exception as Error).message); } finally { setBusy(false); }
  }
  async function cancel(id: string) { try { await api(`agents/${agentId}/commands/${id}/cancel`, { method: 'POST', body: '{}' }); await load(); } catch (exception) { setError((exception as Error).message); } }
  return <Section title="Remote support">
    {!eligible.length ? <p className="muted small">Install the current agent to enable support.</p> : <>
      <div className="support-bar"><label>Target agent<select value={agentId} onChange={event => { setAgentId(event.target.value); setSelected(''); }} disabled={eligible.length === 1}>{eligible.map(item => <option key={item.id} value={item.id}>{item.employeeName || 'Unnamed user'} · {item.availability} · {item.id.slice(0, 8)}</option>)}</select></label><span className={`readiness ${silent ? 'ready' : ready ? 'limited' : 'offline'}`}>{!ready ? 'Agent unavailable' : silent ? 'Silent support ready' : 'Approval mode only'}</span></div>
      <div className="tabs"><button className={tab === 'quick' ? 'active' : ''} onClick={() => setTab('quick')}>Quick actions</button><button className={tab === 'advanced' ? 'active' : ''} onClick={() => setTab('advanced')}>Advanced PowerShell</button></div>
      {tab === 'quick' ? <div className="command-grid">{catalog.map(item => <button className={selected === item.id ? 'selected' : ''} key={item.id} onClick={() => { setSelected(item.id); setMode(item.defaultRunSilently && silent ? 'silent' : 'prompt'); }}><strong>{item.name}</strong><span>{item.description}</span></button>)}</div> : <div className="advanced"><p className="fine muted">Runs with the signed-in employee's Windows permissions.</p><textarea value={script} onChange={event => setScript(event.target.value)} maxLength={4096} placeholder="Example: Get-Date" /></div>}
      <div className="command-run"><select value={mode} onChange={event => setMode(event.target.value as 'silent' | 'prompt')}><option value="silent" disabled={!silent}>Run silently</option><option value="prompt">Ask employee first</option></select><button className="button primary" disabled={busy || !ready || (tab === 'quick' ? !selected : !script.trim())} onClick={queue}>{busy ? 'Queuing…' : tab === 'quick' ? 'Run selected action' : 'Queue PowerShell'}</button></div>
      {error && <p className="error">{error}</p>}
      <div className="activity-title"><h4>Command activity</h4><span className="fine muted">Updates automatically</span></div>
      <div className="command-history">{!jobs.length && <p className="muted small">No commands have been sent to this agent.</p>}{jobs.map(job => <article key={job.id}><div><strong>{job.displayName || 'PowerShell command'}</strong><span className={`command-status ${job.status.toLowerCase()}`}>{job.status}</span></div><small>{job.runSilently ? 'Silent' : 'Approval'} · {date(job.requestedAtUtc)} · {job.requestedBy}</small><details><summary>View command and result</summary><pre>{job.script}</pre>{job.output != null && <><h5>Result</h5><pre>{job.output || '(No output)'}</pre></>}{job.exitCode != null && <p className="fine muted">Exit code: {job.exitCode}</p>}</details>{['Queued', 'AwaitingApproval'].includes(job.status) && <button className="button secondary compact" onClick={() => cancel(job.id)}>Cancel</button>}</article>)}</div>
    </>}
  </Section>;
}

function Review({ status, onReview }: { status: string; onReview: (status: string, note: string) => Promise<void> }) {
  const [note, setNote] = useState(''); const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  async function save(next: string) { setBusy(true); setError(''); try { await onReview(next, note); setNote(''); } catch (exception) { setError((exception as Error).message); } finally { setBusy(false); } }
  return <Section title="Review submission"><div className="review-head"><Badge tone={status}>{statusLabel(status)}</Badge></div><textarea value={note} onChange={event => setNote(event.target.value)} maxLength={2000} placeholder="Optional review note" /><div className="actions"><button className="button primary" disabled={busy || status === 'Approved'} onClick={() => save('Approved')}>Approve</button><button className="button secondary" disabled={busy || status === 'Matched'} onClick={() => save('Matched')}>Mark matched</button><button className="button secondary" disabled={busy || status === 'Rejected'} onClick={() => save('Rejected')}>Reject</button></div>{error && <p className="error">{error}</p>}</Section>;
}

export default App;
