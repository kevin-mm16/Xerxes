[CmdletBinding()]
param([string]$ApiUrl = $env:DEVICE_COLLECTOR_API_URL, [string]$EnrollmentToken = $env:DEVICE_INTAKE_TOKEN)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$configuration = Join-Path $repo 'src\MiLife.DeviceAgent\agent-settings.json'
$uri = $null
if (-not [Uri]::TryCreate($ApiUrl, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) { throw 'Supply the HTTPS intake URL.' }
if ($EnrollmentToken.Length -lt 32 -or $EnrollmentToken.Length -gt 512 -or $EnrollmentToken -match '[^!-~]') { throw 'Supply a valid enrollment token.' }
if (Test-Path -LiteralPath $configuration) { throw 'Move the existing agent-settings.json aside before building.' }
try {
    @{ ApiUrl = $ApiUrl; EnrollmentToken = $EnrollmentToken; CollectorVersion = '1.4.0' } | ConvertTo-Json | Set-Content -LiteralPath $configuration -Encoding UTF8
    dotnet publish (Join-Path $repo 'src\MiLife.DeviceAgent\MiLife.DeviceAgent.csproj') --configuration Release --runtime win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false --output (Join-Path $repo 'publish\agent')
    if ($LASTEXITCODE -ne 0) { throw 'Agent publish failed.' }
    Get-FileHash (Join-Path $repo 'publish\agent\MiLifeDeviceAgent.exe')
} finally { if (Test-Path -LiteralPath $configuration) { Remove-Item -LiteralPath $configuration -Force } }
