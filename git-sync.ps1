$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path $PSScriptRoot).Path
$gitDir = Join-Path $repoRoot 'work\pdfread.git'
$gitArguments = @($args)

if (-not (Test-Path (Join-Path $gitDir 'HEAD'))) {
    New-Item -ItemType Directory -Force -Path (Split-Path $gitDir -Parent) | Out-Null
    git init --bare --initial-branch=main $gitDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

git --git-dir=$gitDir --work-tree=$repoRoot -c "safe.directory=$repoRoot" @gitArguments
exit $LASTEXITCODE
