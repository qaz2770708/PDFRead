param([switch]$NoRestore)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    foreach ($suite in @(@('Acceptance', 'samples/navigation.pdf'), @('StorageAcceptance', 'samples/continuous.pdf'))) {
        $arguments = @('build', ('tests/' + $suite[0] + '.csproj'), '-c', 'Release')
        if ($NoRestore) { $arguments += '--no-restore' }
        dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw ('Test build failed: ' + $suite[0]) }
        & ('./tests/bin/Release/net48/' + $suite[0] + '.exe') $suite[1]
        if ($LASTEXITCODE -ne 0) { throw ('Acceptance failed: ' + $suite[0]) }
    }
} finally { Pop-Location }
