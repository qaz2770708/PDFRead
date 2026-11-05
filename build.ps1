param(
    [string]$Configuration = 'Release',
    [switch]$NoRestore,
    [string]$BuildOutputPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $repoRoot 'PDFRead.csproj'
$outputPath = Join-Path $repoRoot 'outputs\PDFReader.exe'
if ($BuildOutputPath -and -not [IO.Path]::IsPathRooted($BuildOutputPath)) { $BuildOutputPath = Join-Path $repoRoot $BuildOutputPath }

& (Join-Path $repoRoot 'build-ui.ps1') -NoInstall:$NoRestore

$buildArgs = @('build', $projectPath, '--configuration', $Configuration)
if ($NoRestore) { $buildArgs += '--no-restore' }
if ($BuildOutputPath) { $buildArgs += @('--output', $BuildOutputPath) }
dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$builtExe = Join-Path $repoRoot "bin\$Configuration\net48\PDFReader.exe"
if ($BuildOutputPath) { $builtExe = Join-Path $BuildOutputPath 'PDFReader.exe' }
if (-not (Test-Path $builtExe)) {
    throw "Build output not found: $builtExe"
}
$outputDirectory = Split-Path -Parent $outputPath
$runtimeDirectory = Split-Path -Parent $builtExe
$runtimeNames = @('PDFReader.exe', 'PDFReader.exe.config') + @(Get-ChildItem -LiteralPath $runtimeDirectory -Filter '*.dll' -File | Select-Object -ExpandProperty Name)
foreach ($fileName in $runtimeNames) {
    $source = Join-Path (Split-Path -Parent $builtExe) $fileName
    if (-not (Test-Path $source)) {
        throw "Required runtime file not found: $source"
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $outputDirectory $fileName) -Force
}
$webSource=Join-Path $repoRoot 'ui/dist'
if(-not (Test-Path (Join-Path $webSource 'index.html'))){throw 'HeroUI web assets were not included in the build'}
$webTarget=Join-Path $outputDirectory 'web'
New-Item -ItemType Directory -Path $webTarget -Force | Out-Null
Get-ChildItem -LiteralPath $webSource | Copy-Item -Destination $webTarget -Recurse -Force
foreach($runtimeFolderName in @('runtimes','x86','x64','arm64')) {
    $runtimeFolder=Join-Path $runtimeDirectory $runtimeFolderName
    if(Test-Path $runtimeFolder){Copy-Item -LiteralPath $runtimeFolder -Destination $outputDirectory -Recurse -Force}
}
Write-Host "Built release files in $outputDirectory"
