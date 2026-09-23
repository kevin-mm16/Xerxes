[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $repo 'publish\api'
$publishRoot = Join-Path $repo 'publish'
if ((Test-Path -LiteralPath $publishRoot) -and ((Get-Item -LiteralPath $publishRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'Publish directory must not be a link.'
}
if (Test-Path -LiteralPath $output) {
    $resolved = (Resolve-Path -LiteralPath $output).Path
    if ($resolved -ne $output -or -not $resolved.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe publish path.' }
    if ((Get-Item -LiteralPath $output).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Publish directory must not be a link.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
dotnet publish (Join-Path $repo 'src\MiLife.DeviceIntake.Api\MiLife.DeviceIntake.Api.csproj') --configuration Release --runtime linux-x64 --self-contained false --output $output
if ($LASTEXITCODE -ne 0) { throw 'API publish failed.' }
Write-Host "API published to $output"
