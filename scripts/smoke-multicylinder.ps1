#Requires -Version 7
param(
    [Parameter(Mandatory)][string]$Application,
    [Parameter(Mandatory)][string]$Destination
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'native-file-dialog.ps1')
$exe = (Resolve-Path -LiteralPath $Application).Path
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$output = (Resolve-Path -LiteralPath $Destination).Path
foreach ($name in @('motor.json','curva.csv','ciclo.csv','cilindros.csv','comparacion.csv','estudio.csv')) {
    if (Test-Path -LiteralPath (Join-Path $output $name)) { throw "Ya existe evidencia: $name. Elegí un directorio nuevo." }
}
$process = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden
$type = [System.Windows.Automation.AutomationElement]
$desc = [System.Windows.Automation.TreeScope]::Descendants
function Wait-For($test, [string]$description, [int]$seconds = 30) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        $value = $null
        try { $value = & $test }
        catch [System.IO.IOException], [System.UnauthorizedAccessException] {
            # The native dialog can return while a newly exported file is still locked.
            $value = $null
        }
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
        $message = (Control 'MultiStatusText').Current.Name
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
try {
    $main = Wait-For { $process.Refresh(); if ($process.MainWindowHandle -ne 0) { $process.MainWindowHandle } } 'inicio 4T'
    $root = $type::FromHandle($main)
    Click 'MulticylinderButton'
    $root = Wait-For {
        foreach ($thread in $process.Threads) {
            foreach ($handle in [DialogWindows]::Find([uint32]$thread.Id, 'HwndWrapper')) {
                $candidate = $type::FromHandle($handle)
                if ($null -ne $candidate -and $candidate.Current.Name -like 'Dino2*Multicilíndrico 4T') { return $candidate }
            }
        }
    } 'ventana multicilíndrica'
    Tab 'RPM / cálculo'
    Set-Value 'MultiField_RpmStart' '4000'
    Set-Value 'MultiField_RpmEnd' '4000'
    Set-Value 'MultiField_Cycles' '65'
    Click 'MultiCalculate'
    $first = Status 'Cálculo completado'
    if (!$first.Contains('1/1 RPM periódicas')) { throw $first }
    Click 'MultiKeep'
    $null = Status 'Configuración y curva guardadas como A'
    Tab 'Colectores'
    Set-Value 'MultiField_IntakeVolume' '3.6'
    Click 'MultiCalculate'
    $second = Status 'Cálculo completado'
    if (!$second.Contains('1/1 RPM periódicas')) { throw $second }
    Tab 'A/B'
    $contrastClock = [Diagnostics.Stopwatch]::StartNew()
    Click 'MultiCheck'
    $checked = Status '4000 RPM' 300
    $verdict = switch -Regex ($checked) {
        'B superior' { 'Superior'; break }
        'B inferior' { 'Inferior'; break }
        'indistinguibles' { 'Indistinguishable'; break }
        'Sensible a discretización' { 'DiscretizationSensitive'; break }
        'Sin contraste|No disponible' { 'Unavailable'; break }
        default { throw $checked }
    }
    $contrastClock.Stop()
    File-Dialog 'MultiABExport' (Join-Path $output 'comparacion.csv')
    $null = Status 'CSV exportado'
    $null = Wait-For {
        (Test-Path -LiteralPath (Join-Path $output 'comparacion.csv')) -and
            (Select-String -Quiet -Path (Join-Path $output 'comparacion.csv') -Pattern "^$verdict,4000,")
    } 'clasificación en CSV'
    Tab 'Cilindros y ciclo'
    File-Dialog 'MultiTraceExport' (Join-Path $output 'ciclo.csv')
    $null = Wait-For {
        $path = Join-Path $output 'ciclo.csv'
        (Test-Path -LiteralPath $path) -and @(Get-Content -LiteralPath $path).Count -ge 1400
    } 'muestras de ambos cilindros'
    File-Dialog 'MultiCylinderExport' (Join-Path $output 'cilindros.csv')
    $null = Wait-For {
        $path = Join-Path $output 'cilindros.csv'
        (Test-Path -LiteralPath $path) -and (Select-String -Quiet -Path $path -Pattern '^4000,2,')
    } 'resumen CSV de ambos cilindros'

    Tab 'Estudios'
    Click 'MultiStudyUseCurrent'
    Set-Value 'MultiStudyAxis1Fin' '400'
    Set-Value 'MultiStudyAxis1Incremento' '20'
    Click 'MultiStudyStart'
    $study = Status 'Estudio completado'
    if (!$study.Contains('3 casos')) { throw $study }
    File-Dialog 'MultiStudyExport' (Join-Path $output 'estudio.csv')
    $null = Wait-For { Test-Path -LiteralPath (Join-Path $output 'estudio.csv') } 'CSV del estudio'
    File-Dialog 'MultiSave' (Join-Path $output 'motor.json')
    $null = Status 'Proyecto multicilíndrico guardado'
    File-Dialog 'MultiOpen' (Join-Path $output 'motor.json')
    $null = Status 'Proyecto multicilíndrico abierto'
    File-Dialog 'MultiExport' (Join-Path $output 'curva.csv')
    $null = Status 'CSV exportado'
    Tab 'Estudios'
    Tab 'Clasificación'
    Click 'MultiStudyApply'
    $null = Status 'Caso '
    Click 'MultiCalculate'
    Click 'MultiCancel'
    $null = Status 'Cancelado'
    $process.Refresh()
    $runtime = ($process.Modules | Where-Object ModuleName -eq 'coreclr.dll' | Select-Object -First 1).FileName
    if (!(Split-Path -Parent $runtime).Equals((Split-Path -Parent $exe), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Runtime fuera del portable: $runtime"
    }
    "PASS $first"
    "PASS $second"
    "PASS $checked"
    "Contraste nativo: $($contrastClock.Elapsed.TotalSeconds) s"
    "PASS $study"
    "PASS portable: ciclo, archivos, A/B, estudio, aplicar B, cancelar y runtime incluido ($runtime)"
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
