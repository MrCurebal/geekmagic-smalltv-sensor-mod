$ErrorActionPreference = 'Stop'
$child = Start-Process (Join-Path $PSScriptRoot 'MiniScreen.exe') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru -Wait
exit $child.ExitCode
