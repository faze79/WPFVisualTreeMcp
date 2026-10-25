param(
    [Parameter(Mandatory)][string]$ServerDirectory,
    [string]$WorkDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$matrix = Get-Content (Join-Path $PSScriptRoot 'compatibility-matrix.json') -Raw | ConvertFrom-Json
$frameworkRelease = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release).Release
if ($frameworkRelease -lt 533320) { throw 'The compatibility matrix requires .NET Framework 4.8.1.' }
if (-not $WorkDirectory) { $WorkDirectory = Join-Path $root "TestResults/matrix-$([Guid]::NewGuid().ToString('N'))" }
$work = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $work) { throw 'Use a new integration work directory.' }
New-Item -ItemType Directory -Path $work | Out-Null
$inspectorProject = Join-Path $root 'src/WpfVisualTreeMcp.Inspector/WpfVisualTreeMcp.Inspector.csproj'
foreach ($framework in $matrix.targetFrameworks) {
    foreach ($architecture in $matrix.architectures) {
        foreach ($mode in $matrix.inspectionModes) {
            $sampleRoot = Join-Path $work "samples/$framework/$architecture/$mode"
            $source = Join-Path $work "source/$framework/$architecture/$mode"
            New-Item -ItemType Directory -Force $source | Out-Null
            Get-ChildItem "$root/samples/SampleWpfApp/*" -Include *.cs,*.xaml,*.csproj | Copy-Item -Destination $source
            $project = Join-Path $source 'SampleWpfApp.csproj'
            [xml]$xml = Get-Content -LiteralPath $project
            $xml.Project.ItemGroup.ProjectReference.SetAttribute('Include', $inspectorProject)
            $xml.Save($project)
            $hosting = if ($mode -eq 'SelfHosted') { 'true' } else { 'false' }
            # PlatformTarget propagates to project references. Isolate their bin/obj
            # outputs too, so an x64 case cannot reuse a cached x86 Inspector DLL.
            $sampleArtifacts = Join-Path $work "build/$framework/$architecture/$mode"
            $arguments = @('publish', $project, '-c', 'Release', "-p:SampleTargetFramework=$framework",
                "-p:PlatformTarget=$architecture", "-p:EnableInspectorSelfHosting=$hosting",
                "-p:ArtifactsPath=$sampleArtifacts", '--output', $sampleRoot, '--verbosity', 'quiet')
            if ($framework.EndsWith('-windows')) { $arguments += @('--runtime', "win-$architecture", '--self-contained', 'false') }
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
    foreach ($case in @('net8.0-windows/x64', 'net472/x86')) {
        $sample = Join-Path $work "samples/$case/AutoInjection/SampleWpfApp.exe"
        & python "$root/scripts/test_workflow.py" --server $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SERVER --sample $sample
        if ($LASTEXITCODE -ne 0) { throw "Recorded workflow/xUnit round trip failed: $case" }
    }
}
finally {
    $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SERVER = $previousServer
    $env:WPF_VISUAL_TREE_MCP_INTEGRATION_SAMPLES = $previousSamples
}
Write-Output "Matrix artifacts: $work"
