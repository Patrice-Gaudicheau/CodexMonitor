param([switch]$Live, [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
$testArgs = @((Join-Path $projectRoot 'artifacts\verification\icons'))
if ($Live) { $testArgs += '--live' }
[string[]]$restoreArgs = @()
if ($NoRestore) { $restoreArgs += '--no-restore' }
& dotnet run --project (Join-Path $projectRoot 'tests\CodexMonitor.Tests\CodexMonitor.Tests.csproj') -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false @restoreArgs -- @testArgs
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
