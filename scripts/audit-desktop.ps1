param(
    [Parameter(Mandatory)][string]$Application,
    [Parameter(Mandatory)][string]$Destination,
    [switch]$Consolidated
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class DinoAuditWindow
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Bounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Bounds bounds);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    private delegate bool WindowVisitor(IntPtr handle, IntPtr arg);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowVisitor visitor, IntPtr arg);
    public static IntPtr[] Windows(uint thread)
    {
        var handles = new System.Collections.Generic.List<IntPtr>();
        EnumThreadWindows(thread, delegate (IntPtr handle, IntPtr arg) { handles.Add(handle); return true; }, IntPtr.Zero);
        return handles.ToArray();
    }
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
}
"@
$application = (Resolve-Path -LiteralPath $Application).Path
New-Item -ItemType Directory -Force $Destination | Out-Null
$dest = (Resolve-Path -LiteralPath $Destination).Path
$timer = [Diagnostics.Stopwatch]::StartNew()
$process = Start-Process -FilePath $application -WindowStyle Hidden -PassThru
$elementType = [System.Windows.Automation.AutomationElement]
$descendants = [System.Windows.Automation.TreeScope]::Descendants
$children = [System.Windows.Automation.TreeScope]::Children

function Until([scriptblock]$query,[string]$context,[int]$seconds=45) {
    $end = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        $result = & $query
        if ($result) { return $result }
        $process.Refresh()
        if ($process.HasExited) { throw "El proceso se cerró: $context" }
        Start-Sleep -Milliseconds 60
    } while ([DateTime]::UtcNow -lt $end)
    throw "Tiempo agotado: $context"
}
function Lookup($root,[string]$id,[switch]$Label) {
    $field = if ($Label) { $elementType::NameProperty } else { $elementType::AutomationIdProperty }
    $condition = [System.Windows.Automation.PropertyCondition]::new($field,$id)
    return $root.FindFirst($descendants,$condition)
}
function Click($root,[string]$id) {
    $item = Lookup $root $id
    if ($null -eq $item) {throw "Control ausente: $id"}
    $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function WindowByTitle([string]$caption) {
    $process.Refresh()
    foreach ($thread in $process.Threads) {
        foreach ($handle in [DinoAuditWindow]::Windows([uint32]$thread.Id)) {
            $item = $elementType::FromHandle($handle)
            if ($null -ne $item -and $item.Current.Name -eq $caption -and !$item.Current.IsOffscreen) {return $item}
        }
    }
}
function Capture($root,[string]$name) {
    $hwnd = [IntPtr]$root.Current.NativeWindowHandle
    $bounds = New-Object DinoAuditWindow+Bounds
    if (![DinoAuditWindow]::GetWindowRect($hwnd,[ref]$bounds)) {throw "No se pudo medir $name"}
    $w = $bounds.Right-$bounds.Left
    $h = $bounds.Bottom-$bounds.Top
    $canvas = [Drawing.Bitmap]::new($w,$h)
    try {
        $painter = [Drawing.Graphics]::FromImage($canvas)
        try {
            $context = $painter.GetHdc()
            try {
                if (![DinoAuditWindow]::PrintWindow($hwnd,$context,2)) {throw "No se pudo capturar $name"}
            } finally {$painter.ReleaseHdc($context)}
        } finally {$painter.Dispose()}
        $canvas.Save((Join-Path $dest "$name.png"),[Drawing.Imaging.ImageFormat]::Png)
    } finally {$canvas.Dispose()}
    "capture=$name; pixels=$($w)x$($h); dpi=$([DinoAuditWindow]::GetDpiForWindow($hwnd))"
}
try {
    $main = Until {
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) {$elementType::FromHandle($process.MainWindowHandle)}
    } 'Inicio 4T'
    $null = Until {(Lookup $main 'CalculateButton').Current.IsEnabled} '4T preparado'
    "startup_ms=$($timer.ElapsedMilliseconds); working_set_bytes=$($process.WorkingSet64)"
    $timer.Restart()
    Click $main 'CalculateButton'
    $null = Until {if ((Lookup $main 'Status').Current.Name -match '^\d+ RPM') {1}} 'Curva 4T'
    "4t_ui_elapsed_ms=$($timer.ElapsedMilliseconds)"
    Capture $main '4t-curvas'
    $windowHandle = [IntPtr]$main.Current.NativeWindowHandle
    [void][DinoAuditWindow]::SetWindowPos($windowHandle,[IntPtr]::Zero,0,0,1030,650,0x14)
    Start-Sleep -Milliseconds 250
    Capture $main '4t-compacto'

    Click $main 'TwoStrokeButton'
    $two = Until {WindowByTitle 'Dino2 · Simulación 2T'} 'Ventana 2T'
    $timer.Restart()
    Click $two 'CalculateButton'
    $null = Until {if ((Lookup $two 'Status').Current.Name -match '^Curva 2T') {1}} 'Curva 2T' 80
    "2t_ui_elapsed_ms=$($timer.ElapsedMilliseconds); working_set_bytes=$($process.WorkingSet64)"
    Capture $two '2t-curvas'
    $traceTab = Lookup $two 'TwoStrokeTraceTab'
    $selection = $traceTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $selection.Select()
    $null = Until {
        $button = Lookup $two 'ExportCycleButton'
        if ($selection.Current.IsSelected -and $null -ne $button -and !$button.Current.IsOffscreen) {1}
    } 'Pestaña de ciclo 2T visible'
    Start-Sleep -Milliseconds 300
    Capture $two '2t-ciclo'

    if ($Consolidated) {
        Click $two 'BackToFourStroke'
        $null = Until {if ($two.Current.IsOffscreen) {1}} '2T oculto'
        Click $main 'TwoStrokeButton'
        $two = Until {WindowByTitle 'Dino2 · Simulación 2T'} '2T reactivado'
        if ((Lookup $two 'Status').Current.Name -notmatch '^Curva 2T') {throw 'Se perdió el resultado al cambiar de familia.'}
        'PASS 4T/2T navigation retains calculated work'
    }
    Click $main 'OttoReferenceButton'
    $otto = Until {WindowByTitle 'Dino2'} 'Otto'
    Click $otto 'CalculateButton'
    $null = Until {if ((Lookup $otto 'Status').Current.Name -match 'regímenes calculados') {1}} 'Ciclo Otto'
    Capture $otto 'otto-curvas'
}
finally {
    $process.Refresh()
    if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
