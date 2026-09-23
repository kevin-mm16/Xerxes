[CmdletBinding()]
param(
    [string]$ApiUrl = $env:DEVICE_COLLECTOR_API_URL,
    [string]$EnrollmentToken = $env:DEVICE_INTAKE_TOKEN,
    [string]$Version = '1.0.0',
    [switch]$Unconfigured,
    [string]$SigningScript
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repo 'src\MiLife.DeviceCollector\MiLife.DeviceCollector.csproj'
$configuration = Join-Path $repo 'src\MiLife.DeviceCollector\collector-settings.json'
$output = Join-Path $repo 'publish\collector'
$publishRoot = Join-Path $repo 'publish'
if ((Test-Path -LiteralPath $publishRoot) -and ((Get-Item -LiteralPath $publishRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'Publish directory must not be a link.'
}
if (-not $Unconfigured) {
    $uri = $null
    if (-not [Uri]::TryCreate($ApiUrl, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) {
        throw 'Supply an HTTPS intake URL with no credentials, query, or fragment.'
    }
    if ($EnrollmentToken.Length -lt 32 -or $EnrollmentToken.Length -gt 512 -or $EnrollmentToken -match '[^!-~]' -or $EnrollmentToken.Contains('CHANGE_ME')) {
        throw 'Supply a printable enrollment token of 32-512 characters through DEVICE_INTAKE_TOKEN.'
    }
} else {
    $ApiUrl = 'https://example.ngrok.app/api/device-intake'
    $EnrollmentToken = 'CHANGE_ME'
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must have the form 1.0.0.' }
if (Test-Path -LiteralPath $configuration) { throw 'A collector-settings.json file already exists. Move it aside before building to avoid overwriting local configuration.' }
if (Test-Path -LiteralPath $output) {
    $resolved = (Resolve-Path -LiteralPath $output).Path
    if ($resolved -ne $output -or -not $resolved.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe publish path.' }
    if ((Get-Item -LiteralPath $output).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Publish directory must not be a link.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
try {
    @{ ApiUrl = $ApiUrl; EnrollmentToken = $EnrollmentToken; CollectorVersion = $Version } |
        ConvertTo-Json | Set-Content -LiteralPath $configuration -Encoding UTF8
    dotnet publish $project --configuration Release --runtime win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" --output $output
    if ($LASTEXITCODE -ne 0) { throw 'Collector publish failed.' }
    $exe = Join-Path $output 'MiLifeDeviceCollector.exe'
    if ($SigningScript) {
        & $SigningScript -FilePath $exe
        if (-not $?) { throw 'Signing stage failed.' }
        if ((Get-AuthenticodeSignature -LiteralPath $exe).Status -ne 'Valid') { throw 'Signing stage did not produce a valid signature.' }
    }
    Get-FileHash -LiteralPath $exe -Algorithm SHA256 | Format-List
    if ($Unconfigured) { Write-Warning 'This build is for testing only. It cannot submit until rebuilt with a URL and enrollment token.' }
} finally {
    if (Test-Path -LiteralPath $configuration) { Remove-Item -LiteralPath $configuration -Force }
}
