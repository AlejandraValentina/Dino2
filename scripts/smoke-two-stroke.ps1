param([string]$Application = 'artifacts/stage6-win-x64/Dino2.Desktop.exe', [switch]$VerifyResolution, [switch]$VerifyDomain, [switch]$AuditFiles,
    [string]$TamperedProject)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'native-file-dialog.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$exe = if ([IO.Path]::IsPathRooted($Application)) { $Application } else { Join-Path $repo $Application }
$output = Join-Path $repo ('artifacts/stage6-native/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$process = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden
$descendants = [System.Windows.Automation.TreeScope]::Descendants
$elementType = [System.Windows.Automation.AutomationElement]

function Wait-For([scriptblock]$action, [string]$context, [int]$seconds = 22) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        $value = & $action
        if ($value) { return $value }
        Start-Sleep -Milliseconds 120
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out: $context"
}

function Find-Control($parent, [string]$id, [switch]$Label, $scope = $descendants) {
    $property = if ($Label) { $elementType::NameProperty } else { $elementType::AutomationIdProperty }
    $condition = [System.Windows.Automation.PropertyCondition]::new($property, $id)
    $parent.FindFirst($scope, $condition)
}

function Control([string]$id, [switch]$Label) {
    $control = Find-Control $root $id -Label:$Label
    if ($null -eq $control) { throw "Missing control $id" }
    return $control
}

function Click([string]$id, [switch]$Label) {
    $control = Wait-For {
        $candidate = Find-Control $root $id -Label:$Label
        if ($null -ne $candidate -and $candidate.Current.IsEnabled) { $candidate }
    } "enabled control $id"
    $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Select-Tab([string]$id) {
    (Control $id).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

function Edit([string]$label, [string]$value) {
    $nameMatch = [System.Windows.Automation.PropertyCondition]::new($elementType::NameProperty, $label)
    $editMatch = [System.Windows.Automation.PropertyCondition]::new(
        $elementType::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    $filter = [System.Windows.Automation.AndCondition]::new($nameMatch, $editMatch)
    $edit = $root.FindFirst($descendants, $filter)
    if ($null -eq $edit) { throw "Missing editor $label" }
    $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

function Status([string]$prefix, [int]$seconds = 22) {
    try {
        Wait-For {
            $message = (Control 'Status').Current.Name
            if ($message.StartsWith($prefix)) { return $message }
        } $prefix $seconds
    } catch {
        throw "$($_.Exception.Message) · final status: $((Control 'Status').Current.Name)"
    }
}

function File-Dialog([string]$control, [string]$path, [switch]$Label) {
    Click $control -Label:$Label
    $dialog = Wait-For {
        $process.Refresh()
        foreach ($thread in $process.Threads) {
            foreach ($candidate in [DialogWindows]::Find([uint32]$thread.Id)) {
                $edit = [DialogWindows]::Child($candidate, 'Edit', 1001)
                if ($edit -ne [IntPtr]::Zero) { return $candidate }
            }
        }
    } "file picker $control"
    $edit = [DialogWindows]::Child($dialog, 'Edit', 1001)
    [DialogWindows]::SetFilename($edit, $path)
    if ([DialogWindows]::Text($edit) -ne $path) { throw 'File name entry failed.' }
    $confirm = [DialogWindows]::Child($dialog, 'Button', 1)
    if ($confirm -eq [IntPtr]::Zero) { throw 'Dialog has no acceptance button.' }
    [DialogWindows]::Confirm($dialog, $confirm)
}

try {
    $main = Wait-For {
        $process.Refresh()
        if ($process.HasExited) { throw 'Portable WPF failed to start.' }
        if ($process.MainWindowHandle -ne 0) { $process.MainWindowHandle }
    } '4T main window'
    $root = $elementType::FromHandle($main)
    Click 'TwoStrokeButton'
    $root = Wait-For {
        $process.Refresh()
        if ($process.HasExited) { throw 'Portable exited after selecting 2T.' }
        foreach ($thread in $process.Threads) {
            foreach ($window in [DialogWindows]::Find([uint32]$thread.Id, 'HwndWrapper')) {
                $candidate = $elementType::FromHandle($window)
                if ($null -ne $candidate -and $candidate.Current.Name -like 'Dino2*2T') { return $candidate }
            }
        }
    } '2T window'
    Edit 'RPM inicio' '5000'
    Edit 'RPM fin' '5000'
    Click 'CalculateButton'
    $null = Status 'Curva 2T: 1/1' 40
    Click 'KeepButton'
    $null = Status 'Curva original A conservada'
    Write-Output 'PASS portable 4T/2T selection, simulated curve and immutable baseline'

    Select-Tab 'TwoStrokeStudyTab'
    Click 'Previsualizar costo 2T' -Label
    Click 'Ejecutar estudio 2T' -Label
    $null = Status 'Estudio 2T terminado' 65
    if ($AuditFiles) {
        Click 'Abrir estudio 2T…' -Label
        $notice = Wait-For {
            $process.Refresh()
            foreach ($thread in $process.Threads) {
                foreach ($handle in [DialogWindows]::Find([uint32]$thread.Id)) {
                    $button = [DialogWindows]::Child($handle, 'Button', 7) # Win32 IDNO
                    if ($button -ne [IntPtr]::Zero) { return @($handle, $button) }
                }
            }
        } 'unsaved study confirmation'
        [DialogWindows]::Confirm($notice[0], $notice[1], 7)
        $null = Wait-For { if ((Control 'Guardar estudio 2T…' -Label).Current.IsEnabled) { 1 } } 'dismissed confirmation'
        Write-Output 'PASS portable protects unsaved study before opening another one'
    }
    $study = Join-Path $output 'engine-study.json'
    File-Dialog 'Guardar estudio 2T…' $study -Label
    $null = Wait-For { Test-Path -LiteralPath $study } 'study saved'
    $model = Get-Content -LiteralPath $study -Raw | ConvertFrom-Json
    if ($model.run.evaluations.Count -ne 3 -or $model.run.definition.base.speeds.start -ne 5000) { throw 'Study has wrong samples.' }
    File-Dialog 'Abrir estudio 2T…' $study -Label
    $null = Status 'Estudio 2T recuperado'
    $studyCsv = Join-Path $output 'study.csv'
    File-Dialog 'Exportar estudio CSV…' $studyCsv -Label
    $null = Wait-For { Test-Path -LiteralPath $studyCsv } 'study CSV'
    Write-Output 'PASS portable 2T study execution, save, reopen and CSV'

    $itemsCondition = [System.Windows.Automation.PropertyCondition]::new(
        $elementType::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
    $items = (Control 'Candidatos 2T' -Label).FindAll($descendants, $itemsCondition)
    if ($items.Count -lt 3) { throw 'Missing candidate rows.' }
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Click 'Aplicar como B' -Label
    $null = Status 'Variante aplicada'
    Click 'CalculateButton'
    $null = Status 'Curva 2T: 1/1' 40
    if ($VerifyResolution) {
        Select-Tab 'TwoStrokeABTab'
        Click 'CheckTwoStrokeAB'
        $null = Status 'Contraste A/B terminado' 420
        if ((Control 'Resolution').Current.Name -notmatch 'indistinguible') {
            throw "Unexpected A/B resolution: $((Control 'Resolution').Current.Name)"
        }
        Write-Output 'PASS portable matched A/B refinement and numerical verdict'
    }
    $file = Join-Path $output 'engine-project.json'
    File-Dialog 'SaveTwoStroke' $file
    $null = Status 'Proyecto 2T guardado'
    $project = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    if ($project.project.current.expansion.diffuserM -ne .29 -or $project.project.original.expansion.diffuserM -ne .27) {
        throw 'The variant was not applied or modified the original.'
    }
    if ($project.format -like '*-v2' -and ($project.integrity -notmatch '^[0-9a-f]{64}$' -or
        !$project.project.currentResult.provenance.build)) {
        throw 'The v2 project has no seal or build revision.'
    }
    File-Dialog 'OpenTwoStroke' $file
    $null = Status 'Proyecto 2T recuperado'
    $curve = Join-Path $output 'curve.csv'
    File-Dialog 'ExportButton' $curve
    $null = Status 'CSV 2T exportado'
    if (!(Test-Path -LiteralPath (Join-Path $output 'curve.ab.csv'))) { throw 'A/B export missing.' }
    if ($VerifyResolution -and !(Test-Path -LiteralPath (Join-Path $output 'curve.ab-resolution.csv'))) {
        throw 'A/B numerical evidence export missing.'
    }
    Select-Tab 'TwoStrokeTraceTab'
    $trace = Join-Path $output 'trace.csv'
    File-Dialog 'ExportCycleButton' $trace
    $null = Status 'Traza 2T exportada'
    if ((Get-Content -LiteralPath $trace).Count -lt 250) { throw 'Trace export incomplete.' }
    Write-Output 'PASS portable applies B, reruns, reopens A/B and exports curves and cycle'
    if ($AuditFiles) {
        $corrupt = Join-Path $output 'corrupt-project.json'
        '{}' | Set-Content -Encoding utf8 $corrupt
        File-Dialog 'OpenTwoStroke' $corrupt
        $null = Status 'Archivo de proyecto 2T incompatible'
        $retained = Join-Path $output 'retained.csv'
        File-Dialog 'ExportButton' $retained
        $null = Status 'CSV 2T exportado'
        if ((@(Get-Content -LiteralPath $retained | Where-Object { $_ -notlike '#*' } | ConvertFrom-Csv)).Count -ne 1) {
            throw 'A malformed project replaced the current curve.'
        }
        Write-Output 'PASS portable rejects corrupted project and retains the previous results'
    }
    if ($TamperedProject) {
        $fixture = Join-Path $output 'audit-tampered.json'
        Copy-Item -LiteralPath $TamperedProject -Destination $fixture
        File-Dialog 'OpenTwoStroke' $fixture
        $null = Status 'La curva 2T y su configuración deben corresponder'
        if (!(Control 'ExportButton').Current.IsEnabled) { throw 'The rejected file replaced the working curve.' }
        Write-Output 'PASS portable rejects original audit tampering'
    }

    if ($VerifyDomain) {
        $partialInput = Join-Path (Split-Path -Parent $exe) 'examples/2t-125cc-partial-domain.json'
        File-Dialog 'OpenTwoStroke' $partialInput
        $null = Status 'Proyecto 2T recuperado'
        Click 'CalculateButton'
        $null = Status 'Curva 2T: 1/2' 70
        $partial = Join-Path $output 'partial.csv'
        File-Dialog 'ExportButton' $partial
        $null = Status 'CSV 2T exportado'
        $rows = @(Get-Content -LiteralPath $partial | Where-Object { $_ -notlike '#*' } | ConvertFrom-Csv)
        if ($rows.Count -ne 2 -or $rows[0].status -ne 'periodic' -or $rows[1].status -ne 'OutsideDomain' -or
            $rows[1].p_shaft_w -or $rows[1].diagnostic -notmatch '10000 RPM') { throw 'Unexpected partial-domain CSV.' }
        Write-Output 'PASS portable reports inadmissible RPM and keeps the converged point'
    }
    # Use a new multi-RPM workload so a cached one-point result cannot beat UI Automation.
    Edit 'RPM inicio' '4000'
    Edit 'RPM fin' '7500'
    Click 'CalculateButton'
    Click 'CancelButton'
    $null = Status 'Cálculo cancelado'
    $module = ($process.Modules | Where-Object ModuleName -eq 'coreclr.dll' | Select-Object -First 1).FileName
    if (!$module.StartsWith((Split-Path -Parent $exe), [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected runtime: $module" }
    Write-Output "PASS portable cancellation and bundled runtime: $module"
    Write-Output "Evidence: $output"
}
finally {
    $process.Refresh()
    if (!$process.HasExited) {
        $null = $process.CloseMainWindow()
        if (!$process.WaitForExit(3000)) { $process.Kill() }
    }
    $process.Dispose()
}
