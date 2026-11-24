#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$folder = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'dist\windows'
if (-not (Test-Path (Join-Path $folder 'MiniScreen.exe'))) { throw 'Build the Windows apps into dist/windows first.' }
Copy-Item (Join-Path $PSScriptRoot 'StartSender.ps1') (Join-Path $folder 'StartSender.ps1') -Force
$taskName = 'GeekMagicSensorSender'
$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction -Execute (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -Argument ('-NoProfile -NonInteractive -WindowStyle Hidden -File "' + (Join-Path $folder 'StartSender.ps1') + '"') -WorkingDirectory $folder
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$trigger.Delay = 'PT15S'
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
Write-Host 'Startup installed. Launch dist/windows/GeekMagicControl.exe for the desktop controls.'
