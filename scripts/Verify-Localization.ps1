# Verify only the standalone EXE, in isolation from build output and satellite DLLs.
param([string]$Executable, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Executable) { $Executable = Join-Path $projectRoot 'dist\win-x64\CodexMonitor.exe' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts\verification\single-file-localization' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$isolated = Join-Path $OutputDirectory ('isolated-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $isolated -Force | Out-Null
$copy = Join-Path $isolated 'CodexMonitor.exe'
Copy-Item -LiteralPath $Executable -Destination $copy
if (@(Get-ChildItem -LiteralPath $isolated -File).Count -ne 1) { throw 'Isolation directory must contain only the executable' }
$previousRoot = $env:DOTNET_ROOT
$previousX64Root = $env:DOTNET_ROOT_X64
$previousLookup = $env:DOTNET_MULTILEVEL_LOOKUP
try {
    $env:DOTNET_ROOT = Join-Path $isolated 'no-installed-runtime'
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    foreach ($culture in @('fr-FR', 'en-US', 'de-DE')) {
        $output = Join-Path $OutputDirectory $culture
        # The culture override is confined to the child process, without OS changes.
        $process = Start-Process -FilePath $copy -ArgumentList @('--verify-localization', $culture, ('"' + $output + '"')) -WorkingDirectory $isolated -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "Localization verification timeout: $culture" }
        if ($process.ExitCode -ne 0) { throw "Localization verification failed: $culture. Inspect $output\failure.txt" }
        $report = Get-Content -LiteralPath (Join-Path $output 'localization.json') -Raw | ConvertFrom-Json
        $expected = if ($culture -eq 'fr-FR') { 'fr-FR' } else { 'en-US' }
        if ($report.culture -ne $expected -or $report.version -ne '1.1.0') { throw 'Unexpected culture/version' }
        if (@($report.embeddedResources).Count -ne 2 -or $report.resourceCount -lt 80) { throw 'Embedded localization resources missing' }
        if (@($report.observations).Count -ne 66) { throw 'Incomplete native rendering matrix' }
        if (@($report.observations | Where-Object { $_.overflows.Count -gt 0 }).Count -ne 0) { throw 'Text clipping detected' }
        "PASS single-file $culture -> ${expected}: $($report.resourceCount) resources, $($report.observations.Count) native renderings"
    }
    if (@(Get-ChildItem -LiteralPath $isolated -File).Count -ne 1) { throw 'Unexpected external runtime resources' }
    Get-FileHash -Algorithm SHA256 -LiteralPath $copy
} finally {
    $env:DOTNET_ROOT = $previousRoot
    $env:DOTNET_ROOT_X64 = $previousX64Root
    $env:DOTNET_MULTILEVEL_LOOKUP = $previousLookup
}
