$ErrorActionPreference='Stop'
$install=Join-Path $env:LOCALAPPDATA 'Programs\SignalAtlas'
$diagnostics=Join-Path $install 'SignalAtlas.Diagnostics.exe'
if(-not (Test-Path -LiteralPath $diagnostics)){throw 'Signal Atlas is not installed. Run Setup.bat.'}
$shell=New-Object -ComObject WScript.Shell
$start=Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\Signal Atlas.lnk'
if(-not (Test-Path -LiteralPath $start)){$shortcut=$shell.CreateShortcut($start);$shortcut.TargetPath=Join-Path $install 'SignalAtlas.App.exe';$shortcut.WorkingDirectory=$install;$shortcut.Save()}
$state=Join-Path $env:LOCALAPPDATA 'SignalAtlas\state'
if(Test-Path -LiteralPath $state){Get-ChildItem -LiteralPath $state -Filter 'cancel-*.flag' -File | Where-Object LastWriteTime -lt (Get-Date).AddDays(-1) | Remove-Item -Force}
& $diagnostics --register-scheduler
if($LASTEXITCODE -ne 0){Write-Warning 'Task Scheduler registration needs attention.'}
& $diagnostics --full
if($LASTEXITCODE -ne 0){throw 'An essential diagnostic failed.'}
Write-Host 'Repair checks completed. Topics, reports, history, and model selection were preserved.'
