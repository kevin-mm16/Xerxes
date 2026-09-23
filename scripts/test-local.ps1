[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $PSScriptRoot 'start-local.ps1') -InitializeOnly
$local = Join-Path $repo 'artifacts\local'
$exe = Join-Path $repo 'publish\collector\MiLifeDeviceCollector.exe'
$apiDll = Join-Path $repo 'src\MiLife.DeviceIntake.Api\bin\Release\net8.0\MiLife.DeviceIntake.Api.dll'
if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $apiDll)) { throw 'Build the API and the local-configured collector first.' }
$settings = Get-Content -LiteralPath (Join-Path $local 'settings.json') -Raw | ConvertFrom-Json
$headers = @{ 'X-Admin-Token' = $settings.AdminToken }
$baseUrl = 'https://localhost:7088'
$apiProcess = $null
try {
    $apiProcess = Start-Process -FilePath 'dotnet' -ArgumentList @('"' + $apiDll + '"') -WorkingDirectory (Split-Path $apiDll) `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $local 'api-smoke.stdout.log') `
        -RedirectStandardError (Join-Path $local 'api-smoke.stderr.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($apiProcess.HasExited) { throw 'Local API exited. Inspect artifacts/local/api-smoke logs.' }
        try {
            $health = Invoke-RestMethod "$baseUrl/health" -TimeoutSec 2
            if ($health.status -eq 'healthy') { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw 'Local HTTPS API is not reachable. Ensure the localhost development certificate is trusted and port 7088 is free.' }
    $before = Invoke-RestMethod "$baseUrl/api/admin/submissions" -Headers $headers
    for ($run = 1; $run -le 2; $run++) {
        # Redirected stdin prevents the normal interactive "press any key" pause.
        # The test operator knows this runs real hardware collection on the local PC.
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
            if (-not $collector.WaitForExit(180000)) { $collector.Kill(); throw 'Collector exceeded the smoke-test time limit.' }
            $output.Result | Set-Content -LiteralPath (Join-Path $local "collector-smoke-$run.stdout.log")
            $errors.Result | Set-Content -LiteralPath (Join-Path $local "collector-smoke-$run.stderr.log")
            if ($collector.ExitCode -ne 0) { throw "Collector run $run did not submit. Inspect the local smoke log and recovery copy." }
        } finally { $collector.Dispose() }
    }
    $after = Invoke-RestMethod "$baseUrl/api/admin/submissions" -Headers $headers
    if ($after.total -ne ($before.total + 2)) { throw 'Expected two new historical submissions.' }
    $latest = $after.items[0]
    if (-not $latest.isRepeat -or $latest.status -ne 'PendingReview') { throw 'Repeat detection or staging status was incorrect.' }
    $detail = Invoke-RestMethod "$baseUrl/api/admin/submissions/$($latest.id)" -Headers $headers
    if (-not $detail.hardware.serialNumber -or -not $detail.hardware.computerName) { throw 'Hardware identification was missing.' }
    $download = Join-Path $local 'downloaded-collector.exe'
    Invoke-WebRequest "$baseUrl/download/device-collector" -OutFile $download -UseBasicParsing
    if ((Get-FileHash -LiteralPath $exe).Hash -ne (Get-FileHash -LiteralPath $download).Hash) { throw 'Downloaded file did not match the published EXE.' }
    Write-Host 'PASS: real Windows collector -> validated HTTPS -> SQLite staging; repeat history and download hash verified.'
    Write-Host 'Two real local inventory records were retained in artifacts/local/device-intake.db. Detailed output stays in artifacts/local.'
} finally {
    if ($apiProcess -and -not $apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id }
}
