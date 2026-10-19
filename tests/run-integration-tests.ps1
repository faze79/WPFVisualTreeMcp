param(
    [Parameter(Mandatory)][string]$ServerDirectory,
    [string]$WorkDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $WorkDirectory) { $WorkDirectory = Join-Path $root "TestResults/matrix-$([Guid]::NewGuid().ToString('N'))" }
$work = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $work) { throw 'Use a new integration work directory.' }
New-Item -ItemType Directory -Path $work | Out-Null
$inspectorProject = Join-Path $root 'src/WpfVisualTreeMcp.Inspector/WpfVisualTreeMcp.Inspector.csproj'
foreach ($framework in @('net472', 'net48', 'net8.0-windows')) {
    foreach ($architecture in @('x86', 'x64')) {
        foreach ($mode in @('SelfHosted', 'AutoInjection')) {
            $sampleRoot = Join-Path $work "samples/$framework/$architecture/$mode"
            $source = Join-Path $work "source/$framework/$architecture/$mode"
            New-Item -ItemType Directory -Force $source | Out-Null
            Get-ChildItem "$root/samples/SampleWpfApp/*" -Include *.cs,*.xaml,*.csproj | Copy-Item -Destination $source
            $project = Join-Path $source 'SampleWpfApp.csproj'
            [xml]$xml = Get-Content -LiteralPath $project
            $xml.Project.ItemGroup.ProjectReference.SetAttribute('Include', $inspectorProject)
            $xml.Save($project)
            $hosting = if ($mode -eq 'SelfHosted') { 'true' } else { 'false' }
            $arguments = @('publish', $project, '-c', 'Release', "-p:SampleTargetFramework=$framework",
                "-p:PlatformTarget=$architecture", "-p:EnableInspectorSelfHosting=$hosting", '--output', $sampleRoot, '--verbosity', 'quiet')
            if ($framework -eq 'net8.0-windows') { $arguments += @('--runtime', "win-$architecture", '--self-contained', 'false') }
            & dotnet @arguments
            if ($LASTEXITCODE -ne 0) { throw "Sample build failed: $framework/$architecture/$mode" }
        }
    }
}
$previousServer = $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SERVER
$previousSamples = $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SAMPLES
try {
    $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SERVER = Join-Path ([IO.Path]::GetFullPath($ServerDirectory)) 'WpfVisualTreeMcp.Server.exe'
    $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SAMPLES = Join-Path $work 'samples'
    & dotnet test "$root/tests/WpfVisualTreeMcp.IntegrationTests/WpfVisualTreeMcp.IntegrationTests.csproj" -c Release --filter 'Category=Integration' --verbosity normal
    if ($LASTEXITCODE -ne 0) { throw 'Inspection/screenshot integration matrix failed.' }
}
finally {
    $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SERVER = $previousServer
    $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SAMPLES = $previousSamples
}
Write-Output "Matrix artifacts: $work"
