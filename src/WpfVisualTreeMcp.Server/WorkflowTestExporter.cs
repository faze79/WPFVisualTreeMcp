using WpfVisualTreeMcp.Shared;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Server;

public static class WorkflowTestExporter
{
    public static Dictionary<string, string> Export(RecordingState state, string format)
    {
        if (state.Active) throw new InvalidOperationException("Stop recording before exporting.");
        if (state.Workflow == null) throw new InvalidOperationException("No recorded workflow to export.");
        WorkflowCodec.Validate(state.Workflow);
        var files = new Dictionary<string, string> { ["workflow.json"] = IpcSerializer.Serialize(state.Workflow) };
        if (format == "json") return files;
        if (format != "xunit") throw new ArgumentException("Export format must be xunit or json.");
        files["RecordedWorkflow.Tests.csproj"] = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0-windows</TargetFramework>
                <IsTestProject>true</IsTestProject>
                <IsPackable>false</IsPackable>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
                <PackageReference Include="xunit" Version="2.6.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
                <None Update="workflow.json" CopyToOutputDirectory="PreserveNewest" />
              </ItemGroup>
            </Project>
            """;
        files["WorkflowTests.cs"] = """
            using System.Diagnostics;
            using System.Text.Json;
            using Xunit;

            public class WorkflowTests
            {
                [Fact]
                public async Task Recorded_workflow_succeeds_on_a_fresh_application()
                {
                    string appPath = Required("WPF_TEST_APP");
                    string cliPath = Required("WPFINSPECT_PATH");
                    var startup = new ProcessStartInfo(appPath) { UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(appPath))! };
                    var arguments = Environment.GetEnvironmentVariable("WPF_TEST_APP_ARGS_JSON");
                    if (arguments != null)
                        foreach (string argument in JsonSerializer.Deserialize<string[]>(arguments)!) startup.ArgumentList.Add(argument);
                    // The test runner's DOTNET_ROOT may be x64; honor the optional x86 override.
                    startup.Environment["DOTNET_ROOT_X86"] = Environment.GetEnvironmentVariable("DOTNET_ROOT_X86")
                        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet");
                    using var app = Process.Start(startup) ?? throw new InvalidOperationException("Cannot start test app.");
                    try
                    {
                        string pid = app.Id.ToString();
                        var ready = Stopwatch.StartNew();
                        while (true)
                        {
                            app.Refresh();
                            Assert.False(app.HasExited, "Test application exited before attachment.");
                            if (app.MainWindowHandle != IntPtr.Zero) break;
                            if (ready.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("No test application window.");
                            await Task.Delay(100);
                        }
                        await Cli(cliPath, "attach", "--pid", pid, "--auto-inject");
                        var replay = await Cli(cliPath, "replay", "--pid", pid, "--file", Path.Combine(AppContext.BaseDirectory, "workflow.json"));
                        using var result = JsonDocument.Parse(replay);
                        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), replay);
                    }
                    finally
                    {
                        if (!app.HasExited) { app.Kill(entireProcessTree: true); await app.WaitForExitAsync(); }
                    }
                }

                private static string Required(string name) => Environment.GetEnvironmentVariable(name)
                    ?? throw new InvalidOperationException("Set " + name + " to an absolute executable path.");

                private static async Task<string> Cli(string executable, params string[] arguments)
                {
                    var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true };
                    info.Environment["DOTNET_ROOT_X86"] = Environment.GetEnvironmentVariable("DOTNET_ROOT_X86")
                        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet");
                    foreach (string argument in arguments) info.ArgumentList.Add(argument);
                    using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start CLI.");
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(140));
                    try { await process.WaitForExitAsync(timeout.Token); }
                    catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
                    string output = await stdout;
                    Assert.True(process.ExitCode == 0, output + "\n" + await stderr);
                    return output;
                }
            }
            """;
        files["README.md"] = """
            # Recorded WPF workflow test

            Install .NET 8 SDK and WpfVisualTreeMcp CLI with workflow support. In PowerShell:

            ```powershell
            $env:WPF_TEST_APP = 'C:\path\MyApp.exe'
            $env:WPFINSPECT_PATH = 'C:\path\WpfVisualTreeMcp.Server.exe' # or absolute wpfinspect.exe path
            dotnet test
            ```

            Optional `WPF_TEST_APP_ARGS_JSON` is a JSON array of application arguments.
            Each test starts a fresh app and terminates only that app and its children.
            Close other test runs that could overlap physical mouse/touch/keyboard input.
            Physical actions need an unlocked interactive desktop. Native touch needs app support.
            Review workflow.json before running: actions change application state and recorded
            input values are plain text. Add explicit AssertElement/WaitForElement steps when
            recording to verify outcomes; input delivery alone is not an assertion of app behavior.
            Selectors use exact types/identifiers and fail if ambiguous or missing. Realized items
            only; include explicit waits for asynchronous UI changes. Replay stops at the first
            failure, reports its one-based step number and does not undo earlier actions.
            """;
        return files;
    }
}
