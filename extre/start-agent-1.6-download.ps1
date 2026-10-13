# Stage the 1.6 binary without keeping the agent command process alive.
$ErrorActionPreference='Stop'
Import-Module BitsTransfer
$root=Join-Path $env:LOCALAPPDATA 'MiLifeDeviceAgent'
$destination=Join-Path $root 'MiLifeDeviceAgent.exe.new'
Get-BitsTransfer -Name 'MiLifeAgent16' -ErrorAction SilentlyContinue | Remove-BitsTransfer
if(Test-Path -LiteralPath $destination){Remove-Item -LiteralPath $destination -Force}
$null=Start-BitsTransfer -Source 'https://uphill-cofounder-trident.ngrok-free.dev/download/device-agent' `
    -Destination $destination -DisplayName 'MiLifeAgent16' -Priority Foreground -Asynchronous
'Agent 1.6 download started with BITS.'
