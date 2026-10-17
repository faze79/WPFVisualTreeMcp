param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$MSBuild = 'msbuild'
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../src/WpfVisualTreeMcp.Bootstrapper/WpfVisualTreeMcp.Bootstrapper.vcxproj'
foreach ($architecture in @('Win32', 'x64')) {
    & $MSBuild $project /nologo /verbosity:minimal "/p:Configuration=$Configuration" "/p:Platform=$architecture"
    if ($LASTEXITCODE -ne 0) { throw "Native bootstrapper build failed: $architecture" }
}
