# UI Automation of the real tray submenu; use a --verify-live instance to avoid saving test choices.
param([Parameter(Mandatory)][int]$MonitorProcessId, [Parameter(Mandatory)][string]$OutputDirectory, [Parameter(Mandatory)][string]$SessionReport, [string]$WindowsUICulture = 'fr-FR')
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$runtimeRoot = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'
$runtime = Get-ChildItem -LiteralPath $runtimeRoot -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
foreach ($assembly in @('WindowsBase.dll','UIAutomationTypes.dll','UIAutomationClient.dll','System.Drawing.Common.dll')) { Add-Type -Path (Join-Path $runtime.FullName $assembly) }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CodexMonitorLanguageUi {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string caption);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}
'@
$session = Get-Content -LiteralPath $SessionReport -Raw | ConvertFrom-Json
if ($session.processId -ne $MonitorProcessId) { throw 'Diagnostic session process mismatch' }
$hostWindow = [IntPtr][int64]$session.trayHandle
$owner = [uint32]0
[void][CodexMonitorLanguageUi]::GetWindowThreadProcessId($hostWindow,[ref]$owner)
if ($hostWindow -eq [IntPtr]::Zero -or $owner -ne $MonitorProcessId) { throw 'The target monitor does not own the tray window' }
$root = [System.Windows.Automation.AutomationElement]::RootElement
$scope = [System.Windows.Automation.TreeScope]::Descendants
$nameProperty = [System.Windows.Automation.AutomationElement]::NameProperty
$pidCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$MonitorProcessId)
function Find-Control([string]$name) {
    $condition = [System.Windows.Automation.AndCondition]::new($pidCondition,[System.Windows.Automation.PropertyCondition]::new($nameProperty,$name))
    return $root.FindFirst($scope,$condition)
}
# Reproduce only our icon's documented left-click callback; no global mouse input.
[void][CodexMonitorLanguageUi]::PostMessage($hostWindow,0x8001,[IntPtr]::Zero,[IntPtr]0x400)
Start-Sleep -Milliseconds 300
$originalTitle = if ([Globalization.CultureInfo]::GetCultureInfo($WindowsUICulture).TwoLetterISOLanguageName -eq 'fr') { 'Quotas Codex' } else { 'Codex quotas' }
$panel = Find-Control $originalTitle
if (-not $panel) { throw 'Details window did not open' }
$originalHandle = $panel.Current.NativeWindowHandle
$observations = @()
foreach ($choice in @('English','Français','Automatic')) {
    [void][CodexMonitorLanguageUi]::PostMessage($hostWindow,0x8001,[IntPtr]::Zero,[IntPtr]0x7B)
    Start-Sleep -Milliseconds 150
    $language = Find-Control 'Langue / Language'
    if (-not $language) { throw 'Language submenu missing' }
    $language.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 150
    $label = if ($choice -eq 'Automatic') { 'Automatique (Windows)' } else { $choice }
    $item = Find-Control $label
    if (-not $item) { throw "Language option missing: $label" }
    $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 200
    $french = $choice -eq 'Français' -or ($choice -eq 'Automatic' -and $originalTitle -eq 'Quotas Codex')
    $title = if ($french) { 'Quotas Codex' } else { 'Codex quotas' }
    $panel = Find-Control $title
    if (-not $panel -or $panel.Current.NativeWindowHandle -ne $originalHandle) { throw 'Details did not switch in place' }
    $group = Find-Control $(if ($french) { 'Détails du quota Codex' } else { 'Codex quota details' })
    $button = Find-Control $(if ($french) { 'Actualiser les quotas' } else { 'Refresh quotas' })
    if (-not $group -or -not $button) { throw 'Accessibility labels retained the old language' }
    $description = $group.Current.HelpText
    if (-not $description.Contains($(if ($french) { 'Dernière lecture réussie' } else { 'Last successful reading' }))) { throw 'Accessible description retained the old language' }
    $rect = $panel.Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$rect.Width,[int]$rect.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen([int]$rect.X,[int]$rect.Y,0,0,$bitmap.Size); $bitmap.Save((Join-Path $output "switch-$choice.png")) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    $observations += @{ choice=$choice; title=$title; handle=$originalHandle; description=$description; refreshName=$button.Current.Name }
}
$panel.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
$observations | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'live-switch-ui.json')
'PASS real language submenu: English, Français, Automatic; same open window, translated accessibility and refresh labels'
