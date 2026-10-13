# Queue only after the MiLifeAgent16 BITS job reports Transferred.
$ErrorActionPreference='Stop'
$root=Join-Path $env:LOCALAPPDATA 'MiLifeDeviceAgent'
$target=Join-Path $root 'MiLifeDeviceAgent.exe'
$new=Join-Path $root 'MiLifeDeviceAgent.exe.new'
$hash='7C65A6FF3631F4D0E8088EA28514789F72A18637945AF3A0B7E3C95DB4A040E4'
Import-Module BitsTransfer
$bits=Get-BitsTransfer -Name 'MiLifeAgent16' -ErrorAction Stop
if($bits.JobState -ne 'Transferred'){throw "Download is $($bits.JobState)"}
Complete-BitsTransfer -BitsJob $bits
if((Get-FileHash -LiteralPath $new -Algorithm SHA256).Hash -ne $hash){Remove-Item $new -Force;throw 'Agent hash mismatch'}
$updater=Join-Path $root 'finish-upgrade-1.6.ps1'
$code=@'
$ErrorActionPreference='Stop'
$root=Join-Path $env:LOCALAPPDATA 'MiLifeDeviceAgent';$target=Join-Path $root 'MiLifeDeviceAgent.exe';$new=Join-Path $root 'MiLifeDeviceAgent.exe.new';$old=Join-Path $root 'MiLifeDeviceAgent.exe.rollback';$launch=Join-Path $root 'MiLifeDeviceAgent-1.6-launch.exe';$log=Join-Path $root 'upgrade-1.6.log'
function Log($s){Add-Content $log ((Get-Date).ToUniversalTime().ToString('o')+' '+$s)}
try{
 Start-Sleep 15
 Get-CimInstance Win32_Process -Filter "Name='MiLifeDeviceAgent.exe'"|?{[IO.Path]::GetFullPath($_.ExecutablePath)-eq $target}|%{Stop-Process -Id $_.ProcessId -Force}
 Start-Sleep 2;if(Test-Path $old){Remove-Item $old -Force};Move-Item $target $old;Move-Item $new $target;Copy-Item $target $launch
 Start-Process $launch -ArgumentList '--background' -WindowStyle Hidden;Start-Sleep 25
 $running=@(Get-CimInstance Win32_Process -Filter "Name='MiLifeDeviceAgent.exe'"|?{[IO.Path]::GetFullPath($_.ExecutablePath)-eq $target})
 if(!$running){throw 'Version 1.6 did not remain running'}
 Remove-Item $old -Force;Log 'Upgrade completed successfully'
}catch{
 Log ('Upgrade failed: '+$_.Exception.Message)
 if((Test-Path $old)-and !(Get-Process MiLifeDeviceAgent -ErrorAction SilentlyContinue)){if(Test-Path $target){Remove-Item $target -Force};Move-Item $old $target;Start-Process $target -ArgumentList '--background' -WindowStyle Hidden;Log 'Rollback started'}
}
try{$s=New-Object -ComObject 'Schedule.Service';$s.Connect();$s.GetFolder('\').DeleteTask('MiLife Agent Upgrade 1.6',0)}catch{}
'@
[IO.File]::WriteAllText($updater,$code,[Text.UTF8Encoding]::new($false))
$service=New-Object -ComObject 'Schedule.Service';$service.Connect();$folder=$service.GetFolder('\');$definition=$service.NewTask(0);$identity=[Security.Principal.WindowsIdentity]::GetCurrent().Name
$definition.Principal.UserId=$identity;$definition.Principal.LogonType=3;$definition.Principal.RunLevel=0;$definition.Settings.StartWhenAvailable=$true;$definition.Settings.ExecutionTimeLimit='PT5M'
$trigger=$definition.Triggers.Create(1);$trigger.StartBoundary=(Get-Date).AddMinutes(1).ToString('s')
$action=$definition.Actions.Create(0);$action.Path=(Join-Path $PSHOME 'powershell.exe');$action.Arguments="-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$updater`""
$null=$folder.RegisterTaskDefinition('MiLife Agent Upgrade 1.6',$definition,6,$null,$null,3,$null)
'Verified agent 1.6; replacement scheduled.'
