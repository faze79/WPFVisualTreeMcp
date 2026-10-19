using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using WpfVisualTreeMcp.Server.Cli;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class SelfUpdaterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WpfUpdateTests-" + Guid.NewGuid().ToString("N"));

    public SelfUpdaterTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("0.14.0", "0.13.0", true)]
    [InlineData("0.14.0", "0.14.0+abc123", false)]
    [InlineData("0.13.0", "0.14.0", false)]
    [InlineData("0.14.0", "0.14.0-preview.1", true)]
    [InlineData("0.9.0", "0.10.0", false)]
    public void VersionComparison_AvoidsDowngrades(string latest, string current, bool expected) =>
        SelfUpdater.IsNewer(latest, current).Should().Be(expected);

    [Fact]
    public void DetectsCustomToolRoot_AndDoesNotTreatItAsPortable()
    {
        var tools = Path.Combine(_root, "tools with spaces & quotes'");
        var install = UpdateInstallation.Detect(Path.Combine(tools, ".store", "wpfvisualtreemcp", "0.14.0", "wpfvisualtreemcp", "0.14.0", "tools", "net8.0", "any"));
        install.Kind.Should().Be("dotnet-tool");
        install.Directory.Should().Be(tools);
    }

    [Fact]
    public void SourceBuildsAndSharedCaches_AreNotOverwritten()
    {
        File.WriteAllText(Path.Combine(_root, "WpfVisualTreeMcp.Server.csproj"), "");
        UpdateInstallation.Detect(Path.Combine(_root, "bin", "Release", "net8.0")).Kind.Should().Be("source-build");
        File.WriteAllText(Path.Combine(_root, "DotnetToolSettings.xml"), "");
        UpdateInstallation.Detect(_root).Kind.Should().Be("package-cache");
        UpdateInstallation.Detect(Path.Combine(_root, "publish")).Kind.Should().Be("portable");
    }

    [Fact]
    public async Task NugetLookup_SelectsHighestStableNumerically()
    {
        using var client = Client("""{"versions":["0.9.0","0.14.0","0.15.0-preview.1","0.13.0"]}""");
        (await SelfUpdater.GetLatestAsync(client, true)).Version.Should().Be("0.14.0");
    }

    [Theory]
    [InlineData("https://example.com/untrusted.zip", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://github.com/faze79/WPFVisualTreeMcp/releases/download/v0.14.0/WpfVisualTreeMcp-v0.14.0-win-x64.zip", null)]
    public async Task ReleaseLookup_RejectsUnexpectedUrlOrMissingDigest(string url, string? digest)
    {
        using var client = Client(JsonSerializer.Serialize(new
        {
            tag_name = "v0.14.0", draft = false, prerelease = false,
            assets = new[] { new { name = "WpfVisualTreeMcp-v0.14.0-win-x64.zip", browser_download_url = url, digest } }
        }));
        var action = () => SelfUpdater.GetLatestAsync(client, false);
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public void Extraction_ChecksDigestBeforeWritingAnything()
    {
        var archive = CreateArchive("server/test.txt");
        var destination = Path.Combine(_root, "payload");
        var action = () => SelfUpdater.ExtractServer(archive, destination, "sha256:" + new string('0', 64));
        action.Should().Throw<InvalidDataException>().WithMessage("*checksum*");
        Directory.Exists(destination).Should().BeFalse();
    }

    [Theory]
    [InlineData("server/../../outside.txt")]
    [InlineData("server/..\\outside.txt")]
    [InlineData("server/C:/outside.txt")]
    [InlineData("/outside.txt")]
    public void Extraction_RejectsTraversalBeforeWritingAnything(string path)
    {
        var archive = CreateArchive(path);
        var destination = Path.Combine(_root, "payload");
        var action = () => SelfUpdater.ExtractServer(archive, destination, Digest(archive));
        action.Should().Throw<InvalidDataException>();
        Directory.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public void Extraction_RequiresCompleteServerPayload()
    {
        var archive = CreateArchive("server/WpfVisualTreeMcp.Server.exe");
        var action = () => SelfUpdater.ExtractServer(archive, Path.Combine(_root, "payload"), Digest(archive));
        action.Should().Throw<InvalidDataException>().WithMessage("*missing*");
    }

    [Fact]
    public async Task Worker_ReplacesPortableFiles_PreservesUnrelatedFilesAndBackups()
    {
        if (!OperatingSystem.IsWindows()) return;
        var target = PrepareWorker();
        var (code, status) = await RunWorker(SelfUpdater.CurrentVersion);
        code.Should().Be(0, status.ToString());
        status.GetProperty("status").GetString().Should().Be("updated");
        File.ReadAllText(Path.Combine(target, "existing.txt")).Should().Be("new");
        File.ReadAllText(Path.Combine(target, "settings.json")).Should().Be("personal settings");
        File.ReadAllText(Path.Combine(_root, "backup", "existing.txt")).Should().Be("old");
    }

    [Fact]
    public async Task Worker_LockedFileFailsBeforeChangingInstallation()
    {
        if (!OperatingSystem.IsWindows()) return;
        var target = PrepareWorker();
        using var locked = new FileStream(Path.Combine(target, "existing.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
        var (code, status) = await RunWorker(SelfUpdater.CurrentVersion);
        code.Should().Be(1);
        status.GetProperty("status").GetString().Should().Be("failed");
        File.Exists(Path.Combine(target, "WpfVisualTreeMcp.Server.dll")).Should().BeFalse();
        locked.Dispose();
        File.ReadAllText(Path.Combine(target, "existing.txt")).Should().Be("old");
    }

    [Fact]
    public async Task Worker_VersionMismatchRollsBackChangedAndNewFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        var target = PrepareWorker();
        var (code, status) = await RunWorker("99.99.99");
        code.Should().Be(1);
        status.GetProperty("status").GetString().Should().Be("failed");
        File.ReadAllText(Path.Combine(target, "existing.txt")).Should().Be("old");
        File.Exists(Path.Combine(target, "WpfVisualTreeMcp.Server.dll")).Should().BeFalse();
        File.ReadAllText(Path.Combine(target, "settings.json")).Should().Be("personal settings");
    }

    [Theory]
    [InlineData("update")]
    [InlineData("--update")]
    public void UpdateIsCliCommand_WithoutStartingMcp(string command) => CliRunner.IsCliCommand(command).Should().BeTrue();

    private string PrepareWorker()
    {
        var target = Path.Combine(_root, "install with spaces & quote'");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "existing.txt"), "old");
        File.WriteAllText(Path.Combine(target, "settings.json"), "personal settings");
        var payload = Path.Combine(_root, "payload");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "existing.txt"), "new");
        File.Copy(typeof(CliRunner).Assembly.Location, Path.Combine(payload, "WpfVisualTreeMcp.Server.dll"));
        return target;
    }

    private async Task<(int, JsonElement)> RunWorker(string version)
    {
        var script = Path.Combine(_root, "worker.ps1");
        await using (var resource = typeof(CliRunner).Assembly.GetManifestResourceStream("WpfVisualTreeMcp.UpdateWorker.ps1")!)
        await using (var output = File.Create(script))
            await resource.CopyToAsync(output);
        var status = Path.Combine(_root, "status.json");
        var plan = Path.Combine(_root, "plan.json");
        await File.WriteAllTextAsync(plan, JsonSerializer.Serialize(new
        {
            parentId = int.MaxValue, parentStartTicks = 0, kind = "portable",
            targetDirectory = Path.Combine(_root, "install with spaces & quote'"),
            currentVersion = "0.1.0", version, payloadDirectory = Path.Combine(_root, "payload"),
            workDirectory = _root, stateDirectory = _root, statusPath = status
        }));
        var start = new ProcessStartInfo(SelfUpdater.PowerShellPath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-PlanFile", plan })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        File.Exists(status).Should().BeTrue((await stdout) + (await stderr));
        return (process.ExitCode, JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(status)));
    }

    private string CreateArchive(string path)
    {
        var archive = Path.Combine(_root, "release.zip");
        using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry(path).Open());
        writer.Write("test");
        return archive;
    }

    private static string Digest(string archive) => "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive)));
    private static HttpClient Client(string json) => new(new StubHandler(json));
    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
