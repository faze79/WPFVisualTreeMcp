using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace WpfVisualTreeMcp.IntegrationTests;

public class InspectionModeMatrixTests
{
    private const uint Th32csSnapModule = 0x00000008;
    private const uint Th32csSnapModule32 = 0x00000010;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public static TheoryData<string, string, string> Cases => new()
    {
        { "net472", "x86", "SelfHosted" },
        { "net472", "x86", "AutoInjection" },
        { "net472", "x64", "SelfHosted" },
        { "net472", "x64", "AutoInjection" },
        { "net48", "x86", "SelfHosted" },
        { "net48", "x86", "AutoInjection" },
        { "net48", "x64", "SelfHosted" },
        { "net48", "x64", "AutoInjection" },
        { "net8.0-windows", "x86", "SelfHosted" },
        { "net8.0-windows", "x86", "AutoInjection" },
        { "net8.0-windows", "x64", "SelfHosted" },
        { "net8.0-windows", "x64", "AutoInjection" },
    };

    [IntegrationTheory]
    [MemberData(nameof(Cases))]
    [Trait("Category", "Integration")]
    public async Task Cli_inspects_and_captures_full_content_for_target_architecture_and_mode(
        string targetFramework,
        string architecture,
        string mode)
    {
        var samplePath = Path.Combine(
            GetRequiredEnvironmentVariable("WPF_VISUAL_TREE_MCP_INTEGRATION_SAMPLES"),
            targetFramework,
            architecture,
            "SampleWpfApp.exe");
        File.Exists(samplePath).Should().BeTrue("the integration runner should publish every sample matrix variant");

        var screenshotDirectory = Path.Combine(
            Path.GetTempPath(),
            $"WpfVisualTreeMcp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(screenshotDirectory);
        using var sample = StartSample(samplePath, mode);
        try
        {
            await WaitForMainWindowAsync(sample, TimeSpan.FromSeconds(30));

            GetProcessArchitecture(sample).Should().Be(architecture);
            var runtimeModule = targetFramework == "net8.0-windows" ? "coreclr.dll" : "clr.dll";
            await WaitForModuleAsync(sample.Id, runtimeModule, present: true, TimeSpan.FromSeconds(10));

            var autoInject = mode == "AutoInjection";
            if (autoInject)
            {
                var beforeInjection = await RunCliAsync(
                    new[]
                    {
                        "find",
                        "--pid",
                        sample.Id.ToString(),
                        "--name",
                        "SubmitButton",
                        "--compact",
                    },
                    TimeSpan.FromSeconds(8));
                beforeInjection.ExitCode.Should().NotBe(
                    0,
                    "the auto-injection case must not be inspectable before attach --auto-inject");
            }

            var attachArguments = new List<string>
            {
                "attach",
                "--pid",
                sample.Id.ToString(),
                "--compact",
            };
            if (autoInject)
                attachArguments.Add("--auto-inject");

            var attach = await RunCliAsync(attachArguments, TimeSpan.FromSeconds(20));
            attach.ExitCode.Should().Be(0, FormatCommandFailure("attach", attach));

            using (var attachJson = JsonDocument.Parse(attach.StandardOutput))
            {
                attachJson.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
                attachJson.RootElement.GetProperty("processId").GetInt32().Should().Be(sample.Id);
                attachJson.RootElement.GetProperty("inspectorStatus").GetString()
                    .Should().Be(autoInject ? "Loaded (injected)" : "Loaded (existing)");
            }

            if (autoInject)
            {
                var secondAttach = await RunCliAsync(attachArguments, TimeSpan.FromSeconds(20));
                secondAttach.ExitCode.Should().Be(0, FormatCommandFailure("second attach", secondAttach));

                using var secondAttachJson = JsonDocument.Parse(secondAttach.StandardOutput);
                secondAttachJson.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
                secondAttachJson.RootElement.GetProperty("processId").GetInt32().Should().Be(sample.Id);
                secondAttachJson.RootElement.GetProperty("inspectorStatus").GetString()
                    .Should().Be("Loaded (existing)",
                        "the second attach should reuse the loaded Inspector without reinjecting it");
            }

            var find = await FindSubmitButtonAsync(sample.Id, TimeSpan.FromSeconds(15));
            find.ExitCode.Should().Be(0, FormatCommandFailure("find", find));

            using var findJson = JsonDocument.Parse(find.StandardOutput);
            var elements = findJson.RootElement.GetProperty("elements");
            elements.GetArrayLength().Should().Be(1);
            var submitButton = elements[0];
            submitButton.GetProperty("name").GetString().Should().Be("SubmitButton");
            submitButton.GetProperty("typeName").GetString().Should().Be("System.Windows.Controls.Button");
            submitButton.GetProperty("handle").GetString().Should().StartWith("elem_");
            findJson.RootElement.GetProperty("count").GetInt32().Should().Be(1);

            var listBoxHandle = await FindElementHandleAsync(
                sample.Id, "ItemsListBox", TimeSpan.FromSeconds(15));
            var select = await RunCliAsync(
                new[]
                {
                    "select-item",
                    "--pid",
                    sample.Id.ToString(),
                    "--handle",
                    listBoxHandle,
                    "--index",
                    "39",
                    "--compact",
                },
                TimeSpan.FromSeconds(15));
            select.ExitCode.Should().Be(0, FormatCommandFailure("select-item", select));

            var beforePath = Path.Combine(screenshotDirectory, "before.png");
            var fullPath = Path.Combine(screenshotDirectory, "full.png");
            var afterPath = Path.Combine(screenshotDirectory, "after.png");
            var beforeCapture = await CaptureScreenshotAsync(sample.Id, listBoxHandle, beforePath, false);
            var fullCapture = await CaptureScreenshotAsync(sample.Id, listBoxHandle, fullPath, true);
            var afterCapture = await CaptureScreenshotAsync(sample.Id, listBoxHandle, afterPath, false);

            fullCapture.height.Should().BeGreaterThan(
                beforeCapture.height,
                "full-content capture should include virtualized items outside the ListBox viewport");
            afterCapture.Should().Be(beforeCapture);
            File.ReadAllBytes(afterPath).Should().Equal(
                File.ReadAllBytes(beforePath),
                "full-content capture should restore the original scroll position");

            if (targetFramework == "net8.0-windows" && architecture == "x64" && mode == "SelfHosted")
            {
                var scrollViewerHandle = await FindElementHandleAsync(
                    sample.Id, "ScreenshotScrollViewer", TimeSpan.FromSeconds(15));
                var viewportCapture = await CaptureScreenshotAsync(
                    sample.Id,
                    scrollViewerHandle,
                    Path.Combine(screenshotDirectory, "direct-viewport.png"),
                    false);
                var directCapture = await CaptureScreenshotAsync(
                    sample.Id,
                    scrollViewerHandle,
                    Path.Combine(screenshotDirectory, "direct-full.png"),
                    true);
                directCapture.height.Should().BeGreaterThan(
                    viewportCapture.height,
                    "non-virtualized ScrollViewer content should be rendered beyond the viewport");
            }
        }
        finally
        {
            await StopProcessAsync(sample);
            if (Directory.Exists(screenshotDirectory))
                Directory.Delete(screenshotDirectory, true);
        }
    }

    private static Process StartSample(string samplePath, string mode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = samplePath,
            UseShellExecute = false,
        };
        startInfo.Environment["WPF_VISUAL_TREE_MCP_SELF_HOSTED"] =
            mode == "SelfHosted" ? "true" : "false";

        return Process.Start(startInfo) ?? throw new XunitException($"Failed to start {samplePath}.");
    }

    private static async Task WaitForMainWindowAsync(Process process, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (process.HasExited)
                throw new XunitException($"Sample process exited with code {process.ExitCode} before creating a window.");
            if (process.MainWindowHandle != IntPtr.Zero)
                return;
            await Task.Delay(100);
        }

        throw new XunitException($"Sample process {process.Id} did not create a main window within {timeout}.");
    }

    private static async Task WaitForModuleAsync(int processId, string moduleName, bool present, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (GetModuleNames(processId).Contains(moduleName) == present)
                return;
            await Task.Delay(100);
        }

        var expectation = present ? "load" : "unload";
        throw new XunitException($"Process {processId} did not {expectation} {moduleName} within {timeout}.");
    }

    private static string GetProcessArchitecture(Process process)
    {
        if (!Environment.Is64BitOperatingSystem)
            return "x86";
        if (!IsWow64Process(process.Handle, out var isWow64))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return isWow64 ? "x86" : "x64";
    }

    private static HashSet<string> GetModuleNames(int processId)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var snapshot = CreateToolhelp32Snapshot(Th32csSnapModule | Th32csSnapModule32, (uint)processId);
            if (snapshot == InvalidHandleValue)
            {
                if (Marshal.GetLastWin32Error() == 24)
                    continue;
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var entry = new ModuleEntry32 { Size = (uint)Marshal.SizeOf<ModuleEntry32>() };
                if (!Module32First(snapshot, ref entry))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                do
                {
                    modules.Add(entry.Module);
                }
                while (Module32Next(snapshot, ref entry));

                return modules;
            }
            finally
            {
                CloseHandle(snapshot);
            }
        }

        throw new Win32Exception(24);
    }

    private static async Task<CommandResult> FindSubmitButtonAsync(int processId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        CommandResult? lastResult = null;
        while (DateTime.UtcNow < deadline)
        {
            lastResult = await RunCliAsync(
                new[]
                {
                    "find",
                    "--pid",
                    processId.ToString(),
                    "--name",
                    "SubmitButton",
                    "--compact",
                },
                TimeSpan.FromSeconds(10));

            if (lastResult.ExitCode == 0 && ContainsSubmitButton(lastResult.StandardOutput))
                return lastResult;
            await Task.Delay(250);
        }

        return lastResult ?? throw new XunitException("The CLI find command was not attempted.");
    }

    private static bool ContainsSubmitButton(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("elements").EnumerateArray().Any(element =>
                element.GetProperty("name").GetString() == "SubmitButton");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<string> FindElementHandleAsync(
        int processId, string elementName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        CommandResult? lastResult = null;
        while (DateTime.UtcNow < deadline)
        {
            lastResult = await RunCliAsync(
                new[]
                {
                    "find",
                    "--pid",
                    processId.ToString(),
                    "--name",
                    elementName,
                    "--compact",
                },
                TimeSpan.FromSeconds(10));

            if (lastResult.ExitCode == 0)
            {
                using var document = JsonDocument.Parse(lastResult.StandardOutput);
                var elements = document.RootElement.GetProperty("elements");
                if (elements.GetArrayLength() == 1)
                    return elements[0].GetProperty("handle").GetString()!;
            }
            await Task.Delay(250);
        }

        throw new XunitException(
            $"Element '{elementName}' was not found. " +
            (lastResult == null ? "The CLI find command was not attempted." : FormatCommandFailure("find", lastResult)));
    }

    private static async Task<(int width, int height)> CaptureScreenshotAsync(
        int processId, string elementHandle, string outputPath, bool fullContent)
    {
        var arguments = new List<string>
        {
            "screenshot",
            "--pid",
            processId.ToString(),
            "--handle",
            elementHandle,
            "--out",
            outputPath,
            "--max-width",
            "4000",
            "--max-height",
            "4000",
            "--compact",
        };
        if (fullContent)
            arguments.Add("--full-content");

        var result = await RunCliAsync(arguments, TimeSpan.FromSeconds(20));
        result.ExitCode.Should().Be(0, FormatCommandFailure("screenshot", result));
        File.Exists(outputPath).Should().BeTrue();

        using var document = JsonDocument.Parse(result.StandardOutput);
        return (
            document.RootElement.GetProperty("width").GetInt32(),
            document.RootElement.GetProperty("height").GetInt32());
    }

    private static async Task<CommandResult> RunCliAsync(IEnumerable<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GetRequiredEnvironmentVariable("WPF_VISUAL_TREE_MCP_INTEGRATION_SERVER"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new XunitException("Failed to start the CLI.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new XunitException($"CLI process timed out after {timeout}.");
        }

        return new CommandResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (process.HasExited)
            return;

        process.CloseMainWindow();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static string GetRequiredEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name) ??
            throw new XunitException($"Environment variable {name} is required.");
    }

    private static string FormatCommandFailure(string command, CommandResult result)
    {
        return $"{command} failed.{Environment.NewLine}" +
               $"stdout: {result.StandardOutput}{Environment.NewLine}" +
               $"stderr: {result.StandardError}";
    }

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ModuleEntry32
    {
        public uint Size;
        public uint ModuleId;
        public uint ProcessId;
        public uint GlobalUsageCount;
        public uint ProcessUsageCount;
        public IntPtr BaseAddress;
        public uint BaseSize;
        public IntPtr ModuleHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Module;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExePath;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(IntPtr processHandle, out bool wow64Process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Module32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Module32First(IntPtr snapshot, ref ModuleEntry32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Module32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Module32Next(IntPtr snapshot, ref ModuleEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
