# Integration test: the seed must contain the updater but have a version older
# than the current stable release (e.g. a test-only build with -p:Version=0.0.1).
# Installs into isolated folders, then downloads actual public stable releases.
param(
    [Parameter(Mandatory = $true)][string]$SeedServerDirectory,
    [Parameter(Mandatory = $true)][string]$SeedPackage,
    [Parameter(Mandatory = $true)][string]$SeedVersion,
    [string]$WorkDirectory = (Join-Path $PSScriptRoot "../TestResults/update-$([Guid]::NewGuid().ToString('N'))")
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $root) { throw "Use a new test directory: $root" }
[IO.Directory]::CreateDirectory($root) | Out-Null
$portable = Join-Path $root "portable with spaces & quote'"
[IO.Directory]::CreateDirectory($portable) | Out-Null
Get-ChildItem -LiteralPath $SeedServerDirectory -Force | Copy-Item -Destination $portable -Recurse
$marker = Join-Path $portable 'user-settings.json'
'preserve me' | Set-Content -LiteralPath $marker

function Invoke-UpdateAndWait([string]$executable) {
    $checkText = & $executable update --check --compact
    if ($LASTEXITCODE -ne 0) { throw "Version check failed: $checkText" }
    $check = $checkText | ConvertFrom-Json
    if ($check.status -ne 'update-available') { throw "Seed must be older than stable: $checkText" }
    $scheduledText = & $executable update --compact
    if ($LASTEXITCODE -ne 0) { throw "Scheduling failed: $scheduledText" }
    $scheduled = $scheduledText | ConvertFrom-Json
    if ($scheduled.status -ne 'scheduled') { throw "Expected scheduled: $scheduledText" }
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    do {
        Start-Sleep -Milliseconds 300
        # Status is replaced atomically; tolerate a transient sharing violation.
        try { $result = Get-Content -LiteralPath $scheduled.statusPath -Raw | ConvertFrom-Json }
        catch { continue }
        if ($result.status -eq 'failed') { throw ($result | ConvertTo-Json -Depth 5) }
        if ($result.status -eq 'updated') { return $scheduled.latestVersion }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out. Status: $($scheduled.statusPath)"
}

$latest = Invoke-UpdateAndWait (Join-Path $portable 'WpfVisualTreeMcp.Server.exe')
$installed = (Get-Item -LiteralPath (Join-Path $portable 'WpfVisualTreeMcp.Server.dll')).VersionInfo.ProductVersion.Split('+')[0]
if ($installed -ne $latest) { throw "Portable version mismatch: $installed != $latest" }
if ((Get-Content -LiteralPath $marker -Raw).Trim() -ne 'preserve me') { throw 'Personal settings changed.' }
if (-not (Test-Path -LiteralPath (Join-Path $portable 'native/x86/WpfInspectorBootstrapper.dll'))) { throw 'Native payload missing.' }
Write-Output "Portable update verified: $SeedVersion -> $latest"

$feed = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($SeedPackage))
$config = Join-Path $root 'NuGet.Config'
$escaped = [Security.SecurityElement]::Escape($feed)
"<configuration><packageSources><clear/><add key=`"test`" value=`"$escaped`"/></packageSources></configuration>" | Set-Content -LiteralPath $config
$toolPath = Join-Path $root "tool with spaces & quote'"
& dotnet tool install WpfVisualTreeMcp --tool-path $toolPath --version $SeedVersion --configfile $config
if ($LASTEXITCODE -ne 0) { throw 'Seed tool installation failed.' }
$latest = Invoke-UpdateAndWait (Join-Path $toolPath 'wpfinspect.exe')
$listing = & dotnet tool list --tool-path $toolPath
if ($LASTEXITCODE -ne 0 -or ($listing -join "`n") -notmatch [regex]::Escape($latest)) { throw 'Tool version mismatch.' }
Write-Output "Dotnet tool update verified: $SeedVersion -> $latest"
Write-Output "Artifacts retained at $root"
