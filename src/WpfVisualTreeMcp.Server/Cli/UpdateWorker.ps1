param([Parameter(Mandatory = $true)][string]$PlanFile)

$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath $PlanFile -Raw | ConvertFrom-Json
$logPath = Join-Path $plan.workDirectory 'update.log'
$backup = Join-Path $plan.workDirectory 'backup'
$changed = New-Object 'System.Collections.Generic.List[object]'
$updateLock = $null

function Write-Status([string]$state, [string]$message) {
    $result = [ordered]@{
        status = $state
        updaterProcessId = $PID
        updaterStartTicks = (Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks
        previousVersion = $plan.currentVersion
        latestVersion = $plan.version
        installation = $plan.kind
        directory = $plan.targetDirectory
        message = $message
        logPath = $logPath
        backupPath = $backup
        updatedAt = [DateTime]::UtcNow.ToString('o')
    }
    $temporary = $plan.statusPath + '.tmp'
    $result | ConvertTo-Json | Set-Content -LiteralPath $temporary -Encoding UTF8
    Move-Item -LiteralPath $temporary -Destination $plan.statusPath -Force
}

function Get-ContainedPath([string]$root, [string]$relative) {
    $prefix = [IO.Path]::GetFullPath($root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $path = [IO.Path]::GetFullPath((Join-Path $prefix $relative))
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes the installation directory: $relative"
    }
    # Reject junctions/symlinks in the destination, including ancestor directories.
    $itemPath = $path
    while ($itemPath) {
        if (Test-Path -LiteralPath $itemPath) {
            if ((Get-Item -LiteralPath $itemPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Cannot update through a filesystem link: $itemPath"
            }
        }
        $itemPath = Split-Path -Path $itemPath -Parent
    }
    return $path
}

try {
    # Verify the start time as well as PID, so a reused PID cannot block an update.
    $parent = Get-Process -Id $plan.parentId -ErrorAction SilentlyContinue
    if ($parent -and $parent.StartTime.ToUniversalTime().Ticks -eq $plan.parentStartTicks) {
        if (-not $parent.WaitForExit(60000)) { throw 'The launching CLI did not exit within 60 seconds.' }
    }
    $lockPath = Join-Path $plan.stateDirectory 'update.lock'
    $updateLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    Write-Status 'running' 'Installing the downloaded update.'
    "Updating $($plan.targetDirectory) to $($plan.version)" | Set-Content -LiteralPath $logPath

    if ($plan.kind -eq 'dotnet-tool') {
        $config = Join-Path $plan.workDirectory 'NuGet.Config'
        # An argument array keeps spaces and shell metacharacters in paths literal.
        $toolArgs = @('tool', 'update', 'WpfVisualTreeMcp', '--tool-path', $plan.targetDirectory,
            '--version', $plan.version, '--configfile', $config)
        & $plan.dotnet @toolArgs >> $logPath 2>&1
        if ($LASTEXITCODE -ne 0) { throw "dotnet tool update failed (exit $LASTEXITCODE). See $logPath" }
    }
    elseif ($plan.kind -eq 'portable') {
        $payloadRoot = [IO.Path]::GetFullPath($plan.payloadDirectory).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        $files = @(Get-ChildItem -LiteralPath $payloadRoot -File -Recurse -Force)
        if ($files.Count -eq 0) { throw 'The update payload is empty.' }
        $operations = @()
        # Complete all path/lock checks and backups before changing the installation.
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($payloadRoot.Length)
            $destination = Get-ContainedPath $plan.targetDirectory $relative
            $saved = Get-ContainedPath $backup $relative
            $exists = Test-Path -LiteralPath $destination
            if ($exists) {
                $handle = [IO.File]::Open($destination, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
                $handle.Dispose()
                [IO.Directory]::CreateDirectory((Split-Path -Path $saved -Parent)) | Out-Null
                Copy-Item -LiteralPath $destination -Destination $saved
            }
            $operations += [pscustomobject]@{ Source=$file.FullName; Destination=$destination; Saved=$saved; Existed=$exists }
        }
        foreach ($operation in $operations) {
            [IO.Directory]::CreateDirectory((Split-Path -Path $operation.Destination -Parent)) | Out-Null
            # Record before copying: a failed copy may have partially overwritten the file.
            $changed.Add($operation)
            Copy-Item -LiteralPath $operation.Source -Destination $operation.Destination -Force
        }
        $dll = Join-Path $plan.targetDirectory 'WpfVisualTreeMcp.Server.dll'
        $installed = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion.Split('+')[0]
        if ($installed -ne $plan.version) { throw "Installed version $installed does not match $($plan.version)." }
    }
    else { throw "Unsupported installation kind: $($plan.kind)" }

    Write-Status 'updated' 'Update completed. Restart your MCP client and previously inspected applications to load the new version.'
    'Update completed.' | Add-Content -LiteralPath $logPath
}
catch {
    $failure = $_.Exception.Message
    $rollbackErrors = @()
    for ($i = $changed.Count - 1; $i -ge 0; $i--) {
        $operation = $changed[$i]
        try {
            if ($operation.Existed) {
                Copy-Item -LiteralPath $operation.Saved -Destination $operation.Destination -Force
            }
            elseif (Test-Path -LiteralPath $operation.Destination) {
                Remove-Item -LiteralPath $operation.Destination -Force
            }
        }
        catch { $rollbackErrors += $_.Exception.Message }
    }
    if ($rollbackErrors.Count -gt 0) { $failure += " Rollback needs attention; backups: $backup. " + ($rollbackErrors -join ' ') }
    $cause = $_.Exception.GetBaseException()
    if (($cause.HResult -band 0xFFFF) -in @(32, 33)) {
        $failure += ' Close other MCP instances and applications using this installation, then retry.'
    }
    $failure | Add-Content -LiteralPath $logPath
    Write-Status 'failed' $failure
    exit 1
}
finally {
    if ($updateLock) { $updateLock.Dispose() }
}
