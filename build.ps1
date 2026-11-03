param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $repoRoot 'PDFRead.csproj'
$outputPath = Join-Path $repoRoot 'outputs\PDFReader.exe'

dotnet build $projectPath --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$builtExe = Join-Path $repoRoot "bin\$Configuration\net48\PDFReader.exe"
if (-not (Test-Path $builtExe)) {
    throw "Build output not found: $builtExe"
}
Copy-Item -LiteralPath $builtExe -Destination $outputPath -Force
Write-Host "Built $outputPath"
