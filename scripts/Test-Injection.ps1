param(
    [Parameter(Mandatory)][string]$ServerDirectory,
    [ValidateSet('net472', 'net48', 'net481')][string[]]$Frameworks = @('net472', 'net48', 'net481')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
foreach ($framework in $Frameworks) {
  foreach ($architecture in @('x64', 'x86')) {
    $sampleDirectory = Join-Path $root "TestResults/injection-sample/$framework/$architecture"
    $sampleSource = Join-Path $sampleDirectory 'src'
    New-Item -ItemType Directory -Force $sampleSource | Out-Null
    Get-ChildItem "$root/samples/SampleWpfApp/*" -Include *.cs,*.xaml,*.csproj | Copy-Item -Destination $sampleSource
    dotnet build "$sampleSource/SampleWpfApp.csproj" -c Release "/p:SampleTargetFramework=$framework" "/p:PlatformTarget=$architecture" "/p:EnableInspectorSelfHosting=false" --output "$sampleDirectory/bin" --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "Sample build failed: $architecture" }
    python "$PSScriptRoot/test_injection.py" --server $ServerDirectory --sample "$sampleDirectory/bin/SampleWpfApp.exe" --architecture $architecture
    if ($LASTEXITCODE -ne 0) { throw "Injection smoke test failed: $architecture" }
  }
}
