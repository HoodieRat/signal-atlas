param([string]$Version = '0.1.0-preview')
$ErrorActionPreference = 'Stop'
if($Version -notmatch '^[0-9A-Za-z.-]+$'){throw 'Version contains invalid characters.'}

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$validation = [IO.Path]::GetFullPath((Join-Path $repo 'packaging\validation'))
$release = Join-Path $repo 'packaging\release'
& (Join-Path $PSScriptRoot 'Build-Release.ps1')
if($LASTEXITCODE -ne 0){throw 'Release build failed.'}

New-Item -ItemType Directory -Force -Path $validation | Out-Null
$stage = Join-Path $validation ('release-stage-' + [Guid]::NewGuid().ToString('N'))
$bundle = Join-Path $stage 'SignalAtlas'
$output = Join-Path $validation ("SignalAtlas-v$Version-win-x64.zip")
try {
    New-Item -ItemType Directory -Force -Path (Join-Path $bundle 'packaging\release') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $bundle 'docs') | Out-Null
    Copy-Item -Path (Join-Path $release '*') -Destination (Join-Path $bundle 'packaging\release') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $repo 'scripts') -Destination $bundle -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $repo 'demo') -Destination $bundle -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $repo 'README.md') -Destination $bundle
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $bundle
    Copy-Item -LiteralPath (Join-Path $repo 'THIRD_PARTY_NOTICES.md') -Destination $bundle
    Copy-Item -LiteralPath (Join-Path $repo 'docs\AI_PROVIDERS.md') -Destination (Join-Path $bundle 'docs')
    Copy-Item -LiteralPath (Join-Path $repo 'docs\LINKEDIN_DESIGN.md') -Destination (Join-Path $bundle 'docs')
    Compress-Archive -LiteralPath $bundle -DestinationPath $output -CompressionLevel Optimal -Force
    Write-Host "Package ready: $output"
    Get-FileHash -Algorithm SHA256 -LiteralPath $output | Select-Object Hash,Path
}
finally {
    $absoluteStage = [IO.Path]::GetFullPath($stage)
    if($absoluteStage.StartsWith($validation + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $absoluteStage)) {
        Remove-Item -LiteralPath $absoluteStage -Recurse -Force
    }
}
