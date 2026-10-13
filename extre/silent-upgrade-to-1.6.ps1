# MiLife agent 1.6 silent updater. Run as the signed-in user, never elevated.
# Safe to queue as a silent custom PowerShell command after replacing the hash for the deployed release.
$ErrorActionPreference = 'Stop'
$releaseUri = [Uri]'https://uphill-cofounder-trident.ngrok-free.dev/download/device-agent'
$expectedSha256 = '7C65A6FF3631F4D0E8088EA28514789F72A18637945AF3A0B7E3C95DB4A040E4'

function Invoke-MiLifeUpgrade {
    $ErrorActionPreference = 'Stop'
    $root = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MiLifeDeviceAgent'))
    $target = Join-Path $root 'MiLifeDeviceAgent.exe'
    $download = Join-Path $root 'MiLifeDeviceAgent.exe.new'
    $backup = Join-Path $root 'MiLifeDeviceAgent.exe.rollback'
    $log = Join-Path $root 'upgrade-1.6.log'
    function Log([string]$text) { Add-Content -LiteralPath $log -Value (('{0:o} {1}' -f [DateTime]::UtcNow,$text)) -Encoding UTF8 }
    function Register-AgentTask {
        $service = New-Object -ComObject 'Schedule.Service'; $service.Connect(); $folder = $service.GetFolder('\'); $definition = $service.NewTask(0)
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        $definition.RegistrationInfo.Description = 'MiLife company device inventory and IT support agent'
        $definition.Principal.UserId = $identity; $definition.Principal.LogonType = 3; $definition.Principal.RunLevel = 0
        $definition.Settings.Enabled = $true; $definition.Settings.StartWhenAvailable = $true
        $definition.Settings.DisallowStartIfOnBatteries = $false; $definition.Settings.StopIfGoingOnBatteries = $false
        $definition.Settings.MultipleInstances = 2; $definition.Settings.ExecutionTimeLimit = 'PT0S'
        $definition.Settings.RestartCount = 3; $definition.Settings.RestartInterval = 'PT1M'
        $trigger = $definition.Triggers.Create(9); $trigger.UserId = $identity
        $action = $definition.Actions.Create(0); $action.Path = $target; $action.Arguments = '--background'; $action.WorkingDirectory = $root
        $null = $folder.RegisterTaskDefinition('MiLife Device Heartbeat',$definition,6,$null,$null,3,$null)
        return $folder.GetTask('\MiLife Device Heartbeat')
    }
    try {
        if (!(Test-Path -LiteralPath $target -PathType Leaf)) { throw 'Installed agent is missing' }
        if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked agent directory refused' }
        if ($releaseUri.Scheme -ne 'https' -or $releaseUri.UserInfo) { throw 'Release URL refused' }
        $handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
        $client = [Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromMinutes(30)
        try {
            $response = $client.GetAsync($releaseUri,[Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            if (!$response.IsSuccessStatusCode) { throw "Download failed: $([int]$response.StatusCode)" }
            $input = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult(); $output = [IO.File]::Create($download)
            try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
        } finally { $client.Dispose(); $handler.Dispose() }
        $actual = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash
        if ($actual -ne $expectedSha256) { throw "Release hash mismatch: $actual" }
        Log 'Download and hash verification completed'
        $agents = @(Get-CimInstance Win32_Process -Filter "Name='MiLifeDeviceAgent.exe'" | Where-Object { [IO.Path]::GetFullPath($_.ExecutablePath) -eq $target })
        foreach ($agent in $agents) { Stop-Process -Id $agent.ProcessId -Force -ErrorAction Stop }
        for ($attempt=0; $attempt -lt 30 -and (Get-Process -Name MiLifeDeviceAgent -ErrorAction SilentlyContinue); $attempt++) { Start-Sleep -Milliseconds 500 }
        if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
        Move-Item -LiteralPath $target -Destination $backup
        Move-Item -LiteralPath $download -Destination $target
        $task = Register-AgentTask
        $startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'MiLife Device Heartbeat.lnk'
        if (Test-Path -LiteralPath $startup) { Remove-Item -LiteralPath $startup -Force }
        $null = $task.Run($null); Start-Sleep -Seconds 10
        $running = @(Get-CimInstance Win32_Process -Filter "Name='MiLifeDeviceAgent.exe'" | Where-Object { [IO.Path]::GetFullPath($_.ExecutablePath) -eq $target })
        if (!$running) { throw 'The 1.6 agent did not remain running' }
        Remove-Item -LiteralPath $backup -Force; Log 'Upgrade completed successfully'
    } catch {
        Log ('Upgrade failed: ' + $_.Exception.Message)
        if ((Test-Path -LiteralPath $backup) -and !(Get-Process -Name MiLifeDeviceAgent -ErrorAction SilentlyContinue)) {
            if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
            Move-Item -LiteralPath $backup -Destination $target; $task = Register-AgentTask; $null = $task.Run($null); Log 'Rollback started'
        }
        if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download -Force }
        throw
    }
}

# Detach immediately so the agent's 60-second command limit cannot interrupt a slow download.
$body = '${releaseUri}=[Uri]''' + $releaseUri.AbsoluteUri + "';`n" + '$expectedSha256=''' + $expectedSha256 + "';`nfunction Invoke-MiLifeUpgrade {`n" + ${function:Invoke-MiLifeUpgrade} + "`n}`nInvoke-MiLifeUpgrade"
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($body))
Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoLogo','-NoProfile','-NonInteractive','-STA','-EncodedCommand',$encoded) -WindowStyle Hidden
'MiLife 1.6 upgrade started in the background.'
