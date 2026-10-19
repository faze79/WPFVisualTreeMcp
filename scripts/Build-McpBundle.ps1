param(
    # Build the native bootstrapper and solution in this checkout first.
    [string]$SourceDirectory = (Split-Path $PSScriptRoot -Parent),
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a new output directory; existing files are never overwritten.' }
$bundle = Join-Path $output 'bundle'
$server = Join-Path $bundle 'server'
$helper = Join-Path $output 'helper'
New-Item -ItemType Directory -Path $bundle -Force | Out-Null

Push-Location $source
try {
    # Publish only the server for win-x64. Its x86 helper and multi-target Inspector
    # must not inherit this RID; their standard Release outputs were built earlier.
    foreach ($project in @('Shared', 'Injector')) {
        dotnet build "src/WpfVisualTreeMcp.$project/WpfVisualTreeMcp.$project.csproj" -c Release -f net8.0 -r win-x64 --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw "$project build failed" }
    }
    dotnet publish src/WpfVisualTreeMcp.Server/WpfVisualTreeMcp.Server.csproj -c Release -r win-x64 --self-contained true -p:BuildProjectReferences=false -o $server --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained server publish failed' }
    dotnet publish src/WpfVisualTreeMcp.InjectorHelper/WpfVisualTreeMcp.InjectorHelper.csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $helper --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained helper publish failed' }
    # Copy ONLY the single-file helper. Its runtime DLLs would otherwise overwrite
    # the .NET Framework Inspector dependencies in native/x86 and break injection.
    Copy-Item -LiteralPath (Join-Path $helper 'WpfInjectorHelper.exe') -Destination (Join-Path $server 'native/x86/WpfInjectorHelper.exe') -Force

    # Include the licenses for both bundled runtime architectures.
    $runtime = (Get-Content (Join-Path $server 'WpfVisualTreeMcp.Server.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks[0].version
    $assets = Get-Content src/WpfVisualTreeMcp.Server/obj/project.assets.json -Raw | ConvertFrom-Json
    foreach ($architecture in @('x64', 'x86')) {
        $package = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path $folder "microsoft.netcore.app.runtime.win-$architecture/$runtime"
            if (Test-Path -LiteralPath (Join-Path $candidate 'LICENSE.TXT')) { $package = $candidate; break }
        }
        if (-not $package) { throw "Cannot find runtime license for win-$architecture $runtime" }
        foreach ($notice in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
            Copy-Item -LiteralPath (Join-Path $package $notice) -Destination (Join-Path $bundle "DOTNET-win-$architecture-$notice")
        }
    }
    Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination $bundle
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../docs/MCPB.md') -Destination (Join-Path $bundle 'README.md')
    python (Join-Path $PSScriptRoot 'create-mcpb-manifest.py') --source $source --bundle $bundle
    if ($LASTEXITCODE -ne 0) { throw 'MCP schema verification/manifest generation failed' }
    $manifest = Get-Content (Join-Path $bundle 'manifest.json') -Raw | ConvertFrom-Json
    $archive = Join-Path $output "WpfVisualTreeMcp-v$($manifest.version)-win-x64.mcpb"
    npx --yes @anthropic-ai/mcpb@2.1.2 pack $bundle $archive
    if ($LASTEXITCODE -ne 0) { throw 'MCPB validation/pack failed' }
    Get-FileHash -LiteralPath $archive -Algorithm SHA256
} finally {
    Pop-Location
}
