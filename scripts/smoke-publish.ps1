# Exercises the actual self-contained entry point using Windows UI Automation.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$repoRoot = Split-Path -Parent $PSScriptRoot
$applicationPath = Join-Path $repoRoot 'artifacts/win-x64/Dino2.Desktop.exe'
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
    } while (!$summary.Contains('249.875') -and [DateTime]::UtcNow -lt $deadline)
    if (!$summary.Contains('249.875')) { throw "Published calculation failed: $summary" }
    $runtimePath = ($applicationProcess.Modules | Where-Object ModuleName -eq 'coreclr.dll' | Select-Object -First 1).FileName
    if (!$runtimePath.StartsWith((Split-Path -Parent $applicationPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Runtime was not loaded from the distribution: $runtimePath"
    }
    Write-Output "PASS published app opens and calculates: $summary"
    Write-Output "PASS bundled runtime: $runtimePath"
}
finally {
    $applicationProcess.Refresh()
    if (!$applicationProcess.HasExited) {
        $null = $applicationProcess.CloseMainWindow()
        if (!$applicationProcess.WaitForExit(5000)) { $applicationProcess.Kill() }
    }
    $applicationProcess.Dispose()
}
