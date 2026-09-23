[CmdletBinding()]
param([switch]$BuildCollector, [switch]$InitializeOnly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$local = Join-Path $repo 'artifacts\local'
New-Item -ItemType Directory -Path $local -Force | Out-Null
$settingsPath = Join-Path $local 'settings.json'
if (-not (Test-Path -LiteralPath $settingsPath)) {
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $first = New-Object byte[] 32
        $second = New-Object byte[] 32
        $rng.GetBytes($first)
        $rng.GetBytes($second)
        @{ IntakeToken = [Convert]::ToBase64String($first); AdminToken = [Convert]::ToBase64String($second) } |
            ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    } finally { $rng.Dispose() }
}
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$env:DEVICE_INTAKE_TOKEN = $settings.IntakeToken
$env:DEVICE_ADMIN_TOKEN = $settings.AdminToken
$env:DEVICE_COLLECTOR_API_URL = 'https://localhost:7088/api/device-intake'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'https://localhost:7088'
$env:ConnectionStrings__DeviceIntake = "Data Source=$(Join-Path $local 'device-intake.db');Default Timeout=10"
$env:Intake__CollectorPath = Join-Path $repo 'publish\collector\MiLifeDeviceCollector.exe'
if ($InitializeOnly) { Write-Host "Local settings initialized at $settingsPath"; return }
if ($BuildCollector) {
    & (Join-Path $PSScriptRoot 'build-collector.ps1')
    if (-not $?) { throw 'Collector build failed.' }
}
Write-Host 'Registration page: https://localhost:7088/device-registration'
Write-Host 'Local credentials are in artifacts/local/settings.json. Do not share or commit that file.'
dotnet run --project (Join-Path $repo 'src\MiLife.DeviceIntake.Api') --configuration Release --no-launch-profile
if ($LASTEXITCODE -ne 0) { throw 'Local API stopped with an error.' }
