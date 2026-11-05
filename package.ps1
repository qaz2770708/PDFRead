param([switch]$NoBuild, [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not $NoBuild) { & ./build.ps1 -NoRestore:$NoRestore }
    $stage = Join-Path $PSScriptRoot ('work/package-m3-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($name in @('PDFReader.exe','PDFReader.exe.config')) { Copy-Item -LiteralPath (Join-Path 'bin/Release/net48' $name) -Destination $stage }
    Get-ChildItem -LiteralPath 'bin/Release/net48' -Filter '*.dll' -File | Copy-Item -Destination $stage
    Copy-Item -LiteralPath 'outputs/README.md','samples/navigation.pdf','samples/continuous.pdf','samples/demo.pdf','docs/shortcuts.md','docs/M3-validation.md','docs/THIRD-PARTY-NOTICES.md' -Destination $stage
    Copy-Item -LiteralPath 'docs/licenses' -Destination (Join-Path $stage 'licenses') -Recurse
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath 'outputs/PDFReader-M3.zip' -Force
    Write-Output ('Packaged ' + (Join-Path $PSScriptRoot 'outputs/PDFReader-M3.zip'))
} finally { Pop-Location }
