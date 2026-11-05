param([switch]$NoInstall)
$ErrorActionPreference='Stop'
$uiRoot=Join-Path $PSScriptRoot 'ui'
$oldNodeOptions=$env:NODE_OPTIONS
try {
    $env:NODE_OPTIONS=($oldNodeOptions+' --preserve-symlinks --preserve-symlinks-main').Trim()
    Push-Location $uiRoot
    if(-not $NoInstall -or -not (Test-Path 'node_modules/@heroui/react/package.json')) {
        & npm.cmd ci --ignore-scripts --no-fund --no-audit --cache (Join-Path $PSScriptRoot 'work/npm-cache')
        if($LASTEXITCODE -ne 0){throw 'HeroUI dependency install failed'}
    }
    & node node_modules/typescript/bin/tsc --project tsconfig.json --noEmit
    if($LASTEXITCODE -ne 0){throw 'HeroUI TypeScript check failed'}
    & node node_modules/vite/bin/vite.js build --configLoader native
    if($LASTEXITCODE -ne 0){throw 'HeroUI build failed'}
} finally { Pop-Location; $env:NODE_OPTIONS=$oldNodeOptions }
