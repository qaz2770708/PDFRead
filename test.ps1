param([switch]$NoRestore, [switch]$ModernUI)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if ($ModernUI) {
        $uiArguments = @('build', 'tests/ModernUiAcceptance.csproj', '-c', 'Release')
        if ($NoRestore) { $uiArguments += '--no-restore' }
        dotnet @uiArguments
        if ($LASTEXITCODE -ne 0) { throw 'Modern UI test build failed' }
        & ./tests/bin/Release/net48/ModernUiAcceptance.exe samples/navigation.pdf
        if ($LASTEXITCODE -ne 0) { throw 'Modern UI acceptance failed' }
    }
    foreach ($suite in @(@('Acceptance', 'samples/navigation.pdf'), @('StorageAcceptance', 'samples/continuous.pdf'), @('UiInteractionAcceptance', 'samples/navigation.pdf'), @('ProgressAcceptance', 'samples/navigation.pdf'), @('AnnotationAcceptance', 'samples/navigation.pdf'), @('InlineEditorAcceptance', 'samples/navigation.pdf'), @('PersistenceAcceptance', 'samples/navigation.pdf'), @('PersistenceUiAcceptance', 'samples/navigation.pdf'), @('BookmarkAcceptance', 'samples/navigation.pdf'), @('PdfExportAcceptance', 'samples/navigation.pdf'), @('PdfExportUiAcceptance', 'samples/navigation.pdf'))) {
        $arguments = @('build', ('tests/' + $suite[0] + '.csproj'), '-c', 'Release')
        if ($NoRestore) { $arguments += '--no-restore' }
        dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw ('Test build failed: ' + $suite[0]) }
        & ('./tests/bin/Release/net48/' + $suite[0] + '.exe') $suite[1]
        if ($LASTEXITCODE -ne 0) { throw ('Acceptance failed: ' + $suite[0]) }
    }
} finally { Pop-Location }
