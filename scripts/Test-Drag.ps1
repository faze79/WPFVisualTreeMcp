param(
    [string]$ServerExecutable = "$PSScriptRoot/../src/WpfVisualTreeMcp.Server/bin/Release/net8.0/WpfVisualTreeMcp.Server.exe",
    [string[]]$Configurations = @('net48-x64', 'net48-x86', 'net8.0-windows-x64'),
    [ValidateSet('drag', 'touch')][string]$TestKind = 'drag'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
foreach ($configuration in $Configurations) {
    $architecture = $configuration.Substring($configuration.LastIndexOf('-') + 1)
    $framework = $configuration.Substring(0, $configuration.LastIndexOf('-'))
    if ($architecture -notin @('x86', 'x64') -or $framework -notin @('net472', 'net48', 'net481', 'net8.0-windows', 'net9.0-windows', 'net10.0-windows')) {
        throw "Unsupported configuration: $configuration"
    }
    $sampleDirectory = Join-Path $root "TestResults/$TestKind/$configuration"
    $sampleSource = Join-Path $sampleDirectory 'src'
    New-Item -ItemType Directory -Force $sampleSource | Out-Null
    Get-ChildItem "$root/samples/SampleWpfApp/*" -Include *.cs,*.xaml | Copy-Item -Destination $sampleSource
    $inspectorProject = [System.Security.SecurityElement]::Escape("$root/src/WpfVisualTreeMcp.Inspector/WpfVisualTreeMcp.Inspector.csproj")
    $runtimeProperties = if ($framework.EndsWith('-windows')) {
        "<RuntimeIdentifier>win-$architecture</RuntimeIdentifier><SelfContained>false</SelfContained>"
    } else { '' }
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType><TargetFramework>$framework</TargetFramework>
    <UseWPF>true</UseWPF><LangVersion>12.0</LangVersion><Nullable>enable</Nullable>
    <AssemblyName>SampleWpfApp</AssemblyName><PlatformTarget>$architecture</PlatformTarget>
    <DefineConstants>INSPECTOR_SELF_HOST</DefineConstants>
    $runtimeProperties
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="$inspectorProject" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath "$sampleSource/DragSample.csproj" -Encoding utf8
    dotnet build "$sampleSource/DragSample.csproj" -c Release --output "$sampleDirectory/bin" --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "Drag sample build failed: $configuration" }
    python "$PSScriptRoot/test_$TestKind.py" --server $ServerExecutable --sample "$sampleDirectory/bin/SampleWpfApp.exe" --configuration $configuration
    if ($LASTEXITCODE -ne 0) { throw "Drag smoke test failed: $configuration" }
}
