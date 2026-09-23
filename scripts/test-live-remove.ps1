param([ValidateSet('Disable','Remove')][string]$Action='Remove')
$ErrorActionPreference='Stop'
$base='https://uphill-cofounder-trident.ngrok-free.dev'
$headers=@{'ngrok-skip-browser-warning'='true'}
$session=Invoke-RestMethod "$base/device-admin/api/session" -Headers $headers -SessionVariable webSession
$headers['X-CSRF-TOKEN']=$session.csrfToken
$body=@{username='root';password=$env:MILIFE_DASHBOARD_PASSWORD}|ConvertTo-Json
$null=Invoke-RestMethod "$base/device-admin/api/login" -Method Post -Headers $headers -WebSession $webSession -Body $body -ContentType 'application/json'
$session=Invoke-RestMethod "$base/device-admin/api/session" -Headers $headers -WebSession $webSession
$headers['X-CSRF-TOKEN']=$session.csrfToken
$devices=Invoke-RestMethod "$base/device-admin/api/devices?search=IT%20Acceptance%20Test" -Headers $headers -WebSession $webSession
$serial=[Uri]::EscapeDataString($devices.items[0].serialNumber)
$agents=Invoke-RestMethod "$base/device-admin/api/agents?serialNumber=$serial" -Headers $headers -WebSession $webSession
$agent=$agents.items|Where-Object {$_.employeeName -eq 'IT Acceptance Test' -and $_.agentVersion -eq '1.3.0'}|Select-Object -First 1
if(!$agent){throw 'Test agent missing'}
$route="$base/device-admin/api/agents/$($agent.id)/actions"
$history=Invoke-RestMethod $route -Headers $headers -WebSession $webSession
$job=$history.items|Where-Object {$_.action -eq $Action -and $_.status -ne 'Failed'}|Select-Object -First 1
if(!$job){$job=Invoke-RestMethod $route -Method Post -Headers $headers -WebSession $webSession -Body (@{action=$Action}|ConvertTo-Json) -ContentType 'application/json'}
Write-Host ($Action+' queued for isolated test agent. If it is stopped, start it locally.')
for($attempt=0;$attempt -lt 90;$attempt++){
 Start-Sleep -Seconds 2
 $history=Invoke-RestMethod $route -Headers $headers -WebSession $webSession
 $current=$history.items|Where-Object id -eq $job.id
 if($current.status -eq 'Completed'){Write-Host ('PASS: '+$Action+' confirmed and audited.');break}
 if($current.status -eq 'Failed'){throw $current.error}
}
if($current.status -ne 'Completed'){throw 'Removal confirmation did not arrive'}
$null=Invoke-RestMethod "$base/device-admin/api/logout" -Method Post -Headers $headers -WebSession $webSession -Body '{}' -ContentType 'application/json'
