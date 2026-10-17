param(
    [string]$ServerExecutable = "$PSScriptRoot/../src/WpfVisualTreeMcp.Server/bin/Release/net8.0/WpfVisualTreeMcp.Server.exe",
    [string[]]$Configurations = @('net48-x64', 'net48-x86', 'net8.0-windows-x64')
)
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/Test-Drag.ps1" -ServerExecutable $ServerExecutable -Configurations $Configurations -TestKind touch
