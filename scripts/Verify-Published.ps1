param([string]$Executable, [string]$OutputDirectory, [string]$UICulture, [switch]$SwitchLanguage)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Executable) { $Executable = Join-Path $projectRoot 'dist\win-x64\CodexMonitor.exe' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts\verification\published' }
$Executable = [IO.Path]::GetFullPath($Executable)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$previousRoot = $env:DOTNET_ROOT
$previousLookup = $env:DOTNET_MULTILEVEL_LOOKUP
try {
    # Point runtime discovery at a missing directory: the bundled runtime must suffice.
    $env:DOTNET_ROOT = Join-Path $OutputDirectory 'no-installed-runtime'
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $arguments = @('--verify-live', ('"' + $OutputDirectory + '"'))
    if ($UICulture) { $arguments += $UICulture }
    if ($SwitchLanguage) {
        if (-not $UICulture) { $arguments += [Globalization.CultureInfo]::CurrentUICulture.Name }
        $arguments += '--switch-language'
    }
    $monitor = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 4
    if ($monitor.HasExited) { throw 'Verification monitor exited too early. Close any existing CodexMonitor instance.' }
    $duplicate = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($duplicate.ExitCode -ne 10) { throw 'Single-instance guard failed' }
    if (-not $monitor.WaitForExit(110000)) { $monitor.Kill(); throw 'Published verification exceeded deadline' }
    if ($monitor.ExitCode -ne 0) { throw 'Published application failed' }
    $report = Get-Content -LiteralPath (Join-Path $OutputDirectory 'verification.json') -Raw | ConvertFrom-Json
    if (-not $report.passed) { throw 'Published functional verification failed. Inspect verification.json.' }
    if ($SwitchLanguage -and -not $report.languageSwitchPassed) { throw 'Live language switch verification failed' }
    if ($UICulture) {
        $expectedCulture = if ([Globalization.CultureInfo]::GetCultureInfo($UICulture).TwoLetterISOLanguageName -eq 'fr') { 'fr-FR' } else { 'en-US' }
        if ($report.uiCulture -ne $expectedCulture) { throw 'Published UI culture mismatch' }
    }
    foreach ($observation in $report.observations) {
        if ($observation.action -ne 'liveRead') { continue }
        $ownedServer = Get-Process -Id $observation.processId -ErrorAction SilentlyContinue
        if ($ownedServer) { throw "App Server $($observation.processId) remains after exit" }
    }
    $bytes = [IO.File]::ReadAllBytes($Executable)
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
    $subsystem = [BitConverter]::ToUInt16($bytes, $peOffset + 24 + 68)
    if ($subsystem -ne 2) { throw 'Executable is not Windows GUI subsystem' }
    "PASS: published executable, real tray, live quota, automatic/manual refresh, reconnection, TaskbarCreated recovery, single instance, no main window, no orphan server; PE subsystem $subsystem."
    $report | ConvertTo-Json -Depth 10
} finally {
    $env:DOTNET_ROOT = $previousRoot
    $env:DOTNET_MULTILEVEL_LOOKUP = $previousLookup
}
