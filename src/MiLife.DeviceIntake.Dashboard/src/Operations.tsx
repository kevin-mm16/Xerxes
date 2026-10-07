import { useCallback, useEffect, useRef, useState } from 'react';

type Api = (path: string, options?: RequestInit) => Promise<any>;
type Target = { id: string; serialNumber: string; computerName?: string };
type Resolved = Target & { agentId?: string; silent: boolean; reason?: string; availability?: string };
type Job = { id: string; agentId: string; serialNumber: string; employeeName?: string; displayName: string; status: string; script: string; output?: string; exitCode?: number; runSilently: boolean; requestedAtUtc: string; requestedBy: string };
const timestamp = (text: string) => new Date(text.endsWith('Z') ? text : text + 'Z').toLocaleString();
const bytes = (count?: number) => count == null ? 'Unavailable' : `${(count / 1073741824).toFixed(1)} GB`;
const active = ['Queued', 'AwaitingApproval', 'Running'];

export function CommandActivity({ api, liveTick }: { api: Api; liveTick: number }) {
  const [jobs, setJobs] = useState<Job[]>([]); const [error, setError] = useState('');
  const [loading, setLoading] = useState(true); const [filter, setFilter] = useState('');
  const [busy, setBusy] = useState('');
  const load = useCallback(async () => {
    try { setJobs((await api('commands')).items); setError(''); }
    catch (e) { setError((e as Error).message); } finally { setLoading(false); }
  }, [api]);
  useEffect(() => { load(); }, [load, liveTick]);
  async function cancel(job: Job) {
    setBusy(job.id);
    try { await api(`agents/${job.agentId}/commands/${job.id}/cancel`, { method: 'POST', body: '{}' }); await load(); }
    catch (e) { setError((e as Error).message); } finally { setBusy(''); }
  }
  const visible = jobs.filter(job => filter === 'active' ? active.includes(job.status) : filter === 'attention' ? ['Expired', 'TimedOut', 'ResultUnavailable', 'Declined'].includes(job.status) || (job.exitCode != null && job.exitCode !== 0) : true);
  return <section className="panel operations-panel"><div className="panel-heading"><div><h2>Script activity</h2><p className="muted small">Latest 100 commands across all devices. Results update automatically.</p></div><button className="button secondary" onClick={load}>Refresh</button></div>
    <div className="activity-filters"><label>Show <select value={filter} onChange={e => setFilter(e.target.value)}><option value="">All commands</option><option value="active">In progress</option><option value="attention">Needs attention</option></select></label></div>
    {error && <p className="error" role="alert">{error}</p>}
    {loading ? <p className="muted">Loading command history…</p> : !visible.length ? <div className="empty"><h3>No commands to show</h3><p>Select devices in the inventory to run a script.</p></div> : <div className="command-history global-history">{visible.map(job => <article key={job.id}>
      <div><strong>{job.displayName} <span className="muted">· {job.serialNumber}</span></strong><span className={`command-status ${job.status.toLowerCase()}`}>{job.status === 'Completed' && job.exitCode != null && job.exitCode !== 0 ? `Failed (exit ${job.exitCode})` : job.status}</span></div>
      <small>{job.employeeName || 'Employee not recorded'} · {timestamp(job.requestedAtUtc)} · {job.requestedBy} · {job.runSilently ? 'Silent' : 'Employee approval'}</small>
      <details><summary>View script and output</summary><pre>{job.script}</pre><pre>{job.output ?? (active.includes(job.status) ? 'Waiting for a result…' : 'No output received.')}</pre>{job.exitCode != null && <p className="fine">Exit code: {job.exitCode}</p>}</details>
      {['Queued', 'AwaitingApproval'].includes(job.status) && <button className="button secondary compact" disabled={busy === job.id} onClick={() => cancel(job)}>Cancel command</button>}
    </article>)}</div>}
  </section>;
}

export function ServerHealth({ api, liveTick }: { api: Api; liveTick: number }) {
  const [health, setHealth] = useState<any>(); const [error, setError] = useState('');
  const load = useCallback(async () => {
    try { setHealth(await api('server-health')); setError(''); }
    catch (e) { setError((e as Error).message); }
  }, [api]);
  useEffect(() => { load(); }, [load, liveTick]);
  return <section className="panel operations-panel"><div className="panel-heading"><div><h2>Server health</h2><p className="muted small">Current readings from the intake server.</p></div><button className="button secondary" onClick={load}>Refresh</button></div>
    {error && <p className="error" role="alert">Unable to refresh. Previous readings may be stale: {error}</p>}
    {!health ? <p className="muted">{error ? 'No current readings available.' : 'Checking the server…'}</p> : <>
      <div className="health-grid">
        <HealthCard label="API & database" value={health.status} detail={`Database query: ${health.databaseLatencyMs} ms`} warning={!health.databaseHealthy} />
        <HealthCard label="API uptime" value={`${Math.floor(health.apiUptimeSeconds / 3600)}h ${Math.floor(health.apiUptimeSeconds % 3600 / 60)}m`} detail={`API memory: ${bytes(health.apiMemoryBytes)}`} />
        <HealthCard label="Root disk free" value={bytes(health.diskFreeBytes)} detail={`Total: ${bytes(health.diskTotalBytes)}`} warning={health.diskTotalBytes > 0 && health.diskFreeBytes / health.diskTotalBytes < .1} />
        <HealthCard label="Server RAM available" value={bytes(health.memoryAvailableBytes)} detail={`Total: ${bytes(health.memoryTotalBytes)}`} />
        <HealthCard label="1-minute system load" value={health.loadOneMinute == null ? 'Unavailable' : health.loadOneMinute.toFixed(2)} detail="System load, not CPU percentage" />
        <HealthCard label="Database backup" value={health.backupStatus} detail={health.lastBackupUtc ? `Completed ${timestamp(health.lastBackupUtc)}` : 'No successful backup marker is available.'} warning={['Stale', 'Missing', 'Invalid marker'].includes(health.backupStatus)} />
      </div><p className="fine muted">Checked {timestamp(health.checkedAtUtc)}. The public gateway is connected because this request completed. Disk readings cover the root volume.</p>
    </>}
  </section>;
}
function HealthCard({ label, value, detail, warning = false }: { label: string; value: string; detail: string; warning?: boolean }) {
  return <article className={`health-card ${warning ? 'warning' : ''}`}><span>{label}</span><strong>{value}</strong><p>{detail}</p></article>;
}

export function ScriptRunner({ devices, api, close, showActivity }: { devices: Target[]; api: Api; close: () => void; showActivity: () => void }) {
  const [targets, setTargets] = useState<Resolved[]>([]); const [catalog, setCatalog] = useState<any[]>([]);
  const [loading, setLoading] = useState(true); const [error, setError] = useState('');
  const [template, setTemplate] = useState(''); const [script, setScript] = useState(''); const [custom, setCustom] = useState(false);
  const [silent, setSilent] = useState(false); const [review, setReview] = useState(false); const [busy, setBusy] = useState(false);
  const [results, setResults] = useState<Record<string, string>>({}); const [submitted, setSubmitted] = useState(false);
  const submitting = useRef(false); const dialog = useRef<HTMLElement>(null);
  useEffect(() => {
    let stopped = false;
    async function prepare() {
      try {
        const commands = await api('commands/catalog');
        const resolved: Resolved[] = [];
        for (const device of devices) {
          try {
            const { items } = await api('agents?serialNumber=' + encodeURIComponent(device.serialNumber));
            const agents = items.filter((a: any) => a.isEnabled && !a.isRevoked && a.agentVersion !== '1.1.0');
            const agent = agents.length === 1 ? agents[0] : undefined;
            const version = String(agent?.agentVersion || '').split('.').map(Number);
            resolved.push({ ...device, agentId: agent?.id, availability: agent?.availability,
              silent: !!agent && (version[0] > 1 || (version[0] === 1 && version[1] >= 4)) && agent.consentVersion === '2026-09-23' && !!agent.consentAcceptedAtUtc,
              reason: !agents.length ? 'No enabled, supported agent' : agents.length > 1 ? 'Multiple agents: open device details to choose one' : undefined });
          } catch (e) { resolved.push({ ...device, silent: false, reason: (e as Error).message }); }
        }
        if (!stopped) { setTargets(resolved); setCatalog(commands.items); }
      } catch (e) { if (!stopped) setError((e as Error).message); }
      finally { if (!stopped) setLoading(false); }
    }
    prepare(); return () => { stopped = true; };
  }, [devices, api]);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null; dialog.current?.focus();
    function keyboard(event: KeyboardEvent) {
      if (event.key === 'Escape' && !submitting.current) close();
      if (event.key !== 'Tab') return;
      const elements = dialog.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href]');
      if (!elements?.length) return;
      const first = elements[0], last = elements[elements.length - 1];
      if (event.shiftKey && (document.activeElement === first || document.activeElement === dialog.current)) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    }
    document.addEventListener('keydown', keyboard);
    return () => { document.removeEventListener('keydown', keyboard); previous?.focus(); };
  }, [close]);
  const eligible = targets.filter(target => target.agentId);
  const canRunSilent = eligible.length > 0 && eligible.every(target => target.silent);
  const valid = eligible.length > 0 && (custom ? !!script.trim() : !!template) && (!silent || canRunSilent);
  async function run() {
    if (submitting.current || submitted || !valid) return;
    submitting.current = true; setBusy(true); setSubmitted(true);
    for (const target of eligible) {
      setResults(previous => ({ ...previous, [target.id]: 'Queuing…' }));
      try {
        const job = await api(`agents/${target.agentId}/commands`, { method: 'POST', body: JSON.stringify(custom ? { script, runSilently: silent } : { templateId: template, runSilently: silent }) });
        setResults(previous => ({ ...previous, [target.id]: `${job.status} · ${job.id.slice(0, 8)}` }));
      } catch (e) {
        setResults(previous => ({ ...previous, [target.id]: `Could not confirm queueing: ${(e as Error).message}. Check Script activity before trying again.` }));
      }
    }
    submitting.current = false; setBusy(false);
  }
  return <div className="drawer-backdrop"><aside ref={dialog} tabIndex={-1} className="drawer script-drawer" role="dialog" aria-modal="true" aria-labelledby="script-title">
    <header className="drawer-header"><div><span className="eyebrow">REMOTE SUPPORT</span><h2 id="script-title">Run a script</h2><p className="muted">{devices.length} selected device{devices.length === 1 ? '' : 's'}</p></div><button className="icon-button close" disabled={busy} onClick={close} aria-label="Close script runner">×</button></header>
    <div className="drawer-body"><section className="detail-section">
      {loading && <p>Checking target agents…</p>}{error && <p className="error" role="alert">{error}</p>}
      <h3>Targets</h3><ul className="target-list">{targets.map(target => <li key={target.id}><strong>{target.computerName || target.serialNumber}</strong><span>{target.serialNumber} · {target.reason || target.availability}</span><span role="status">{results[target.id]}</span></li>)}</ul>
      {!loading && !eligible.length && <p className="error">No eligible targets. Open a device to inspect its agent.</p>}
      {!loading && eligible.length > 0 && !submitted && <>
        {!review ? <><div className="tabs"><button className={!custom ? 'active' : ''} onClick={() => setCustom(false)}>Diagnostic template</button><button className={custom ? 'active' : ''} onClick={() => setCustom(true)}>Custom PowerShell</button></div>
          {custom ? <label className="field-label">PowerShell script<textarea aria-label="PowerShell script" className="script-editor" value={script} maxLength={4096} onChange={e => setScript(e.target.value)} placeholder="Get-Date" /><small>{script.length} / 4096 characters</small></label> : <label className="field-label">Diagnostic action<select value={template} onChange={e => setTemplate(e.target.value)}><option value="">Choose an action</option>{catalog.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select><p className="fine muted">{catalog.find(item => item.id === template)?.description}</p></label>}
          <label className="field-label">Execution mode<select value={silent ? 'silent' : 'prompt'} onChange={e => setSilent(e.target.value === 'silent')}><option value="prompt">Ask employee first</option><option value="silent" disabled={!canRunSilent}>Run silently with recorded consent</option></select></label>
          <p className="fine muted">Scripts run as the signed-in employee, with a 60-second limit. Offline agents must reconnect within five minutes. Devices without an eligible agent are skipped.</p>
          <button className="button primary" disabled={!valid} onClick={() => setReview(true)}>Review {eligible.length} target{eligible.length === 1 ? '' : 's'}</button>
        </> : <><h3>Review and run</h3><p>{custom ? 'Custom PowerShell' : catalog.find(item => item.id === template)?.name} on <strong>{eligible.length} device{eligible.length === 1 ? '' : 's'}</strong>.</p><p>Mode: <strong>{silent ? 'Silent execution' : 'Ask employee first'}</strong></p>{custom && <pre>{script}</pre>}
          <div className="actions"><button className="button secondary" onClick={() => setReview(false)}>Back</button><button className="button primary" disabled={!valid} onClick={run}>Queue script on {eligible.length} device{eligible.length === 1 ? '' : 's'}</button></div>
        </>}
      </>}
      {submitted && <><p role="status">{busy ? 'Queuing each device. Keep this panel open…' : 'Queueing finished. Open Script activity to follow results.'}</p><button className="button primary" disabled={busy} onClick={showActivity}>View script activity</button></>}
    </section></div>
  </aside></div>;
}
