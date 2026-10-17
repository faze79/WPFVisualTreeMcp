param([Parameter(Mandatory)][string]$Package)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Package))
try {
    $entries = @($archive.Entries.FullName)
    foreach ($architecture in @('x64', 'x86')) {
        foreach ($file in @('WpfInspectorBootstrapper.dll', 'WpfVisualTreeMcp.Inspector.dll',
                           'WpfVisualTreeMcp.Shared.dll', 'System.Text.Json.dll',
                           'System.Runtime.CompilerServices.Unsafe.dll',
                           'coreclr/WpfVisualTreeMcp.Inspector.dll',
                           'coreclr/WpfVisualTreeMcp.Shared.dll',
                           'coreclr/WpfVisualTreeMcp.Inspector.coreclr.runtimeconfig.json')) {
            if (!($entries -like "tools/*/any/native/$architecture/$file")) {
                throw "NuGet package is missing native/$architecture/$file"
            }
        }
    }
    foreach ($file in @('WpfInjectorHelper.exe', 'WpfInjectorHelper.dll',
                       'WpfInjectorHelper.deps.json', 'WpfInjectorHelper.runtimeconfig.json',
                       'WpfVisualTreeMcp.Injector.dll')) {
        if (!($entries -like "tools/*/any/native/x86/$file")) {
            throw "NuGet package is missing native/x86/$file"
        }
    }
    Write-Host 'PASS: NuGet contains both native/runtime payloads and the x86 helper.'
}
finally { $archive.Dispose() }
