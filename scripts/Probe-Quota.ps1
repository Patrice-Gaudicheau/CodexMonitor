param([string]$CodexPath)
$ErrorActionPreference = 'Stop'
if (-not $CodexPath) { $CodexPath = $env:CODEX_MONITOR_CODEX_PATH }
if (-not $CodexPath) {
    $CodexPath = Join-Path $env:LOCALAPPDATA 'Programs\OpenAI\Codex\bin\codex.exe'
    if (-not (Test-Path -LiteralPath $CodexPath -PathType Leaf)) {
        $CodexPath = (Get-Command codex.exe -ErrorAction Stop).Source
    }
}
if (-not [IO.Path]::IsPathFullyQualified($CodexPath) -or -not (Test-Path -LiteralPath $CodexPath -PathType Leaf)) {
    throw 'Provide the absolute path of an installed native Codex executable.'
}
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $CodexPath
$start.Arguments = 'app-server --listen stdio://'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
try {
    [void]$process.Start()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.StandardInput.WriteLine('{"id":1,"method":"initialize","params":{"clientInfo":{"name":"codex_monitor","title":"CodexMonitor","version":"1.0.0"}}}')
    $process.StandardInput.Flush()
    $initialized = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $deadline) {
        $lineTask = $process.StandardOutput.ReadLineAsync()
        if (-not $lineTask.Wait(30000)) { throw 'App-server timeout' }
        $line = $lineTask.Result
        if ($null -eq $line) { throw 'App-server exited before quota response' }
        $message = $line | ConvertFrom-Json
        if ($message.id -eq 1) {
            if ($message.error) { throw "Initialization rejected (code $($message.error.code))" }
            $initialized = $true
            $process.StandardInput.WriteLine('{"method":"initialized","params":{}}')
            $process.StandardInput.WriteLine('{"id":2,"method":"account/rateLimits/read","params":{}}')
            $process.StandardInput.Flush()
        }
        if ($message.id -eq 2) {
            if ($message.error) { throw "Quota read rejected (code $($message.error.code)): $($message.error.message)" }
            $message.result | ConvertTo-Json -Depth 15
            exit 0
        }
    }
    throw 'Quota deadline exceeded'
} finally {
    if ($process.Id -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    $process.Dispose()
}
