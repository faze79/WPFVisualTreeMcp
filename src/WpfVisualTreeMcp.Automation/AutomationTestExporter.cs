using WpfVisualTreeMcp.Shared;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Automation;

internal static class AutomationTestExporter
{
    internal static IReadOnlyDictionary<string, string> Export(RecordingState state, string format)
    {
        if (state.Active || state.Workflow == null) throw new InvalidOperationException("Stop a recording before exporting it.");
        WorkflowCodec.Validate(state.Workflow);
        var files = new Dictionary<string, string> { ["workflow.json"] = IpcSerializer.Serialize(state.Workflow) };
        if (format == "json") return files;
        if (format != "xunit") throw new ArgumentException("Format must be json or xunit.");
        var packageVersion = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(WpfApplication).Assembly)!
            .InformationalVersion.Split('+')[0];
        files["RecordedWorkflow.Tests.csproj"] = $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0-windows</TargetFramework>
                <IsTestProject>true</IsTestProject><IsPackable>false</IsPackable>
                <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="WpfVisualTreeMcp.Automation" Version="{{packageVersion}}" />
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
                <PackageReference Include="xunit" Version="2.6.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" PrivateAssets="all" />
                <None Update="workflow.json" CopyToOutputDirectory="PreserveNewest" />
              </ItemGroup>
            </Project>
            """;
        files["RecordedWorkflowTest.cs"] = """
            using WpfVisualTreeMcp.Automation;
            using WpfVisualTreeMcp.Shared.Ipc;
            using WpfVisualTreeMcp.Shared.Models;
            using Xunit;

            public class RecordedWorkflowTest
            {
                [Fact]
                public async Task Recorded_workflow_succeeds()
                {
                    var path = Environment.GetEnvironmentVariable("WPF_TEST_APP")
                        ?? throw new InvalidOperationException("Set WPF_TEST_APP to the full path of the application executable.");
                    // Launch waits for readiness and disposes only the process this test owns.
                    await using var app = await WpfApplication.LaunchAsync(new LaunchOptions { FileName = path });
                    var workflow = IpcSerializer.Deserialize<RecordedWorkflow>(
                        await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "workflow.json")))!;
                    var result = await app.ReplayAsync(workflow);
                    Assert.True(result.Success, $"Replay failed at step {result.FailedStep}: {result.Error}");
                }
            }
            """;
        return files;
    }
}
