using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WpfVisualTreeMcp.Server.Cli;

internal sealed record UpdateInstallation(string Kind, string Directory, string? UnsupportedReason = null)
{
    internal static UpdateInstallation Detect(string baseDirectory)
    {
        var directory = new DirectoryInfo(baseDirectory);
        for (var ancestor = directory; ancestor != null; ancestor = ancestor.Parent)
        {
            // Global tools and --tool-path installs both use <tool root>/.store/<package>/...
            if (ancestor.Name.Equals(".store", StringComparison.OrdinalIgnoreCase) &&
                Path.GetRelativePath(ancestor.FullName, directory.FullName).Split(Path.DirectorySeparatorChar)[0]
                    .Equals("wpfvisualtreemcp", StringComparison.OrdinalIgnoreCase))
                return new("dotnet-tool", ancestor.Parent!.FullName);

            if (ancestor.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) && ancestor.Parent != null &&
                File.Exists(Path.Combine(ancestor.Parent.FullName, "WpfVisualTreeMcp.Server.csproj")))
                return new("source-build", directory.FullName,
                    "This executable is a source build. Update the checkout and rebuild/publish it, or use a dotnet tool installation.");
        }

        // A local manifest tool or dnx runs from a shared NuGet cache: never overwrite that cache.
        if (File.Exists(Path.Combine(directory.FullName, "DotnetToolSettings.xml")))
            return new("package-cache", directory.FullName,
                "This executable runs from a package cache (local tool or dnx). For a local tool, run 'dotnet tool update WpfVisualTreeMcp' in its manifest directory; for dnx, select the latest package version.");

        return new("portable", directory.FullName);
    }
}

internal sealed record UpdateRelease(string Version, string? DownloadUrl = null, string? Digest = null);

/// <summary>
/// Stages updates before handing off to an embedded, detached worker. Windows locks
/// the current executable until this process exits; scheduled is not reported as installed.
/// </summary>
internal static class SelfUpdater
{
    private const string PackageIndex = "https://api.nuget.org/v3-flatcontainer/wpfvisualtreemcp/index.json";
    private const string Releases = "https://api.github.com/repos/faze79/WPFVisualTreeMcp/releases/latest";
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static string CurrentVersion => typeof(SelfUpdater).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    internal static async Task<object> RunAsync(bool check, bool status)
    {
        if (check && status) throw new ArgumentException("Use either --check or --status, not both.");
        var installation = UpdateInstallation.Detect(AppContext.BaseDirectory);
        var stateDirectory = GetStateDirectory(installation);
        var statusPath = Path.Combine(stateDirectory, "status.json");
        if (status)
        {
            if (!File.Exists(statusPath))
                return new { status = "not-run", currentVersion = CurrentVersion, installation = installation.Kind, directory = installation.Directory };
            var last = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(statusPath));
            if (last.GetProperty("status").GetString() is "scheduled" or "running" &&
                last.TryGetProperty("updaterProcessId", out var workerId) &&
                last.TryGetProperty("updaterStartTicks", out var workerStart) &&
                !IsProcessRunning(workerId.GetInt32(), workerStart.GetInt64()))
            {
                // The worker may have written its final result between our read and exit check.
                last = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(statusPath));
                if (last.GetProperty("status").GetString() is not ("scheduled" or "running")) return last;
                return JsonSerializer.SerializeToElement(new
                {
                    status = "failed", message = "The update worker exited without recording completion. Check the log and retry.",
                    statusPath, logPath = last.GetProperty("logPath").GetString(), previousResult = last
                }, JsonOptions);
            }
            return last;
        }
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Automatic updates require Windows.");
        if (!check && installation.UnsupportedReason != null)
            throw new InvalidOperationException(installation.UnsupportedReason);

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WpfVisualTreeMcp/" + CurrentVersion);
        var release = await GetLatestAsync(client, installation.Kind == "dotnet-tool" || installation.Kind == "package-cache");
        var available = IsNewer(release.Version, CurrentVersion);
        if (check || !available)
            return new
            {
                status = available ? "update-available" : "up-to-date", currentVersion = CurrentVersion,
                latestVersion = release.Version, installation = installation.Kind, directory = installation.Directory,
                canSelfUpdate = installation.UnsupportedReason == null, note = installation.UnsupportedReason
            };

        Directory.CreateDirectory(stateDirectory);
        // The worker holds this same lock. Concurrent updates cannot stage or apply together.
        using var updateLock = new FileStream(Path.Combine(stateDirectory, "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(statusPath))
        {
            using var previous = JsonDocument.Parse(await File.ReadAllTextAsync(statusPath));
            if (previous.RootElement.TryGetProperty("updaterProcessId", out var pid) &&
                previous.RootElement.TryGetProperty("updaterStartTicks", out var ticks))
            {
                if (IsProcessRunning(pid.GetInt32(), ticks.GetInt64()))
                    throw new InvalidOperationException("An update is already scheduled. Run 'update --status'.");
            }
        }
        var workDirectory = Path.Combine(stateDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        string? payloadDirectory = null;
        string? dotnet = null;
        if (installation.Kind == "dotnet-tool")
        {
            dotnet = await FindSdkAsync();
            await StageToolPackageAsync(client, release.Version, workDirectory);
        }
        else
        {
            Console.Error.WriteLine($"Downloading WpfVisualTreeMcp {release.Version}...");
            var archive = Path.Combine(workDirectory, "release.zip");
            await DownloadAsync(client, release.DownloadUrl!, archive);
            payloadDirectory = Path.Combine(workDirectory, "payload");
            ExtractServer(archive, payloadDirectory, release.Digest!);
        }

        var worker = Path.Combine(workDirectory, "UpdateWorker.ps1");
        await using (var resource = typeof(SelfUpdater).Assembly.GetManifestResourceStream("WpfVisualTreeMcp.UpdateWorker.ps1")!)
        await using (var output = File.Create(worker))
            await resource.CopyToAsync(output);

        var planPath = Path.Combine(workDirectory, "plan.json");
        var plan = new
        {
            parentId = Environment.ProcessId, parentStartTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
            kind = installation.Kind, targetDirectory = installation.Directory,
            currentVersion = CurrentVersion, version = release.Version, payloadDirectory, dotnet,
            workDirectory, stateDirectory, statusPath
        };
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, JsonOptions));
        var start = new ProcessStartInfo(PowerShellPath)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workDirectory,
            // Do not inherit a CLI/MCP caller's stdout pipes: the worker writes its own log.
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", worker, "-PlanFile", planPath })
            start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the update worker.");
            process.StandardInput.Close();
            var scheduled = new
            {
                status = "scheduled", currentVersion = CurrentVersion, latestVersion = release.Version,
                installation = installation.Kind, directory = installation.Directory,
                updaterProcessId = process.Id, updaterStartTicks = process.StartTime.ToUniversalTime().Ticks,
                logPath = Path.Combine(workDirectory, "update.log"), statusPath,
                message = "The updater will start after this command exits. Run 'update --status' to confirm completion. Restart the MCP client and inspected apps after updating."
            };
            await File.WriteAllTextAsync(statusPath, JsonSerializer.Serialize(scheduled, JsonOptions));
            return scheduled;
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(statusPath, JsonSerializer.Serialize(new { status = "failed", error = ex.Message }, JsonOptions));
            throw;
        }
    }

    internal static string PowerShellPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe");

    private static bool IsProcessRunning(int id, long startTicks)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == startTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static string GetStateDirectory(UpdateInstallation installation)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(installation.Directory)).ToUpperInvariant())))[..16];
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WpfVisualTreeMcp", "updates", key);
    }

    internal static Version StableVersion(string version)
    {
        if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$") || !Version.TryParse(version, out var parsed))
            throw new InvalidDataException($"Invalid stable release version: {version}");
        return parsed;
    }

    internal static bool IsNewer(string latest, string current)
    {
        var core = current.Split('+')[0];
        var version = StableVersion(core.Split('-')[0]);
        var available = StableVersion(latest);
        return available > version || (available == version && core.Contains('-'));
    }

    internal static async Task<UpdateRelease> GetLatestAsync(HttpClient client, bool tool)
    {
        using var response = await client.GetAsync(tool ? PackageIndex : Releases);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (tool)
        {
            var latest = json.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()!)
                .Where(v => Regex.IsMatch(v, @"^\d+\.\d+\.\d+$")).OrderByDescending(StableVersion).FirstOrDefault()
                ?? throw new InvalidDataException("No stable NuGet version is available.");
            return new(latest);
        }
        var root = json.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("The release is not stable.");
        var tag = root.GetProperty("tag_name").GetString()!;
        var version = tag.StartsWith('v') ? tag[1..] : tag;
        StableVersion(version);
        var name = $"WpfVisualTreeMcp-v{version}-win-x64.zip";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != name) continue;
            var url = asset.GetProperty("browser_download_url").GetString()!;
            var expectedUrl = $"https://github.com/faze79/WPFVisualTreeMcp/releases/download/{tag}/{name}";
            if (!string.Equals(url, expectedUrl, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected release download URL.");
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            if (digest == null || !Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$"))
                throw new InvalidDataException("The release asset has no SHA-256 digest. Download it manually from GitHub Releases.");
            return new(version, url, digest);
        }
        throw new InvalidDataException($"The release does not contain {name}.");
    }

    internal static async Task StageToolPackageAsync(HttpClient client, string version, string workDirectory,
        Func<TimeSpan, Task>? delay = null)
    {
        StableVersion(version);
        delay ??= Task.Delay;
        var url = $"https://api.nuget.org/v3-flatcontainer/wpfvisualtreemcp/{version}/wpfvisualtreemcp.{version}.nupkg";
        var package = Path.Combine(workDirectory, $"WpfVisualTreeMcp.{version}.nupkg");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Console.Error.WriteLine($"Downloading WpfVisualTreeMcp {version} from NuGet (attempt {attempt}/4)...");
                await DownloadAsync(client, url, package);
                break;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is null or HttpStatusCode.NotFound or
                HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or
                HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
            {
                if (attempt == 4)
                    throw new HttpRequestException($"NuGet lists version {version}, but its package could not be downloaded after four attempts. " +
                        "The feed may still be propagating or temporarily unavailable. Retry later; no installed files were changed.", ex, ex.StatusCode);
                await delay(TimeSpan.FromSeconds(attempt * 10));
            }
        }

        // Install the exact staged package, avoiding a second version lookup through
        // NuGet's registration services (which can lag behind the flat-container index).
        var localFeed = System.Security.SecurityElement.Escape(Path.GetFullPath(workDirectory));
        await File.WriteAllTextAsync(Path.Combine(workDirectory, "NuGet.Config"),
            $"<configuration><packageSources><clear/><add key=\"staged-official-package\" value=\"{localFeed}\"/></packageSources></configuration>");
    }

    private static async Task DownloadAsync(HttpClient client, string url, string destination)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(destination);
        var buffer = new byte[81920];
        long total = 0;
        int length;
        while ((length = await input.ReadAsync(buffer, timeout.Token)) != 0)
        {
            total += length;
            if (total > 256 * 1024 * 1024) throw new InvalidDataException("Release download exceeds 256 MiB.");
            await output.WriteAsync(buffer.AsMemory(0, length), timeout.Token);
        }
    }

    internal static void ExtractServer(string archivePath, string destination, string digest)
    {
        using (var input = File.OpenRead(archivePath))
        {
            var actual = "sha256:" + Convert.ToHexString(SHA256.HashData(input));
            if (!string.Equals(actual, digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Release checksum mismatch. No installed files were changed.");
        }
        using var archive = ZipFile.OpenRead(archivePath);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        var entries = new List<(ZipArchiveEntry Entry, string Path)>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long size = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(p => p is "." or ".."))
                throw new InvalidDataException("Unsafe path in release archive.");
            if (!name.StartsWith("server/", StringComparison.Ordinal) || name.EndsWith('/')) continue;
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Links are not supported in release archives.");
            var path = Path.GetFullPath(Path.Combine(root, name[7..]));
            size += entry.Length;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !paths.Add(path) ||
                size > 512 * 1024 * 1024 || entries.Count >= 10000)
                throw new InvalidDataException("Invalid or oversized release archive.");
            entries.Add((entry, path));
        }
        foreach (var required in new[] { "WpfVisualTreeMcp.Server.exe", "WpfVisualTreeMcp.Server.dll", "WpfVisualTreeMcp.Server.deps.json", "WpfVisualTreeMcp.Server.runtimeconfig.json", "native/x64/WpfInspectorBootstrapper.dll", "native/x86/WpfInspectorBootstrapper.dll" })
            if (!paths.Contains(Path.GetFullPath(Path.Combine(root, required))))
                throw new InvalidDataException($"Release payload is missing {required}.");
        foreach (var (entry, path) in entries)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path);
        }
    }

    private static async Task<string> FindSdkAsync()
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrEmpty(dotnet) || !File.Exists(dotnet)) dotnet = "dotnet";
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetTempPath()
        };
        start.ArgumentList.Add("--list-sdks");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start dotnet.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await stderr;
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(await stdout))
            throw new InvalidOperationException("Updating a dotnet tool requires a .NET SDK. Install .NET SDK 8 or later and retry.");
        return dotnet;
    }
}
