# Optional visual integration check. CodexMonitor must already be running in the current interactive Windows session.
param([string]$UICulture = [Globalization.CultureInfo]::CurrentUICulture.Name)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $projectRoot 'artifacts\verification\interactive'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$runtimeRoot = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'
$runtime = Get-ChildItem -LiteralPath $runtimeRoot -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $runtime) { throw 'This development test requires the Windows Desktop runtime installed with the SDK.' }
foreach ($assembly in @('WindowsBase.dll', 'UIAutomationTypes.dll', 'UIAutomationClient.dll', 'System.Drawing.Common.dll')) {
    Add-Type -Path (Join-Path $runtime.FullName $assembly)
}
$root = [System.Windows.Automation.AutomationElement]::RootElement
$classProperty = [System.Windows.Automation.AutomationElement]::ClassNameProperty
$scope = [System.Windows.Automation.TreeScope]
$taskbarClass = [System.Windows.Automation.PropertyCondition]::new($classProperty, 'Shell_TrayWnd')
$taskbar = $root.FindFirst($scope::Children, $taskbarClass)
$buttonCondition = [System.Windows.Automation.AndCondition]::new(
    [System.Windows.Automation.PropertyCondition]::new($classProperty, 'SystemTray.NormalButton'),
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SystemTrayIcon'))
$button = $taskbar.FindFirst($scope::Descendants, $buttonCondition)
$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 300
$overflowClass = [System.Windows.Automation.OrCondition]::new(
    [System.Windows.Automation.PropertyCondition]::new($classProperty, 'TopLevelWindowForOverflowXamlIsland'),
    [System.Windows.Automation.PropertyCondition]::new($classProperty, 'NotifyIconOverflowWindow'))
$overflow = $root.FindFirst($scope::Children, $overflowClass)
if (-not $overflow) { throw 'Overflow panel not found. This check targets an icon Windows has placed in the overflow panel.' }
$icon = $overflow.FindAll($scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
    Where-Object { $_.Current.Name.StartsWith('CodexMonitor') } | Select-Object -First 1
if (-not $icon) { throw 'CodexMonitor icon not found in overflow; check whether Windows already shows it directly in the taskbar.' }
$rect = $icon.Current.BoundingRectangle
$bitmap = [Drawing.Bitmap]::new([int]$rect.Width, [int]$rect.Height)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bitmap.Size)
    $bitmap.Save((Join-Path $output 'actual-tray-badge.png'))
} finally { $graphics.Dispose(); $bitmap.Dispose() }
$icon.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 400
$title = if ([Globalization.CultureInfo]::GetCultureInfo($UICulture).TwoLetterISOLanguageName -eq 'fr') { 'Quotas Codex' } else { 'Codex quotas' }
$panelName = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $title)
$panel = $root.FindFirst($scope::Children, $panelName)
if (-not $panel) { throw 'Left click did not open quota details' }
$panel.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
@{ iconFound = $true; detailsOpenedByShell = $true; rectangle = $rect.ToString() } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'shell-interaction.json')
'PASS actual shell badge and left-click details'
