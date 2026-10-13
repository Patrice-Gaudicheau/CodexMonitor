param([switch]$Arm64, [switch]$NoRestore, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
$rid = if ($Arm64) { 'win-arm64' } else { 'win-x64' }
$publishedDirectory = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot "dist\$rid" }
[string[]]$restoreArgs = @()
if ($NoRestore) { $restoreArgs += '--no-restore' }
& dotnet publish (Join-Path $projectRoot 'src\CodexMonitor\CodexMonitor.csproj') -c Release -r $rid --self-contained true -o $publishedDirectory -m:1 -p:UseSharedCompilation=false @restoreArgs
if ($LASTEXITCODE -ne 0) { throw 'Publication failed' }
foreach ($debugFile in @('CodexMonitor.Core.pdb', 'CodexMonitor.pdb')) {
    $debugPath = Join-Path $publishedDirectory $debugFile
    if (Test-Path -LiteralPath $debugPath) { Remove-Item -LiteralPath $debugPath }
}
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $publishedDirectory 'CodexMonitor.exe')
if (@(Get-ChildItem -LiteralPath $publishedDirectory -Recurse -File).Count -ne 1) {
    throw 'Single-file distribution must contain only CodexMonitor.exe (no satellite resources).'
}
