# Sync updated Inspector DLLs to ValueS application
# This script stops ValueS, copies the DLLs, and optionally restarts it

param(
    [switch]$NoRestart,
    [string]$ValuesExePath = "C:\DevOPS\VALUES\Salvagnini.ValueS\bin\x64\Debug\Salvagnini.ValueS.exe"
)

Write-Host "=== WPF Visual Tree Inspector - Sync to ValueS ===" -ForegroundColor Cyan
Write-Host ""

$inspectorOutput = Join-Path $PSScriptRoot 'src\WpfVisualTreeMcp.Inspector\bin\Debug\net48'
$valuesOutput = Split-Path -Parent $ValuesExePath
$inspectorDlls = @(Get-ChildItem -LiteralPath $inspectorOutput -Filter '*.dll' -File -ErrorAction Stop)
if ($inspectorDlls.Count -eq 0) {
    throw "No Inspector DLLs were found at: $inspectorOutput"
}
if (-not (Test-Path -LiteralPath $valuesOutput -PathType Container)) {
    throw "ValueS output directory was not found at: $valuesOutput"
}

# Stop ValueS if running
$valuesProcess = Get-Process ValueS -ErrorAction SilentlyContinue
if ($valuesProcess) {
    Write-Host "[1/3] Stopping ValueS process (PID: $($valuesProcess.Id))..." -ForegroundColor Yellow
    Stop-Process -Name ValueS -Force
    Start-Sleep -Seconds 2
    Write-Host "      ValueS stopped." -ForegroundColor Green
} else {
    Write-Host "[1/3] ValueS is not running." -ForegroundColor Gray
}

# Copy Inspector and its complete .NET Framework dependency closure
Write-Host "[2/3] Copying Inspector dependency closure..." -ForegroundColor Yellow
try {
    Copy-Item -LiteralPath $inspectorDlls.FullName -Destination $valuesOutput -Force

    Write-Host "      Copied $($inspectorDlls.Count) DLLs." -ForegroundColor Green
} catch {
    Write-Host "      ERROR: Failed to copy Inspector dependency closure - $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Restart ValueS (unless -NoRestart is specified)
if (-not $NoRestart) {
    Write-Host "[3/3] Starting ValueS..." -ForegroundColor Yellow

    if (Test-Path -LiteralPath $ValuesExePath) {
        Start-Process $ValuesExePath -WorkingDirectory (Split-Path $ValuesExePath)
        Start-Sleep -Seconds 2

        $newProcess = Get-Process ValueS -ErrorAction SilentlyContinue
        if ($newProcess) {
            Write-Host "      ValueS started (PID: $($newProcess.Id))" -ForegroundColor Green
        } else {
            Write-Host "      WARNING: ValueS may not have started successfully" -ForegroundColor Yellow
        }
    } else {
        Write-Host "      ERROR: ValueS.exe not found at: $ValuesExePath" -ForegroundColor Red
        Write-Host "      Please start ValueS manually" -ForegroundColor Yellow
    }
} else {
    Write-Host "[3/3] Skipping restart (-NoRestart specified)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "=== Sync completed ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Test with: wpf_attach(process_id: <PID>)" -ForegroundColor Gray
Write-Host "  2. Try: wpf_find_elements(type_name: 'TabItem', max_results: 10)" -ForegroundColor Gray
