# Sync updated Inspector DLLs to ValueS application
# This script stops ValueS, copies the DLLs, and optionally restarts it

param(
    [switch]$NoRestart,
    [string]$ValuesExePath = "C:\DevOPS\VALUES\Salvagnini.ValueS\bin\x64\Debug\Salvagnini.ValueS.exe"
)

Write-Host "=== WPF Visual Tree Inspector - Sync to ValueS ===" -ForegroundColor Cyan
Write-Host ""

# Stop ValueS if running
$valuesProcess = Get-Process ValueS -ErrorAction SilentlyContinue
if ($valuesProcess) {
    Write-Host "[1/4] Stopping ValueS process (PID: $($valuesProcess.Id))..." -ForegroundColor Yellow
    Stop-Process -Name ValueS -Force
    Start-Sleep -Seconds 2
    Write-Host "      ValueS stopped." -ForegroundColor Green
} else {
    Write-Host "[1/4] ValueS is not running." -ForegroundColor Gray
}

# Copy Inspector DLL
Write-Host "[2/4] Copying Inspector DLL..." -ForegroundColor Yellow
try {
    Copy-Item 'C:\DevOPS\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\bin\Debug\net48\WpfVisualTreeMcp.Inspector.dll' `
              -Destination 'C:\DevOPS\VALUES\Salvagnini.ValueS\bin\x64\Debug\' -Force

    $inspectorDll = Get-Item 'C:\DevOPS\VALUES\Salvagnini.ValueS\bin\x64\Debug\WpfVisualTreeMcp.Inspector.dll'
    Write-Host "      Inspector.dll copied (Modified: $($inspectorDll.LastWriteTime))" -ForegroundColor Green
} catch {
    Write-Host "      ERROR: Failed to copy Inspector.dll - $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Copy Shared DLL
Write-Host "[3/4] Copying Shared DLL..." -ForegroundColor Yellow
try {
    Copy-Item 'C:\DevOPS\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Shared\bin\Debug\net48\WpfVisualTreeMcp.Shared.dll' `
              -Destination 'C:\DevOPS\VALUES\Salvagnini.ValueS\bin\x64\Debug\' -Force

    $sharedDll = Get-Item 'C:\DevOPS\VALUES\Salvagnini.ValueS\bin\x64\Debug\WpfVisualTreeMcp.Shared.dll'
    Write-Host "      Shared.dll copied (Modified: $($sharedDll.LastWriteTime))" -ForegroundColor Green
} catch {
    Write-Host "      ERROR: Failed to copy Shared.dll - $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Restart ValueS (unless -NoRestart is specified)
if (-not $NoRestart) {
    Write-Host "[4/4] Starting ValueS..." -ForegroundColor Yellow

    if (Test-Path $ValuesExePath) {
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
    Write-Host "[4/4] Skipping restart (use -NoRestart to disable)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "=== Sync completed ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Restart Claude Code to reload the MCP server" -ForegroundColor Gray
Write-Host "  2. Test with: wpf_attach(process_id: <PID>)" -ForegroundColor Gray
Write-Host "  3. Try: wpf_find_elements(type_name: 'TabItem', max_results: 10)" -ForegroundColor Gray
