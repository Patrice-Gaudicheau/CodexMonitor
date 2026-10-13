param([switch]$Arm64)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
$rid = if ($Arm64) { 'win-arm64' } else { 'win-x64' }
& dotnet publish (Join-Path $projectRoot 'src\CodexMonitor\CodexMonitor.csproj') -c Release -r $rid --self-contained true -o (Join-Path $projectRoot "dist\$rid")
if ($LASTEXITCODE -ne 0) { throw 'Publication failed' }
foreach ($debugFile in @('CodexMonitor.Core.pdb', 'CodexMonitor.pdb')) {
    $debugPath = Join-Path $projectRoot "dist\$rid\$debugFile"
    if (Test-Path -LiteralPath $debugPath) { Remove-Item -LiteralPath $debugPath }
}
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $projectRoot "dist\$rid\CodexMonitor.exe")
