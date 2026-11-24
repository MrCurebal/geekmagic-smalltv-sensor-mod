param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version='1.5.0')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$dist=Join-Path $repo 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$stage=Join-Path $dist ('windows-stage-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
foreach($project in @('sender','control','startup')) {
    $source=Join-Path $repo ('clients\windows\'+$project)
    & dotnet publish $source -c Release -r win-x64 --self-contained true -o $stage -p:DebugType=None -p:DebugSymbols=false -v quiet
    if($LASTEXITCODE -ne 0){throw ('Build failed: '+$project)}
}
Copy-Item (Join-Path $repo 'config.example.json') $stage
Copy-Item (Join-Path $repo 'LICENSE') $stage
Copy-Item (Join-Path $repo 'THIRD_PARTY.md') $stage
Copy-Item (Join-Path $repo 'packaging\START-HERE.txt') $stage
$licenses=Join-Path $stage 'licenses'
New-Item -ItemType Directory -Path $licenses | Out-Null
Invoke-WebRequest 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/LICENSE' -OutFile (Join-Path $licenses 'LibreHardwareMonitor-LICENSE.txt')
Invoke-WebRequest 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/THIRD-PARTY-NOTICES.txt' -OutFile (Join-Path $licenses 'LibreHardwareMonitor-THIRD-PARTY-NOTICES.txt')
$runtime=Get-Content (Join-Path $stage 'GeekMagicControl.runtimeconfig.json') -Raw | ConvertFrom-Json
$coreVersion=($runtime.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
$desktopVersion=($runtime.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.WindowsDesktop.App').version
$cache=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'}
Copy-Item (Join-Path $cache ('microsoft.netcore.app.runtime.win-x64\'+$coreVersion+'\LICENSE.TXT')) (Join-Path $licenses 'dotnet-runtime-LICENSE.txt')
Copy-Item (Join-Path $cache ('microsoft.netcore.app.runtime.win-x64\'+$coreVersion+'\THIRD-PARTY-NOTICES.TXT')) (Join-Path $licenses 'dotnet-runtime-THIRD-PARTY-NOTICES.txt')
Copy-Item (Join-Path $cache ('microsoft.windowsdesktop.app.runtime.win-x64\'+$desktopVersion+'\LICENSE')) (Join-Path $licenses 'dotnet-windowsdesktop-LICENSE.txt')
foreach($privateFile in @('config.json','private_config.h','monitor.log','readings.json','stop.signal')) {
    if(Test-Path (Join-Path $stage $privateFile)){throw ('Unexpected private file: '+$privateFile)}
}
$archive=Join-Path $dist ('GeekMagic-Sensor-Mod-v'+$Version+'-windows-x64.zip')
if(Test-Path -LiteralPath $archive){throw 'Archive already exists. Choose another version or move the old archive yourself.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$archive,[IO.Compression.CompressionLevel]::Optimal,$false)
$hash=Get-FileHash -LiteralPath $archive -Algorithm SHA256
($hash.Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($archive)) | Set-Content -LiteralPath ($archive+'.sha256') -Encoding ascii
Write-Host $archive
