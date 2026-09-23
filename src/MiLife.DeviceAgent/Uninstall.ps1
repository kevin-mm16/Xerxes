# Embedded, fixed uninstall helper. No script is downloaded from the server.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$folderName = if ($acceptanceTest) { 'MiLifeDeviceAgent.AcceptanceTest' } else { 'MiLifeDeviceAgent' }
$root = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) $folderName))
$expected = [IO.Path]::Combine([Environment]::GetFolderPath('LocalApplicationData'), $folderName)
if ($root -ne $expected -or !(Test-Path -LiteralPath $root -PathType Container)) { exit 1 }
if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { exit 1 }
$profilePath = Join-Path $root 'profile.dat'
$profile = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($profilePath), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)) | ConvertFrom-Json
$endpoint = [Uri]$profile.RemovalApiUrl
if ($endpoint.Scheme -ne 'https' -or $endpoint.UserInfo -or $endpoint.AbsolutePath -ne ('/api/device-agent/actions/' + $profile.RemovalActionId + '/status')) { exit 1 }
$headers = @{'X-Device-Token'=$profile.DeviceToken; 'ngrok-skip-browser-warning'='true'}
$shortcutName = if ($acceptanceTest) { 'MiLife Device Heartbeat Acceptance Test.lnk' } else { 'MiLife Device Heartbeat.lnk' }
$shortcut = Join-Path ([Environment]::GetFolderPath('Startup')) $shortcutName
function Report([string]$status, [string]$errorText) {
    $body = @{agentId=$profile.AgentId;status=$status;error=$errorText} | ConvertTo-Json -Compress
    for ($attempt=0; $attempt -lt 3; $attempt++) {
        try { $null = Invoke-RestMethod -Uri $endpoint.AbsoluteUri -Method Post -Headers $headers -Body $body -ContentType 'application/json' -TimeoutSec 15 -MaximumRedirection 0; return }
        catch { Start-Sleep -Seconds 2 }
    }
}
try {
    # Parent waits for this marker before exiting. Credentials stay inside DPAPI storage/memory, not command-line arguments.
    $parent = Get-Process -Id $agentParentPid -ErrorAction SilentlyContinue
    [IO.File]::WriteAllText((Join-Path $root 'uninstall-ready'), 'ready')
    if ($parent -and !$parent.WaitForExit(60000)) { throw 'Agent did not exit' }
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
    for ($attempt=0; $attempt -lt 20; $attempt++) {
        try {
            if (Test-Path -LiteralPath $root) {
                if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked directory refused' }
                $links = @(Get-ChildItem -LiteralPath $root -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
                if ($links.Count -gt 0) { throw 'Linked content refused' }
                # Absolute, fixed application directory checked above; never delete user-selected paths.
                Remove-Item -LiteralPath $root -Recurse -Force
            }
            break
        } catch { if ($attempt -eq 19) { throw }; Start-Sleep -Milliseconds 500 }
    }
    if ((Test-Path -LiteralPath $root) -or (Test-Path -LiteralPath $shortcut)) { throw 'Files remain' }
    Report 'Completed' $null
} catch { Report 'Failed' 'Cleanup could not remove all agent files. Start the agent again or contact IT for local removal.' }
