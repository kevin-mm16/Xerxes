$ErrorActionPreference='Stop'
$base='https://uphill-cofounder-trident.ngrok-free.dev'
$headers=@{'ngrok-skip-browser-warning'='true'}
$session=Invoke-RestMethod "$base/device-admin/api/session" -Headers $headers -SessionVariable webSession
$headers['X-CSRF-TOKEN']=$session.csrfToken
$body=@{username='root';password=$env:MILIFE_DASHBOARD_PASSWORD}|ConvertTo-Json
$null=Invoke-RestMethod "$base/device-admin/api/login" -Method Post -Headers $headers -WebSession $webSession -Body $body -ContentType 'application/json'
$session=Invoke-RestMethod "$base/device-admin/api/session" -Headers $headers -WebSession $webSession
$headers['X-CSRF-TOKEN']=$session.csrfToken
$serial='RECOVERY-CHECK-'+[Guid]::NewGuid().ToString('N').Substring(0,12).ToUpperInvariant()
$secrets=Get-Content artifacts/deployment/server-secrets.json -Raw|ConvertFrom-Json
$intakeHeaders=@{'ngrok-skip-browser-warning'='true';'X-Enrollment-Token'=$secrets.IntakeToken}
$inventory=@{collectionId=[Guid]::NewGuid().ToString();collectorVersion='1.2.0';collectedAtUtc=[DateTime]::UtcNow.ToString('o');serialNumber=$serial;computerName='RECOVERY VERIFICATION'}
$receipt=Invoke-RestMethod "$base/api/device-intake" -Method Post -Headers $intakeHeaders -Body ($inventory|ConvertTo-Json) -ContentType 'application/json'
$random=New-Object byte[] 32; $rng=[Security.Cryptography.RandomNumberGenerator]::Create();$rng.GetBytes($random);$rng.Dispose();$token=[BitConverter]::ToString($random).Replace('-','')
$agent=@{agentId=[Guid]::NewGuid().ToString();submissionId=$receipt.submissionId;serialNumber=$serial;agentVersion='1.2.0';deviceToken=$token;employeeName='Recovery Verification'}
@{AgentId=$agent.agentId;Serial=$serial}|ConvertTo-Json|Set-Content artifacts/deployment/recovery-fixture.json
$null=Invoke-RestMethod "$base/api/device-agent/enroll" -Method Post -Headers $intakeHeaders -Body ($agent|ConvertTo-Json) -ContentType 'application/json'
$null=Invoke-RestMethod "$base/device-admin/api/agents/$($agent.agentId)/revoke" -Method Post -Headers $headers -WebSession $webSession -Body '{}' -ContentType 'application/json'
$blocked=$false
try{$null=Invoke-RestMethod "$base/api/device-agent/enroll" -Method Post -Headers $intakeHeaders -Body ($agent|ConvertTo-Json) -ContentType 'application/json'}catch{if([int]$_.Exception.Response.StatusCode -eq 403){$blocked=$true}else{throw}}
if(!$blocked){throw 'Revoked enrollment was not blocked'}
$null=Invoke-RestMethod "$base/device-admin/api/agents/$($agent.agentId)/enable" -Method Post -Headers $headers -WebSession $webSession -Body '{}' -ContentType 'application/json'
$null=Invoke-RestMethod "$base/api/device-agent/enroll" -Method Post -Headers $intakeHeaders -Body ($agent|ConvertTo-Json) -ContentType 'application/json'
$beatHeaders=@{'ngrok-skip-browser-warning'='true';'X-Device-Token'=$token}
$beat=Invoke-RestMethod "$base/api/device-agent/heartbeat" -Method Post -Headers $beatHeaders -Body (@{agentId=$agent.agentId;agentVersion='1.2.0'}|ConvertTo-Json) -ContentType 'application/json'
if(!$beat.received){throw 'Restored agent heartbeat was rejected'}
$history=Invoke-RestMethod "$base/device-admin/api/agents/$($agent.agentId)/actions" -Headers $headers -WebSession $webSession
if(!$history.items.Where({$_.action -eq 'Enable' -and $_.requestedBy -eq 'root'})){throw 'Recovery audit missing'}
$null=Invoke-RestMethod "$base/device-admin/api/logout" -Method Post -Headers $headers -WebSession $webSession -Body '{}' -ContentType 'application/json'
Write-Host 'PASS: revoked 1.2 test agent restored by admin, re-enrolled, heartbeat accepted, recovery audited.'
