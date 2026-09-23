[CmdletBinding()]
param([string]$Executable = (Join-Path $PSScriptRoot '..\artifacts\deployment\downloaded-collector.exe'))
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
$logs = Join-Path $PSScriptRoot '..\artifacts\deployment'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this acceptance test from a non-elevated session.' }
Write-Host 'Running real hardware collection twice against the endpoint embedded in this EXE.'
for ($run = 1; $run -le 2; $run++) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $exe
    $info.WorkingDirectory = Split-Path $exe
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $collector = New-Object Diagnostics.Process
    $collector.StartInfo = $info
    try {
        [void]$collector.Start()
        $collector.StandardInput.Close()
        $output = $collector.StandardOutput.ReadToEndAsync()
        $errors = $collector.StandardError.ReadToEndAsync()
        if (-not $collector.WaitForExit(180000)) { $collector.Kill(); throw 'Collector exceeded acceptance-test timeout.' }
        $output.Result | Set-Content -LiteralPath (Join-Path $logs "public-collector-$run.stdout.log")
        $errors.Result | Set-Content -LiteralPath (Join-Path $logs "public-collector-$run.stderr.log")
        if ($collector.ExitCode -ne 0 -or $output.Result -notmatch 'Device information submitted successfully') {
            throw "Public collector run $run failed; see ignored artifacts/deployment logs."
        }
        Write-Host "PASS: public collector run $run, non-elevated, successful HTTPS submission."
    } finally { $collector.Dispose() }
}
