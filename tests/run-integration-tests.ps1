[CmdletBinding()]
param(
    [string]$PlatformToolset,
    [switch]$SkipNativeBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\integration'))
$pathPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $artifactsRoot.StartsWith($pathPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Integration artifact path is outside the repository: $artifactsRoot"
}

function Invoke-ExternalCommand {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(Mandatory)]
        [string[]]$ArgumentList
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($ArgumentList -join ' ')"
    }
}

function Get-MSBuildPath {
    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw 'MSBuild was not found. Install Visual Studio Build Tools with Desktop development with C++.'
    }

    $path = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($path)) {
        throw 'MSBuild was not found. Install Visual Studio Build Tools with Desktop development with C++.'
    }
    return $path
}

function Assert-X86DotNetRuntime {
    $dotnetRoot = $env:DOTNET_ROOT_X86
    if ([string]::IsNullOrWhiteSpace($dotnetRoot)) {
        $dotnetRoot = Join-Path ${env:ProgramFiles(x86)} 'dotnet'
    }
    $env:DOTNET_ROOT_X86 = $dotnetRoot

    $dotnet = Join-Path $dotnetRoot 'dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnet)) {
        throw "The x86 .NET 8 runtime is required by WpfInjectorHelper. Install it or set DOTNET_ROOT_X86 (checked $dotnet)."
    }

    $runtimes = & $dotnet --list-runtimes
    if ($LASTEXITCODE -ne 0 -or
        -not ($runtimes -match '^Microsoft\.NETCore\.App 8\.') -or
        -not ($runtimes -match '^Microsoft\.WindowsDesktop\.App 8\.')) {
        throw "The x86 .NET 8 and Windows Desktop runtimes are required by WpfInjectorHelper and the sample matrix (checked $dotnet)."
    }
}

Assert-X86DotNetRuntime

if (Test-Path -LiteralPath $artifactsRoot) {
    Remove-Item -LiteralPath $artifactsRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactsRoot | Out-Null

if (-not $SkipNativeBuild) {
    $msbuild = Get-MSBuildPath
    $bootstrapper = Join-Path $repoRoot 'src\WpfVisualTreeMcp.Bootstrapper\WpfVisualTreeMcp.Bootstrapper.vcxproj'
    foreach ($platform in @('x64', 'Win32')) {
        $arguments = @(
            $bootstrapper,
            '/m',
            '/p:Configuration=Release',
            "/p:Platform=$platform"
        )
        if (-not [string]::IsNullOrWhiteSpace($PlatformToolset)) {
            $arguments += "/p:PlatformToolset=$PlatformToolset"
        }
        Invoke-ExternalCommand $msbuild $arguments
    }
}

$inspector = Join-Path $repoRoot 'src\WpfVisualTreeMcp.Inspector\WpfVisualTreeMcp.Inspector.csproj'
$injectorHelper = Join-Path $repoRoot 'src\WpfVisualTreeMcp.InjectorHelper\WpfVisualTreeMcp.InjectorHelper.csproj'
$server = Join-Path $repoRoot 'src\WpfVisualTreeMcp.Server\WpfVisualTreeMcp.Server.csproj'
$sample = Join-Path $repoRoot 'samples\SampleWpfApp\SampleWpfApp.csproj'
$integrationTests = Join-Path $repoRoot 'tests\WpfVisualTreeMcp.IntegrationTests\WpfVisualTreeMcp.IntegrationTests.csproj'
$serverOutput = Join-Path $artifactsRoot 'server'
$samplesOutput = Join-Path $artifactsRoot 'samples'
$sampleIntermediateOutput = [IO.Path]::GetFullPath((Join-Path (Split-Path $sample) 'obj'))
$samplePathPrefix = [IO.Path]::GetFullPath((Split-Path $sample)).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $sampleIntermediateOutput.StartsWith($samplePathPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Sample intermediate output path is outside the sample project: $sampleIntermediateOutput"
}

Invoke-ExternalCommand dotnet @('build', $inspector, '--configuration', 'Release', '--no-incremental')
Invoke-ExternalCommand dotnet @('build', $injectorHelper, '--configuration', 'Release', '--no-incremental')
Invoke-ExternalCommand dotnet @(
    'publish',
    $server,
    '--configuration',
    'Release',
    '--output',
    $serverOutput
)

foreach ($targetFramework in @('net472', 'net48', 'net8.0-windows')) {
    foreach ($architecture in @('x86', 'x64')) {
        $output = Join-Path $samplesOutput "$targetFramework\$architecture"
        if (Test-Path -LiteralPath $sampleIntermediateOutput) {
            Remove-Item -LiteralPath $sampleIntermediateOutput -Recurse -Force
        }
        $arguments = @(
            'publish',
            $sample,
            '--configuration',
            'Release',
            '--framework',
            $targetFramework,
            '--runtime',
            "win-$architecture",
            '--output',
            $output,
            "-p:PlatformTarget=$architecture"
        )
        if ($targetFramework -eq 'net8.0-windows') {
            $arguments += @('--self-contained', 'false')
        }
        Invoke-ExternalCommand dotnet $arguments
    }
}

# Leave the shared intermediate output in its normal, non-RID-specific state.
Invoke-ExternalCommand dotnet @('restore', $sample)

Invoke-ExternalCommand dotnet @('build', $integrationTests, '--configuration', 'Release')

$previousServer = $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SERVER
$previousSamples = $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SAMPLES
try {
    $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SERVER = Join-Path $serverOutput 'WpfVisualTreeMcp.Server.exe'
    $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SAMPLES = $samplesOutput
    Invoke-ExternalCommand dotnet @(
        'test',
        $integrationTests,
        '--configuration',
        'Release',
        '--no-build',
        '--filter',
        'Category=Integration',
        '--verbosity',
        'normal'
    )
}
finally {
    $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SERVER = $previousServer
    $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SAMPLES = $previousSamples
}
