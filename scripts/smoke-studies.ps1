# Exercises the normal study workflow through the portable WPF executable.
param([string]$Application = 'artifacts/stage5-win-x64/Dino2.Desktop.exe')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

. (Join-Path $PSScriptRoot "native-file-dialog.ps1")

$repo = Split-Path -Parent $PSScriptRoot
$applicationPath = if ([IO.Path]::IsPathRooted($Application)) { $Application } else { Join-Path $repo $Application }
$output = Join-Path $repo ('artifacts/stage5-native/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$process = Start-Process -FilePath $applicationPath -PassThru -WindowStyle Hidden

function Wait-For([scriptblock]$condition, [string]$reason, [int]$seconds = 20) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        $value = & $condition
        if ($value) { return $value }
        Start-Sleep -Milliseconds 120
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out: $reason"
}
function By-Id($element, [string]$id) {
    $property = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $property)
}
function Control([string]$id) {
    $control = By-Id $root $id
    if ($null -eq $control) { throw "Missing control $id" }
    return $control
}
function Invoke-Control([string]$id) {
    (Control $id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Set-Value($element, [string]$value) {
    $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}
function Wait-Message([string]$id, [string]$prefix, [int]$seconds = 20) {
    return Wait-For { $message = (Control $id).Current.Name; if ($message.StartsWith($prefix)) { $message } } $prefix $seconds
}
function Select-Variant {
    $type = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::DataItem)
    $rows = (Control 'StudyRows').FindAll([System.Windows.Automation.TreeScope]::Descendants, $type)
    if ($rows.Count -lt 2) { throw 'No ranked variant appeared.' }
    $rows[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Choose-File([string]$button, [string]$path) {
    Invoke-Control $button
    $dialog = Wait-For {
        $process.Refresh()
        foreach ($thread in $process.Threads) {
            foreach ($dialogHandle in [DialogWindows]::Find([uint32]$thread.Id)) {
                $candidate = [System.Windows.Automation.AutomationElement]::FromHandle($dialogHandle)
                if ($null -ne $candidate -and !( $candidate.Current.IsOffscreen)) { return $candidate }
            }
        }
    } 'File dialog'
    $nativeDialog = [IntPtr]$dialog.Current.NativeWindowHandle
    $editHandle = Wait-For {
        $edit = [DialogWindows]::Child($nativeDialog, 'Edit', 1001)
        if ($edit -ne [IntPtr]::Zero) { $edit }
    } "Filename entry in '$($dialog.Current.Name)'"
    [DialogWindows]::SetFilename($editHandle, $path)
    if ([DialogWindows]::Text($editHandle) -ne $path) { throw 'Filename input did not reach the dialog.' }
    $confirm = Wait-For {
        $button = [DialogWindows]::Child($nativeDialog, 'Button', 1)
        if ($button -ne [IntPtr]::Zero) { $button }
    } 'File acceptance button'
    [DialogWindows]::Confirm($nativeDialog, $confirm)
}

try {
    $handle = Wait-For {
        $process.Refresh()
        if ($process.HasExited) { throw 'Portable exited before opening.' }
        if ($process.MainWindowHandle -ne 0) { $process.MainWindowHandle }
    } 'Main window'
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    (Control 'StudyTab').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke-Control 'StudyUseCurrent'
    Set-Value (Control 'RpmStart') '2500'
    Set-Value (Control 'RpmEnd') '3500'
    Set-Value (Control 'RpmStep') '500'
    Invoke-Control 'StudyStart'
    $message = Wait-Message 'StudyMessage' 'Estudio completo'
    Write-Output "PASS portable study executes sampled curves: $message"
    Select-Variant
    Invoke-Control 'StudyCheckButton'
    $message = Wait-Message 'StudyMessage' 'Contraste finalizado'
    Write-Output "PASS portable numerical comparison: $message"

    $file = Join-Path $output 'portable.dino-study.json'
    Choose-File 'StudySave' $file
    try { $null = Wait-For { Test-Path -LiteralPath $file } 'Study saved' }
    catch {
        $dialogs = foreach ($thread in $process.Threads) {
            [DialogWindows]::Find([uint32]$thread.Id) | ForEach-Object { [DialogWindows]::Text($_) }
        }
        throw "Study save failed. Dialogs: $dialogs. Current message: $((Control 'StudyMessage').Current.Name)"
    }
    $saved = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    if ($saved.study.run.evaluations.Count -ne 4 -or $saved.study.checks.Count -ne 1) { throw 'Saved study is incomplete.' }
    Choose-File 'StudyOpen' $file
    $null = Wait-Message 'StudyMessage' 'Estudio recuperado'
    $csv = Join-Path $output 'portable.csv'
    Choose-File 'StudyExport' $csv
    $null = Wait-For { Test-Path -LiteralPath $csv } 'CSV exported'
    if (!(Select-String -LiteralPath $csv -Pattern '# resolution=' -Quiet)) { throw 'CSV missed numeric contrast.' }
    Write-Output 'PASS portable study save, reopen and CSV export'
    Select-Variant
    Invoke-Control 'StudyApply'
    $null = Wait-Message 'Status' 'Variante aplicada'
    Invoke-Control 'CalculateButton'
    $null = Wait-For {
        $status = (Control 'Status').Current.Name
        if ($status -match '^3(?:/3)? RPM' -and $status.Contains('equilibrium-v3')) { $status }
    } 'Applied B calculated'
    $runtime = ($process.Modules | Where-Object ModuleName -eq 'coreclr.dll' | Select-Object -First 1).FileName
    if (!$runtime.StartsWith((Split-Path -Parent $applicationPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected runtime location: $runtime"
    }
    Write-Output "PASS portable applies selected B and solves it; runtime: $runtime"
    Write-Output "Evidence: $output"
}
finally {
    $process.Refresh()
    if (!$process.HasExited) {
        $null = $process.CloseMainWindow()
        if (!$process.WaitForExit(5000)) { $process.Kill() }
    }
    $process.Dispose()
}
