param([switch]$DesktopShortcut)
$ErrorActionPreference='Stop'
if(-not [Environment]::Is64BitOperatingSystem -or [Environment]::OSVersion.Version.Major -lt 10){throw 'Windows 10/11 x64 is required.'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$release=Join-Path $repo 'packaging\release'
$install=Join-Path $env:LOCALAPPDATA 'Programs\SignalAtlas'
$data=Join-Path $env:LOCALAPPDATA 'SignalAtlas'
if(-not (Test-Path -LiteralPath (Join-Path $release 'SignalAtlas.App.exe'))){throw "Release files are missing from $release. Run Build-Release.ps1 first."}
New-Item -ItemType Directory -Force -Path $install | Out-Null
Copy-Item -Path (Join-Path $release '*') -Destination $install -Recurse -Force
foreach($part in @('data','documents','cache\searches','cache\temporary','logs\application','logs\worker','logs\dependency','state','config')){New-Item -ItemType Directory -Force -Path (Join-Path $data $part) | Out-Null}
$reports=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Signal Atlas'
New-Item -ItemType Directory -Force -Path (Join-Path $reports 'Reports') | Out-Null
$shell=New-Object -ComObject WScript.Shell
$start=Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\Signal Atlas.lnk'
$shortcut=$shell.CreateShortcut($start);$shortcut.TargetPath=Join-Path $install 'SignalAtlas.App.exe';$shortcut.WorkingDirectory=$install;$shortcut.Save()
if($DesktopShortcut){$desktop=Join-Path ([Environment]::GetFolderPath('Desktop')) 'Signal Atlas.lnk';$shortcut=$shell.CreateShortcut($desktop);$shortcut.TargetPath=Join-Path $install 'SignalAtlas.App.exe';$shortcut.WorkingDirectory=$install;$shortcut.Save()}
$diagnostics=Join-Path $install 'SignalAtlas.Diagnostics.exe'
& $diagnostics --full
if($LASTEXITCODE -ne 0){throw 'An essential diagnostic failed. Run Repair.bat for details.'}
& $diagnostics --register-scheduler
if($LASTEXITCODE -ne 0){Write-Warning 'The scheduled task could not be registered. Open Signal Atlas and review Schedule and Diagnostics.'}
if(-not (Get-Command lms -ErrorAction SilentlyContinue)){Write-Warning 'LM Studio CLI was not found. Install it if you choose local AI; OpenAI API and Codex are available as alternatives.'}
if(-not (Get-Command dokobot -ErrorAction SilentlyContinue)){Write-Warning 'DokoBot CLI was not found. Install the tested version of @dokobot/cli, then use the Browser setup in Signal Atlas.'}
Write-Host "Installed Signal Atlas at $install"
Write-Host 'Open it from the Start Menu to finish topics, browser bridge, AI provider, and schedule setup.'
