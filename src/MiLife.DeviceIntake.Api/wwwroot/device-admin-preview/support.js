'use strict';

// Preview-only replacement for the production device-management panel.
window.renderManagement = function renderManagementPreview(body, agents) {
  const location = section('Last reported location');
  const positions = agents.filter(agent => agent.latitude != null && agent.longitude != null)
    .sort((a, b) => new Date(b.locationCapturedAtUtc) - new Date(a.locationCapturedAtUtc));
  if (positions.length) {
    const point = positions[0];
    location.append(specs([
      ['Latitude / longitude', point.latitude.toFixed(5) + ', ' + point.longitude.toFixed(5)],
      ['Accuracy', Math.round(point.accuracyMeters) + ' metres'],
      ['Captured', date(point.locationCapturedAtUtc)],
      ['Source', 'Windows location services']
    ]));
    location.append(node('p', 'Last reported position; it may be approximate or outdated.', 'small muted'));
  } else {
    location.append(node('p', 'No Windows location has been reported for this PC.', 'small muted'));
  }
  body.append(location);

  const support = section('Remote support');
  support.classList.add('support-workspace');
  const eligible = agents.filter(agent => agent.agentVersion !== '1.1.0')
    .sort((a, b) => Number(a.isRevoked) - Number(b.isRevoked) || Number(b.isEnabled) - Number(a.isEnabled));
  if (!eligible.length) {
    support.append(node('p', 'Install the current registration application to enable remote support.', 'small muted'));
    body.append(support);
    return;
  }

  const toolbar = node('div', undefined, 'support-toolbar');
  const agentSelect = node('select'); agentSelect.setAttribute('aria-label', 'Support agent');
  for (const agent of eligible) {
    const option = node('option', (agent.employeeName || 'Unnamed employee') + ' | ' + agent.availability + ' | ' + agent.agentVersion);
    option.value = agent.id; agentSelect.append(option);
  }
  const readiness = node('span', undefined, 'support-readiness');
  toolbar.append(agentSelect, readiness); support.append(toolbar);

  const tabs = node('div', undefined, 'support-tabs');
  const quickButton = node('button', 'Quick actions', 'support-tab active'); quickButton.type = 'button';
  const advancedButton = node('button', 'Advanced PowerShell', 'support-tab'); advancedButton.type = 'button';
  tabs.append(quickButton, advancedButton); support.append(tabs);

  const quickPanel = node('div', undefined, 'support-panel');
  const commandGrid = node('div', undefined, 'command-grid');
  const modeRow = node('div', undefined, 'mode-row');
  const modeLabel = node('label', 'Execution mode');
  const mode = node('select'); mode.setAttribute('aria-label', 'Execution mode');
  mode.append(new Option('Run silently', 'silent'), new Option('Ask employee first', 'prompt'));
  const run = node('button', 'Run selected action', 'primary'); run.type = 'button'; run.disabled = true;
  modeRow.append(modeLabel, mode, run); quickPanel.append(commandGrid, modeRow);

  const advancedPanel = node('div', undefined, 'support-panel'); advancedPanel.hidden = true;
  const warning = node('p', 'Custom PowerShell runs with the signed-in employee\'s Windows permissions. Review the command before queuing it.', 'small muted');
  const script = node('textarea'); script.maxLength = 4096; script.placeholder = 'Example: Get-Date'; script.setAttribute('aria-label', 'PowerShell command');
  const advancedMode = node('select'); advancedMode.setAttribute('aria-label', 'Advanced execution mode');
  advancedMode.append(new Option('Run silently', 'silent'), new Option('Ask employee first', 'prompt'));
  const runAdvanced = node('button', 'Queue PowerShell', 'primary'); runAdvanced.type = 'button';
  const advancedActions = node('div', undefined, 'mode-row'); advancedActions.append(advancedMode, runAdvanced);
  advancedPanel.append(warning, script, advancedActions);
  support.append(quickPanel, advancedPanel);

  const historyHeader = node('div', undefined, 'history-header');
  historyHeader.append(node('h4', 'Command activity'), node('span', 'Newest first', 'small muted'));
  const error = node('p', undefined, 'error'); error.setAttribute('role', 'alert');
  const history = node('div', undefined, 'command-history');
  support.append(historyHeader, error, history); body.append(support);

  let catalog = [];
  let selectedTemplate = null;
  const selectedAgent = () => eligible.find(agent => agent.id === agentSelect.value);
  const supportsSilent = agent => {
    const version = String(agent.agentVersion || '').split('.').map(Number);
    return (version[0] > 1 || (version[0] === 1 && version[1] >= 4)) && agent.consentVersion === '2026-09-23' && !!agent.consentAcceptedAtUtc;
  };
  function updateReadiness() {
    const agent = selectedAgent();
    const ready = agent && agent.isEnabled && !agent.isRevoked;
    const silent = ready && supportsSilent(agent);
    readiness.textContent = !ready ? 'Agent unavailable' : silent ? 'Silent support ready' : 'Approval mode only';
    readiness.className = 'support-readiness ' + (silent ? 'ready' : ready ? 'limited' : 'offline');
    mode.querySelector('option[value="silent"]').disabled = !silent;
    advancedMode.querySelector('option[value="silent"]').disabled = !silent;
    if (!silent) { mode.value = 'prompt'; advancedMode.value = 'prompt'; }
    run.disabled = !ready || !selectedTemplate;
    runAdvanced.disabled = !ready;
  }
  function showTab(advanced) {
    quickPanel.hidden = advanced; advancedPanel.hidden = !advanced;
    quickButton.classList.toggle('active', !advanced); advancedButton.classList.toggle('active', advanced);
  }
  function renderCatalog() {
    commandGrid.replaceChildren();
    for (const command of catalog) {
      const card = node('button', undefined, 'command-card'); card.type = 'button';
      card.append(node('strong', command.name), node('span', command.description));
      card.addEventListener('click', () => {
        selectedTemplate = command.id;
        commandGrid.querySelectorAll('.command-card').forEach(item => item.classList.remove('selected'));
        card.classList.add('selected'); mode.value = command.defaultRunSilently && supportsSilent(selectedAgent()) ? 'silent' : 'prompt';
        updateReadiness();
      });
      commandGrid.append(card);
    }
  }
  async function queue(payload) {
    error.textContent = ''; run.disabled = true; runAdvanced.disabled = true;
    try {
      const result = await api('agents/' + agentSelect.value + '/commands', { method: 'POST', body: JSON.stringify(payload) });
      toast(result.displayName + ' queued'); script.value = ''; await loadHistory();
    } catch (exception) { error.textContent = exception.message; }
    finally { updateReadiness(); }
  }
  async function cancel(id) {
    error.textContent = '';
    try { await api('agents/' + agentSelect.value + '/commands/' + id + '/cancel', { method: 'POST', body: '{}' }); await loadHistory(); }
    catch (exception) { error.textContent = exception.message; }
  }
  async function loadHistory() {
    try {
      const data = await api('agents/' + agentSelect.value + '/commands'); history.replaceChildren();
      if (!data.items.length) { history.append(node('p', 'No support commands have been sent to this agent.', 'small muted')); return; }
      for (const job of data.items) {
        const card = node('article', undefined, 'command-record');
        const title = node('div', undefined, 'command-record-title');
        const titleText = node('div'); titleText.append(node('strong', job.displayName || 'PowerShell command'), node('span', (job.runSilently ? 'Silent' : 'Approval') + ' | ' + date(job.requestedAtUtc), 'small muted'));
        title.append(titleText, node('span', job.status, 'command-status ' + String(job.status).toLowerCase())); card.append(title);
        const details = node('details'); details.append(node('summary', 'View command'));
        details.append(node('pre', job.script)); if (job.output != null) details.append(node('h5', 'Result'), node('pre', job.output || '(No output)'));
        if (job.exitCode != null) details.append(node('p', 'Exit code: ' + job.exitCode, 'small muted'));
        card.append(details);
        if (job.status === 'Queued' || job.status === 'AwaitingApproval') {
          const cancelButton = node('button', 'Cancel', 'secondary compact'); cancelButton.type = 'button';
          cancelButton.addEventListener('click', () => cancel(job.id)); card.append(cancelButton);
        }
        history.append(card);
      }
    } catch (exception) { error.textContent = exception.message; }
  }

  quickButton.addEventListener('click', () => showTab(false)); advancedButton.addEventListener('click', () => showTab(true));
  agentSelect.addEventListener('change', () => { selectedTemplate = null; renderCatalog(); updateReadiness(); loadHistory(); });
  run.addEventListener('click', () => queue({ templateId: selectedTemplate, runSilently: mode.value === 'silent' }));
  runAdvanced.addEventListener('click', () => {
    if (!script.value.trim()) { error.textContent = 'Enter a PowerShell command.'; script.focus(); return; }
    queue({ script: script.value, runSilently: advancedMode.value === 'silent' });
  });
  api('commands/catalog').then(data => { catalog = data.items; renderCatalog(); updateReadiness(); }).catch(exception => { error.textContent = exception.message; });
  updateReadiness(); loadHistory();
};
