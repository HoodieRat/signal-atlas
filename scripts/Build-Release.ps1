param([string]$Configuration='Release')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$out=Join-Path $repo 'packaging\release'
New-Item -ItemType Directory -Force -Path $out | Out-Null
foreach($project in @('SignalAtlas.App','SignalAtlas.Runner','SignalAtlas.Worker','SignalAtlas.Diagnostics')) {
    $path=Join-Path $repo "src\$project\$project.csproj"
    & dotnet publish $path -c $Configuration -r win-x64 --self-contained true -p:PublishTrimmed=false -o $out
    if($LASTEXITCODE -ne 0){throw "Publish failed: $project"}
}
Copy-Item -LiteralPath (Join-Path $repo 'dependencies.lock.json') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $repo 'THIRD_PARTY_NOTICES.md') -Destination $out -Force
Write-Host "Release ready: $out"
