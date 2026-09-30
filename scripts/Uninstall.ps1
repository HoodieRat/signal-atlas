param([ValidateSet('ProgramOnly','ProgramAndCache','Everything')][string]$Mode='ProgramOnly')
$ErrorActionPreference='Stop'
$install=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\SignalAtlas'))
$expected=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\SignalAtlas'))
if($install -ne $expected){throw 'Unexpected installation path.'}
$diagnostics=Join-Path $install 'SignalAtlas.Diagnostics.exe'
if(Test-Path -LiteralPath $diagnostics){& $diagnostics --remove-scheduler | Out-Null}
$shell=New-Object -ComObject WScript.Shell
foreach($shortcut in @((Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\Signal Atlas.lnk'),(Join-Path ([Environment]::GetFolderPath('Desktop')) 'Signal Atlas.lnk'))){if(Test-Path -LiteralPath $shortcut){Remove-Item -LiteralPath $shortcut -Force}}
if(Test-Path -LiteralPath $install){Remove-Item -LiteralPath $install -Recurse -Force}
if($Mode -in @('ProgramAndCache','Everything')){
    $cache=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'SignalAtlas\cache'))
    if($cache -ne [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'SignalAtlas\cache'))){throw 'Unexpected cache path.'}
    if(Test-Path -LiteralPath $cache){Remove-Item -LiteralPath $cache -Recurse -Force}
}
if($Mode -eq 'Everything'){
    $data=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'SignalAtlas'))
    $reports=[IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Signal Atlas'))
    if($data -ne [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'SignalAtlas')) -or $reports -ne [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Signal Atlas'))){throw 'Unexpected data path.'}
    if(Test-Path -LiteralPath $data){Remove-Item -LiteralPath $data -Recurse -Force}
    if(Test-Path -LiteralPath $reports){Remove-Item -LiteralPath $reports -Recurse -Force}
}
Write-Host "Uninstall complete ($Mode). Node, DokoBot, LM Studio, browsers, and models were not changed."
