# Verify the real installed details window through Windows UI Automation.
# CodexMonitor must already be running; only its own icon and window are inspected.
param([string]$OutputDirectory, [switch]$RequireNoScrollbar, [string]$UICulture = [Globalization.CultureInfo]::CurrentUICulture.Name)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$french = [Globalization.CultureInfo]::GetCultureInfo($UICulture).TwoLetterISOLanguageName -eq 'fr'
$title = if ($french) { 'Quotas Codex' } else { 'Codex quotas' }
$groupName = if ($french) { 'Détails du quota Codex' } else { 'Codex quota details' }
$refreshName = if ($french) { 'Actualiser les quotas' } else { 'Refresh quotas' }
$remainingText = if ($french) { 'restants' } else { 'remaining' }
$resetText = if ($french) { 'Réinitialisation' } else { 'Resets' }
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'artifacts\verification\details-redesign\installed' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$runtimeRoot = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'
$runtime = Get-ChildItem -LiteralPath $runtimeRoot -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
foreach ($assembly in @('WindowsBase.dll','UIAutomationTypes.dll','UIAutomationClient.dll','System.Drawing.Common.dll')) {
    Add-Type -Path (Join-Path $runtime.FullName $assembly)
}
$root = [System.Windows.Automation.AutomationElement]::RootElement
$scope = [System.Windows.Automation.TreeScope]
$classProperty = [System.Windows.Automation.AutomationElement]::ClassNameProperty
$nameProperty = [System.Windows.Automation.AutomationElement]::NameProperty
$classCondition = [System.Windows.Automation.PropertyCondition]::new($classProperty,'Shell_TrayWnd')
$taskbar = $root.FindFirst($scope::Children,$classCondition)
$icon = $taskbar.FindAll($scope::Descendants,[System.Windows.Automation.Condition]::TrueCondition) |
    Where-Object { $_.Current.Name.StartsWith('CodexMonitor') -and $_.Current.ClassName -eq 'SystemTray.NormalButton' } | Select-Object -First 1
if (-not $icon) {
    $buttonCondition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new($classProperty,'SystemTray.NormalButton'),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'SystemTrayIcon'))
    $taskbar.FindFirst($scope::Descendants,$buttonCondition).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
    $overflowClass = [System.Windows.Automation.OrCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new($classProperty,'TopLevelWindowForOverflowXamlIsland'),
        [System.Windows.Automation.PropertyCondition]::new($classProperty,'NotifyIconOverflowWindow'))
    $overflow = $root.FindFirst($scope::Children,$overflowClass)
    $icon = $overflow.FindAll($scope::Descendants,[System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.Name.StartsWith('CodexMonitor') } | Select-Object -First 1
}
if (-not $icon) { throw 'CodexMonitor tray icon not found' }
$icon.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 400
$panel = $root.FindFirst($scope::Children,[System.Windows.Automation.PropertyCondition]::new($nameProperty,$title))
if (-not $panel) { throw 'Details did not open through the tray icon' }
try {
    $group = $panel.FindFirst($scope::Descendants,[System.Windows.Automation.PropertyCondition]::new($nameProperty,$groupName))
    $before = $group.Current.HelpText
    if (-not $before) { throw 'Accessible quota description missing' }
    $button = $panel.FindFirst($scope::Descendants,[System.Windows.Automation.PropertyCondition]::new($nameProperty,$refreshName))
    if (-not $button.Current.IsEnabled) { throw 'Refresh was unexpectedly disabled' }
    Start-Sleep -Milliseconds 1100
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 3
    $after = $group.Current.HelpText
    if ($after -eq $before) { throw 'No updated successful-read timestamp after the real refresh button' }
    if (-not $after.Contains($remainingText) -or -not $after.Contains($resetText)) { throw 'Quota details missing' }
    $layout = $null
    if ($RequireNoScrollbar) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CodexMonitorLayoutProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")] public static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window, out Rect rect);
}
'@
        $viewport = [CodexMonitorLayoutProbe]::GetParent([IntPtr]$group.Current.NativeWindowHandle)
        $scrollbar = ([CodexMonitorLayoutProbe]::GetWindowLong($viewport,-16) -band 0x00200000) -ne 0
        $client = [CodexMonitorLayoutProbe+Rect]::new()
        [void][CodexMonitorLayoutProbe]::GetClientRect($viewport,[ref]$client)
        $contentHeight = $group.Current.BoundingRectangle.Height
        $viewportHeight = $client.Bottom - $client.Top
        if ($scrollbar) { throw 'Unnecessary vertical scrollbar remains in the installed window' }
        if ($contentHeight -gt $viewportHeight) { throw 'Content is clipped despite absent scrollbar' }
        $layout = @{ verticalScrollbar = $scrollbar; contentHeight = $contentHeight; viewportHeight = $viewportHeight }
    }
    $rect = $panel.Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$rect.Width,[int]$rect.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$rect.X,[int]$rect.Y,0,0,$bitmap.Size)
        $bitmap.Save((Join-Path $output 'details-window.png'))
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    @{ openedThroughTray = $true; refreshedThroughButton = $true; description = $after; rectangle = $rect.ToString(); layout = $layout } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'ui-interaction.json')
    'PASS installed details window, tray click, native refresh button, updated quota timestamp'
} finally { $panel.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
