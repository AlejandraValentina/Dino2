#Requires -Version 7
param(
    [Parameter(Mandatory)][string]$Application,
    [Parameter(Mandatory)][string]$Destination,
    [switch]$SkipResolution
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'native-file-dialog.ps1')
$exe = (Resolve-Path -LiteralPath $Application).Path
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$output = (Resolve-Path -LiteralPath $Destination).Path
foreach ($name in @('motor.json','curva.csv','ciclo.csv','cilindros.csv','comparacion.csv','estudio.csv','caso.json')) {
    if (Test-Path -LiteralPath (Join-Path $output $name)) { throw "Ya existe evidencia: $name. Elegí un directorio nuevo." }
}
$process = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden
$type = [System.Windows.Automation.AutomationElement]
$desc = [System.Windows.Automation.TreeScope]::Descendants
function Wait-For($test, [string]$description, [int]$seconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        $value = & $test
        if ($value) { return $value }
        if ($process.HasExited) { throw 'Dino2 se cerró durante el recorrido.' }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Tiempo agotado: $description"
}
function Control([string]$id) {
    Wait-For {
        $root.FindFirst($desc, [System.Windows.Automation.PropertyCondition]::new($type::AutomationIdProperty, $id))
    } "control $id" 10
}
function Click([string]$id) {
    (Control $id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Tab([string]$name) {
    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new($type::NameProperty, $name),
        [System.Windows.Automation.PropertyCondition]::new($type::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem))
    $tab = Wait-For { $root.FindFirst($desc, $condition) } "pestaña $name"
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Set-Value([string]$id, [string]$value) {
    (Control $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}
function Status([string]$prefix, [int]$seconds = 35) {
    $last = [ref]''
    Wait-For {
        $message = (Control 'M2Status').Current.Name
        if ($message.StartsWith('No se completó')) { throw $message }
        if ($last.Value -ne $message) {
            $last.Value = $message
            Write-Host $message
        }
        if ($message.StartsWith($prefix)) { return $message }
    } $prefix $seconds
}
function File-Dialog([string]$id, [string]$path) {
    Click $id
    $dialog = Wait-For {
        foreach ($thread in $process.Threads) {
            foreach ($handle in [DialogWindows]::Find([uint32]$thread.Id)) {
                if ([DialogWindows]::Child($handle, 'Edit', 1001) -ne [IntPtr]::Zero) { return $handle }
            }
        }
    } "archivo de $id"
    $edit = [DialogWindows]::Child($dialog, 'Edit', 1001)
    [DialogWindows]::SetFilename($edit, $path)
    if ([DialogWindows]::Text($edit) -ne $path) { throw 'No quedó el nombre solicitado.' }
    $button = [DialogWindows]::Child($dialog, 'Button', 1)
    if ($button -eq [IntPtr]::Zero) { throw 'Falta la confirmación del diálogo.' }
    [DialogWindows]::Confirm($dialog, $button)
}
function Select-EditorGroup([string]$name) {
    $box = Control 'EditorGroups'
    $expand = $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    $item = Wait-For {
        $box.FindFirst($desc, [System.Windows.Automation.PropertyCondition]::new($type::NameProperty, $name))
    } "grupo $name"
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $expand.Collapse()
}
$example = Join-Path (Split-Path -Parent $exe) 'examples/multi-2t-inline2.json'
$case = Get-Content -LiteralPath $example -Raw | ConvertFrom-Json
$case.project.current.cylinder.speeds.start = 5000
$case.project.current.cylinder.speeds.end = 5000
$case.project.current.cylinder.speeds.step = 1
$case.project.current.cylinder.intakeDuct.cells = 8
$case.project.current.cylinder.expansion.cells = 16
$case.project.current.sharedExhaust.primary.cells = 8
$fixture = Join-Path $output 'caso.json'
$case | ConvertTo-Json -Depth 35 | Set-Content -LiteralPath $fixture -Encoding utf8
try {
    $main = Wait-For { $process.Refresh(); if ($process.MainWindowHandle -ne 0) { $process.MainWindowHandle } } 'inicio 4T'
    $root = $type::FromHandle($main)
    Click 'MultiTwoStrokeButton'
    $root = Wait-For {
        foreach ($thread in $process.Threads) {
            foreach ($handle in [DialogWindows]::Find([uint32]$thread.Id, 'HwndWrapper')) {
                $candidate = $type::FromHandle($handle)
                if ($null -ne $candidate -and $candidate.Current.Name -like 'Dino2*Multicilíndrico 2T') { return $candidate }
            }
        }
    } 'ventana multicilíndrica 2T'
    File-Dialog 'M2Open' $fixture
    $null = Status 'Proyecto 2T multicilíndrico abierto'
    Click 'M2Run'
    $first = Status 'Cálculo terminado' 70
    if (!$first.Contains('1/1 RPM periódicas')) { throw $first }
    Click 'M2Keep'
    $null = Status 'Configuración conservada como A'
    Select-EditorGroup 'Colector de escape'
    Set-Value 'M2Network_EVolume' '0.75'
    Click 'M2Run'
    $second = Status 'Cálculo terminado' 70
    if (!$second.Contains('1/1 RPM periódicas')) { throw $second }
    Tab 'A/B'
    if (!$SkipResolution) {
        $contrastClock = [Diagnostics.Stopwatch]::StartNew()
        Click 'M2Compare'
        $checked = Status '5000 RPM · Sensible a discretización' 300
        $contrastClock.Stop()
    }
    File-Dialog 'M2ExportAB' (Join-Path $output 'comparacion.csv')
    $null = Status 'CSV exportado'
    if (!$SkipResolution -and !(Select-String -Quiet -Path (Join-Path $output 'comparacion.csv') -Pattern 'DiscretizationSensitive')) {
        throw 'Falta la clasificación numérica en el CSV.'
    }
    Tab 'Ciclo y barrido'
    File-Dialog 'M2Trace' (Join-Path $output 'ciclo.csv')
    $null = Wait-For {
        $path = Join-Path $output 'ciclo.csv'
        (Test-Path -LiteralPath $path) -and @(Get-Content -LiteralPath $path).Count -ge 600
    } 'ciclo de ambos cilindros'
    File-Dialog 'M2Cylinders' (Join-Path $output 'cilindros.csv')
    $null = Wait-For {
        $path = Join-Path $output 'cilindros.csv'
        (Test-Path -LiteralPath $path) -and (Select-String -Quiet -Path $path -Pattern '^5000,2,')
    } 'datos de barrido de ambos cilindros'
    Tab 'Estudios'
    Click 'M2TakeBase'
    Set-Value 'M2StudyAxis1Fin' '160'
    Set-Value 'M2StudyAxis1Paso' '20'
    Click 'M2StudyRun'
    $study = Status 'Estudio terminado' 90
    if (!$study.Contains('3 casos')) { throw $study }
    File-Dialog 'M2StudyExport' (Join-Path $output 'estudio.csv')
    $null = Wait-For { Test-Path -LiteralPath (Join-Path $output 'estudio.csv') } 'CSV del estudio'
    File-Dialog 'M2Save' (Join-Path $output 'motor.json')
    $null = Status 'Proyecto 2T multicilíndrico guardado'
    File-Dialog 'M2Open' (Join-Path $output 'motor.json')
    $null = Status 'Proyecto 2T multicilíndrico abierto'
    File-Dialog 'M2Export' (Join-Path $output 'curva.csv')
    $null = Status 'CSV exportado'
    Tab 'Estudios'
    Tab 'Clasificación'
    Click 'M2Apply'
    $null = Status 'Caso '
    Click 'M2Run'
    Click 'M2Cancel'
    $null = Status 'Cancelado'
    $process.Refresh()
    $runtime = ($process.Modules | Where-Object ModuleName -eq 'coreclr.dll' | Select-Object -First 1).FileName
    if (!(Split-Path -Parent $runtime).Equals((Split-Path -Parent $exe), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Runtime fuera del portable: $runtime"
    }
    "PASS $first"
    "PASS $second"
    if (!$SkipResolution) { "PASS $checked · $($contrastClock.Elapsed.TotalSeconds) s" }
    "PASS $study"
    "PASS portable: barrido, archivos, A/B, estudio, aplicar B, cancelar y runtime incluido ($runtime)"
    "Evidencia: $output"
}
finally {
    $process.Refresh()
    if (!$process.HasExited) {
        $null = $process.CloseMainWindow()
        if (!$process.WaitForExit(5000)) { $process.Kill() }
    }
    $process.Dispose()
}
