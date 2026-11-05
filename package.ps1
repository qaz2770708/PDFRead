param([switch]$NoBuild, [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not $NoBuild) { & ./build.ps1 -NoRestore:$NoRestore }
    $stage = Join-Path $PSScriptRoot ('work/package-heroui-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($name in @('PDFReader.exe','PDFReader.exe.config')) { Copy-Item -LiteralPath (Join-Path 'outputs' $name) -Destination $stage }
    Get-ChildItem -LiteralPath 'outputs' -Filter '*.dll' -File | Copy-Item -Destination $stage
    if (-not (Test-Path 'outputs/web/index.html')) { throw 'HeroUI web assets missing' }
    Copy-Item -LiteralPath 'outputs/web' -Destination $stage -Recurse
    foreach ($runtimeFolder in @('runtimes','x86','x64','arm64')) {
        $runtimePath=Join-Path 'outputs' $runtimeFolder
        if (Test-Path $runtimePath) { Copy-Item -LiteralPath $runtimePath -Destination $stage -Recurse }
    }
    Copy-Item -LiteralPath 'outputs/README.md','samples/navigation.pdf','samples/continuous.pdf','samples/demo.pdf','samples/annotated-demo.pdf','docs/shortcuts.md','docs/M4-validation.md','docs/M4.1-validation.md','docs/M5-validation.md','docs/M6-validation.md','docs/data-storage.md','docs/THIRD-PARTY-NOTICES.md' -Destination $stage
    Copy-Item -LiteralPath 'docs/licenses' -Destination (Join-Path $stage 'licenses') -Recurse
    Copy-Item -LiteralPath 'docs/HeroUI-validation.md' -Destination $stage
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath 'outputs/PDFReader-HeroUI.zip' -Force
    Write-Output ('Packaged ' + (Join-Path $PSScriptRoot 'outputs/PDFReader-HeroUI.zip'))
} finally { Pop-Location }
