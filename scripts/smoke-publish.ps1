# Exercises the actual self-contained entry point using Windows UI Automation.
param([string]$Application = 'artifacts/win-x64/Dino2.Desktop.exe')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$repoRoot = Split-Path -Parent $PSScriptRoot
$applicationPath = if ([System.IO.Path]::IsPathRooted($Application)) { $Application }
    else { [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Application)) }
$applicationProcess = Start-Process -FilePath $applicationPath -PassThru -WindowStyle Hidden
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 200
        $applicationProcess.Refresh()
        if ($applicationProcess.HasExited) { throw 'Published app exited before opening a window' }
    } while ($applicationProcess.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($applicationProcess.MainWindowHandle -eq 0) { throw 'Published window did not appear' }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($applicationProcess.MainWindowHandle)
    function Find-Control([string]$automationId) {
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -eq $element) { throw "Missing control: $automationId" }
        return $element
    }
    $calculate = Find-Control 'CalculateButton'
    $invoke = $calculate.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $summary = (Find-Control 'Summary').Current.Name
    } while (!$summary.Contains('402.12') -and [DateTime]::UtcNow -lt $deadline)
    if (!$summary.Contains('402.12')) { throw "Published 4T calculation failed: $summary" }
    $modelStatus=(Find-Control 'Status').Current.Name
    if(!$modelStatus.Contains('four-stroke-isooctane-equilibrium-v3')) { throw "Published default thermochemical model missing: $modelStatus" }
    Write-Output "PASS published 4T app opens and calculates: $summary"
    $reference = Find-Control 'OttoReferenceButton'
    $reference.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $applicationProcess.Id),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Dino2'))
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $ottoWindow = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
    } while ($null -eq $ottoWindow -and [DateTime]::UtcNow -lt $deadline)
    if ($null -eq $ottoWindow) { throw 'Independent Otto reference did not open' }
    $root = $ottoWindow
    (Find-Control 'CalculateButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 200
    $ottoSummary = (Find-Control 'Summary').Current.Name
    if (!$ottoSummary.Contains('249.875')) { throw "Published Otto reference failed: $ottoSummary" }
    Write-Output "PASS independent Otto reference: $ottoSummary"
    $ottoWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    $runtimePath = ($applicationProcess.Modules | Where-Object ModuleName -eq 'coreclr.dll' | Select-Object -First 1).FileName
    if (!$runtimePath.StartsWith((Split-Path -Parent $applicationPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Runtime was not loaded from the distribution: $runtimePath"
    }
    Write-Output "PASS bundled runtime: $runtimePath"
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($applicationProcess.MainWindowHandle)
    $models = Find-Control 'PhysicsModel'
    $dropdown = $models.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $dropdown.Expand()
    $ductModel = $models.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
            $_.Current.Name.Contains('MUSCL') -and $_.Current.Name.EndsWith('Wiebe prescrita') } | Select-Object -First 1
    if ($null -eq $ductModel) { throw 'Published v4 model not found' }
    $ductModel.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $dropdown.Collapse()
    (Find-Control 'CalculateButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $deadline = [DateTime]::UtcNow.AddSeconds(55)
    do {
        Start-Sleep -Milliseconds 250
        $status = (Find-Control 'Status').Current.Name
    } while (!$status.Contains('four-stroke-isooctane-ducts-v4-muscl') -and !$status.StartsWith('No se completó') -and [DateTime]::UtcNow -lt $deadline)
    if (!$status.Contains('four-stroke-isooctane-ducts-v4-muscl')) { throw "Published v4 calculation failed: $status" }
    Write-Output "PASS published 1D calculation: $status"
}
finally {
    $applicationProcess.Refresh()
    if (!$applicationProcess.HasExited) {
        $null = $applicationProcess.CloseMainWindow()
        if (!$applicationProcess.WaitForExit(5000)) { $applicationProcess.Kill() }
    }
    $applicationProcess.Dispose()
}
