[CmdletBinding()]
param([string]$BaseUrl = 'https://uphill-cofounder-trident.ngrok-free.dev')
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:MILIFE_DASHBOARD_PASSWORD)) { throw 'Set MILIFE_DASHBOARD_PASSWORD.' }

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$secrets = Get-Content (Join-Path $repo 'artifacts\deployment\server-secrets.json') -Raw | ConvertFrom-Json
$common = @{ 'ngrok-skip-browser-warning' = 'true' }
$session = Invoke-RestMethod "$BaseUrl/device-admin/api/session" -Headers $common -SessionVariable webSession
$dashboard = $common.Clone(); $dashboard['X-CSRF-TOKEN'] = $session.csrfToken
$login = @{ username = 'root'; password = $env:MILIFE_DASHBOARD_PASSWORD } | ConvertTo-Json
$null = Invoke-RestMethod "$BaseUrl/device-admin/api/login" -Method Post -Headers $dashboard -WebSession $webSession -Body $login -ContentType 'application/json'
$session = Invoke-RestMethod "$BaseUrl/device-admin/api/session" -Headers $common -WebSession $webSession
$dashboard['X-CSRF-TOKEN'] = $session.csrfToken

$serial = 'PUBLIC-STACK-' + [Guid]::NewGuid().ToString('N').Substring(0,12).ToUpperInvariant()
$agentId = [Guid]::NewGuid()
$fixture = @{ Serial = $serial; AgentId = $agentId }
$fixture | ConvertTo-Json | Set-Content (Join-Path $repo 'artifacts\deployment\public-stack-fixture.json')
$intakeHeaders = $common.Clone(); $intakeHeaders['X-Enrollment-Token'] = $secrets.IntakeToken
$inventory = @{ collectionId = [Guid]::NewGuid(); collectorVersion = '1.3.1'; collectedAtUtc = [DateTime]::UtcNow.ToString('o')
    serialNumber = $serial; computerName = 'PUBLIC-STACK-TEST' }
$receipt = Invoke-RestMethod "$BaseUrl/api/device-intake" -Method Post -Headers $intakeHeaders -Body ($inventory | ConvertTo-Json) -ContentType 'application/json'
$random = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($random) } finally { $rng.Dispose() }
$deviceToken = [BitConverter]::ToString($random).Replace('-', '')
$agent = @{ agentId = $agentId; submissionId = $receipt.submissionId; serialNumber = $serial
    agentVersion = '1.3.1'; deviceToken = $deviceToken; employeeName = 'Public Stack Test' }
$null = Invoke-RestMethod "$BaseUrl/api/device-agent/enroll" -Method Post -Headers $intakeHeaders -Body ($agent | ConvertTo-Json) -ContentType 'application/json'
$deviceHeaders = $common.Clone(); $deviceHeaders['X-Device-Token'] = $deviceToken
$beat = Invoke-RestMethod "$BaseUrl/api/device-agent/heartbeat" -Method Post -Headers $deviceHeaders -Body (@{agentId=$agentId;agentVersion='1.3.1'} | ConvertTo-Json) -ContentType 'application/json'
if (-not $beat.received) { throw 'Initial public heartbeat was rejected.' }

$command = Invoke-RestMethod "$BaseUrl/device-admin/api/agents/$agentId/commands" -Method Post -Headers $dashboard -WebSession $webSession -Body (@{script="Write-Output 'public-stack-ok'"} | ConvertTo-Json) -ContentType 'application/json'
$next = Invoke-RestMethod "$BaseUrl/api/device-agent/$agentId/commands/next" -Headers $deviceHeaders
if ($next.id -ne $command.id) { throw 'The public support command was not delivered.' }
$null = Invoke-RestMethod "$BaseUrl/api/device-agent/commands/$($command.id)/decision" -Method Post -Headers $deviceHeaders -Body (@{agentId=$agentId;approved=$true} | ConvertTo-Json) -ContentType 'application/json'
$null = Invoke-RestMethod "$BaseUrl/api/device-agent/commands/$($command.id)/result" -Method Post -Headers $deviceHeaders -Body (@{agentId=$agentId;output='public-stack-ok';exitCode=0;timedOut=$false} | ConvertTo-Json) -ContentType 'application/json'
$commands = Invoke-RestMethod "$BaseUrl/device-admin/api/agents/$agentId/commands" -Headers $common -WebSession $webSession
if (-not $commands.items.Where({$_.id -eq $command.id -and $_.status -eq 'Completed'})) { throw 'The public support result was not recorded.' }

$disable = Invoke-RestMethod "$BaseUrl/device-admin/api/agents/$agentId/actions" -Method Post -Headers $dashboard -WebSession $webSession -Body (@{action='Disable'} | ConvertTo-Json) -ContentType 'application/json'
$pending = Invoke-RestMethod "$BaseUrl/api/device-agent/$agentId/action" -Headers $deviceHeaders
if ($pending.id -ne $disable.id -or $pending.action -ne 'Disable') { throw 'Disable was not delivered.' }
foreach ($status in 'Acknowledged','Completed') {
    $null = Invoke-RestMethod "$BaseUrl/api/device-agent/actions/$($disable.id)/status" -Method Post -Headers $deviceHeaders -Body (@{agentId=$agentId;status=$status} | ConvertTo-Json) -ContentType 'application/json'
}
$null = Invoke-RestMethod "$BaseUrl/device-admin/api/agents/$agentId/enable" -Method Post -Headers $dashboard -WebSession $webSession -Body '{}' -ContentType 'application/json'
$null = Invoke-RestMethod "$BaseUrl/api/device-agent/enroll" -Method Post -Headers $intakeHeaders -Body ($agent | ConvertTo-Json) -ContentType 'application/json'

$remove = Invoke-RestMethod "$BaseUrl/device-admin/api/agents/$agentId/actions" -Method Post -Headers $dashboard -WebSession $webSession -Body (@{action='Remove'} | ConvertTo-Json) -ContentType 'application/json'
$pending = Invoke-RestMethod "$BaseUrl/api/device-agent/$agentId/action" -Headers $deviceHeaders
if ($pending.id -ne $remove.id -or $pending.action -ne 'Remove') { throw 'Remove was not delivered.' }
foreach ($status in 'Acknowledged','Completed') {
    $null = Invoke-RestMethod "$BaseUrl/api/device-agent/actions/$($remove.id)/status" -Method Post -Headers $deviceHeaders -Body (@{agentId=$agentId;status=$status} | ConvertTo-Json) -ContentType 'application/json'
}
$actions = Invoke-RestMethod "$BaseUrl/device-admin/api/agents/$agentId/actions" -Headers $common -WebSession $webSession
if (-not $actions.items.Where({$_.id -eq $remove.id -and $_.status -eq 'Completed'})) { throw 'Removal completion was not recorded.' }
$null = Invoke-RestMethod "$BaseUrl/device-admin/api/logout" -Method Post -Headers $dashboard -WebSession $webSession -Body '{}' -ContentType 'application/json'
Write-Host "PASS: public intake, enrollment, heartbeat, approved support result, disable, re-enable and removal lifecycle ($serial)."
