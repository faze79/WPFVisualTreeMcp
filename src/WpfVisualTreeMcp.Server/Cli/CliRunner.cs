using System.Text.Json;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WpfVisualTreeMcp.Server.Services;
using WpfVisualTreeMcp.Shared.Models;
using WpfVisualTreeMcp.Shared;
using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Server.Cli;

/// <summary>
/// One-shot command-line front-end over the same services the MCP server uses
/// (<see cref="ProcessManager"/> + <see cref="NamedPipeBridge"/>).
///
/// Lets a human - or an AI agent via a plain Bash call - inspect WPF apps without
/// needing a live MCP connection. Each invocation is stateless: element handles
/// live in the Inspector inside the target process, so they stay valid across
/// separate CLI calls as long as the target app keeps running.
/// </summary>
public static class CliRunner
{
    private static readonly string[] Commands =
    {
        "update", "list", "attach", "report", "tree", "props", "find", "find-deep", "bindings",
        "binding-errors", "clear-binding-errors", "data-context", "resources",
        "styles", "watch-property", "highlight", "click", "move-mouse", "scroll", "drag", "touch", "select-item", "set-text",
        "send-keys", "wait-for", "set-property", "revert-property", "snapshot", "diff",
        "evaluate-binding", "explain-triggers", "layout", "export", "screenshot",
        "record", "get-recording", "assert", "replay", "export-test",
    };

    /// <summary>Options that never take a value (presence alone is meaningful).</summary>
    private static readonly HashSet<string> KnownFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "auto-inject", "compact", "verbose", "physical", "horizontal", "visible-only", "full-content", "all", "help", "h", "check", "status",
    };

    private static string Exe =>
        Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "WpfVisualTreeMcp.Server";

    /// <summary>
    /// True if the first process argument selects CLI mode rather than the MCP stdio server.
    /// </summary>
    public static bool IsCliCommand(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return false;
        var c = arg.TrimStart('-').ToLowerInvariant();
        return c is "help" or "h" || Commands.Contains(c);
    }

    /// <summary>Runs a single CLI command and returns the process exit code.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        var command = args[0].TrimStart('-').ToLowerInvariant();
        var cli = CliArgs.Parse(args.Skip(1).ToArray(), KnownFlags);

        if (command is "help" or "h")
        {
            PrintGeneralHelp();
            return 0;
        }
        if (cli.Flags.Contains("help") || cli.Flags.Contains("h"))
        {
            PrintCommandHelp(command);
            return 0;
        }

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(cli.Flags.Contains("verbose") ? LogLevel.Debug : LogLevel.Warning);
            // Keep stdout pure JSON: route every log line to stderr.
            builder.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        });

        var processManager = new ProcessManager(loggerFactory.CreateLogger<ProcessManager>());
        var bridge = new NamedPipeBridge(loggerFactory.CreateLogger<NamedPipeBridge>(), processManager);

        try
        {
            switch (command)
            {
                case "update":
                {
                    // Reject misspelled options rather than accidentally starting an update.
                    var allowed = new[] { "--check", "--status", "--compact", "--verbose" };
                    if (args.Skip(1).Any(a => !allowed.Contains(a, StringComparer.OrdinalIgnoreCase)))
                        throw new ArgumentException("Usage: update [--check | --status] [--compact] [--verbose]");
                    var result = await SelfUpdater.RunAsync(cli.Flags.Contains("check"), cli.Flags.Contains("status"));
                    WriteJson(result, cli);
                    return result is JsonElement state && state.GetProperty("status").GetString() == "failed" ? 1 : 0;
                }

                case "list":
                {
                    var processes = await processManager.GetWpfProcessesAsync();
                    WriteJson(new { processes }, cli);
                    break;
                }

                case "attach":
                {
                    var session = await AttachAsync(processManager, cli, cli.Flags.Contains("auto-inject"));
                    WriteJson(new
                    {
                        success = session.InspectorReady,
                        processId = session.ProcessId,
                        sessionId = session.SessionId,
                        mainWindowHandle = session.MainWindowHandle,
                        inspectorStatus = session.InspectorStatus,
                        inspectorReady = session.InspectorReady,
                    }, cli);
                    return session.InspectorReady ? 0 : 1;
                }

                case "record":
                {
                    await AttachAsync(processManager, cli, false);
                    var action = cli.GetStringOrNull("action")
                        ?? (args.Length > 1 && !args[1].StartsWith('-') ? args[1] : "status");
                    WriteJson(await bridge.RecordAsync(action, cli.GetStringOrNull("name")), cli);
                    break;
                }

                case "get-recording":
                {
                    await AttachAsync(processManager, cli, false);
                    var state = await bridge.RecordAsync("status");
                    if (cli.GetStringOrNull("out") is string path)
                    {
                        if (state.Workflow == null) throw new InvalidOperationException("No recorded workflow.");
                        using var file = new StreamWriter(new FileStream(path, FileMode.CreateNew));
                        await file.WriteAsync(IpcSerializer.Serialize(state.Workflow));
                        WriteJson(new { path = Path.GetFullPath(path), active = state.Active }, cli);
                    }
                    else WriteJson(state, cli);
                    break;
                }

                case "assert":
                {
                    await AttachAsync(processManager, cli, false);
                    var actual = await bridge.AssertElementAsync(cli.GetRequired("handle"), cli.GetRequired("property"), cli.GetRequired("expected"));
                    WriteJson(new { success = true, actualValue = actual }, cli);
                    break;
                }

                case "replay":
                {
                    var path = cli.GetRequired("file");
                    if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new ArgumentException("Workflow file exceeds 2 MiB.");
                    var workflow = IpcSerializer.Deserialize<RecordedWorkflow>(await File.ReadAllTextAsync(path))
                        ?? throw new ArgumentException("Workflow JSON is required.");
                    WorkflowCodec.Validate(workflow);
                    await AttachAsync(processManager, cli, false);
                    var result = await bridge.ReplayAsync(workflow, cli.GetInt("timeout", 120000));
                    WriteJson(result, cli);
                    return result.Success ? 0 : 1;
                }

                case "export-test":
                {
                    await AttachAsync(processManager, cli, false);
                    var files = WorkflowTestExporter.Export(await bridge.RecordAsync("status"), cli.GetString("format", "xunit"));
                    var directory = cli.GetStringOrNull("out");
                    if (directory == null) WriteJson(new { files }, cli);
                    else
                    {
                        var fullPath = Path.GetFullPath(directory);
                        if (Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any())
                            throw new ArgumentException("Export directory must be new or empty.");
                        Directory.CreateDirectory(fullPath);
                        foreach (var file in files)
                        {
                            using var writer = new StreamWriter(new FileStream(Path.Combine(fullPath, file.Key), FileMode.CreateNew));
                            await writer.WriteAsync(file.Value);
                        }
                        WriteJson(new { path = fullPath, files = files.Keys }, cli);
                    }
                    break;
                }

                case "report":
                {
                    await AttachAsync(processManager, cli, false);
                    WriteJson(await bridge.ReportAsync(cli.GetStringOrNull("root"), cli.GetInt("max", 500),
                        cli.GetInt("depth", 25), cli.GetInt("max-findings", 100)), cli);
                    break;
                }

                case "tree":
                {
                    await AttachAsync(processManager, cli, false);
                    var depth = Math.Clamp(cli.GetInt("depth", 25), 1, 100);
                    WriteJson(await bridge.GetVisualTreeAsync(cli.GetStringOrNull("root"), depth), cli);
                    break;
                }

                case "props":
                {
                    await AttachAsync(processManager, cli, false);
                    WriteJson(await bridge.GetElementPropertiesAsync(cli.GetRequired("handle")), cli);
                    break;
                }

                case "find":
                {
                    await AttachAsync(processManager, cli, false);
                    var result = await bridge.FindElementsAsync(
                        cli.GetStringOrNull("root"),
                        cli.GetStringOrNull("type"),
                        cli.GetStringOrNull("name"),
                        cli.GetStringOrNull("text"),
                        ParseFilter(cli.GetStringOrNull("filter")),
                        cli.Flags.Contains("visible-only"),
                        cli.GetInt("max", 50));
                    WriteJson(result, cli);
                    break;
                }

                case "find-deep":
                {
                    await AttachAsync(processManager, cli, false);
                    var type = cli.GetStringOrNull("type");
                    var name = cli.GetStringOrNull("name");
                    var text = cli.GetStringOrNull("text");
                    if (string.IsNullOrEmpty(type) && string.IsNullOrEmpty(name) && string.IsNullOrEmpty(text))
                        throw new ArgumentException("find-deep requires --type, --name or --text to bound the search.");
                    WriteJson(await bridge.FindElementsDeepAsync(
                        cli.GetStringOrNull("root"), type, name, text,
                        ParseFilter(cli.GetStringOrNull("filter")),
                        cli.Flags.Contains("visible-only")), cli);
                    break;
                }

                case "bindings":
                {
                    await AttachAsync(processManager, cli, false);
                    WriteJson(await bridge.GetBindingsAsync(cli.GetRequired("handle")), cli);
                    break;
                }

                case "binding-errors":
                {
                    await AttachAsync(processManager, cli, false);
                    WriteJson(await bridge.GetBindingErrorsAsync(), cli);
                    break;
                }

                case "clear-binding-errors":
                {
                    await AttachAsync(processManager, cli, false);
                    await bridge.ClearBindingErrorsAsync();
                    WriteJson(new { success = true, message = "Binding errors cleared." }, cli);
                    break;
                }

                case "data-context":
                {
                    await AttachAsync(processManager, cli, false);
                    var dc = await bridge.GetDataContextAsync(cli.GetRequired("handle"));
                    JsonNode? parsed = null;
                    if (!string.IsNullOrEmpty(dc.DataContextJson))
                    {
                        try { parsed = JsonNode.Parse(dc.DataContextJson); }
                        catch { /* not valid JSON - fall back to the raw string below */ }
                    }
                    WriteJson(new { element = dc.Element, dataContext = (object?)parsed ?? dc.DataContextJson }, cli);
                    break;
                }

                case "resources":
                {
                    await AttachAsync(processManager, cli, false);
                    var scope = cli.GetString("scope", "application");
                    var handle = cli.GetStringOrNull("handle");
                    if (scope == "element" && string.IsNullOrEmpty(handle))
                        throw new ArgumentException("--handle is required when --scope is 'element'.");
                    WriteJson(await bridge.GetResourcesAsync(scope, handle), cli);
                    break;
                }

                case "styles":
                {
                    await AttachAsync(processManager, cli, false);
                    WriteJson(await bridge.GetStylesAsync(cli.GetRequired("handle")), cli);
                    break;
                }

                case "watch-property":
                {
                    await AttachAsync(processManager, cli, false);
                    var watchId = await bridge.WatchPropertyAsync(
                        cli.GetRequired("handle"), cli.GetRequired("property"));
                    WriteJson(new
                    {
                        watchId,
                        note = "Watch registered in the Inspector. Re-run 'props' to read the current "
                             + "value; one-shot CLI invocations do not stream change events.",
                    }, cli);
                    break;
                }

                case "highlight":
                {
                    await AttachAsync(processManager, cli, false);
                    await bridge.HighlightElementAsync(cli.GetRequired("handle"), cli.GetInt("duration", 2000));
                    WriteJson(new { success = true, message = "Element highlighted." }, cli);
                    break;
                }

                case "click":
                {
                    await AttachAsync(processManager, cli, false);
                    var result = await bridge.ClickElementAsync(
                        cli.GetRequired("handle"),
                        cli.Flags.Contains("physical"),
                        cli.GetStringOrNull("click-type"), cli.GetDoubleOrNull("x"), cli.GetDoubleOrNull("y"));
                    WriteJson(new
                    {
                        success = true,
                        method = result.Method,
                        elementType = result.ElementType,
                        detail = result.Detail,
                    }, cli);
                    break;
                }

                case "move-mouse":
                case "scroll":
                {
                    var options = new MousePointerOptions
                    {
                        ElementHandle = cli.GetRequired("handle"), Action = command == "scroll" ? "scroll" : "move",
                        X = cli.GetDoubleOrNull("x"), Y = cli.GetDoubleOrNull("y"),
                        Delta = cli.GetInt("delta", 0), Horizontal = cli.Flags.Contains("horizontal"),
                        HoverMs = command == "move-mouse" ? cli.GetInt("hover-ms", 500) : 0
                    };
                    options.Validate();
                    await AttachAsync(processManager, cli, false);
                    WriteJson(await bridge.MousePointerAsync(options), cli);
                    break;
                }

                case "drag":
                {
                    var options = new DragOptions
                    {
                        ElementHandle = cli.GetRequired("handle"), TargetHandle = cli.GetStringOrNull("target"),
                        DeltaX = cli.GetDoubleOrNull("delta-x"), DeltaY = cli.GetDoubleOrNull("delta-y"),
                        StartX = cli.GetDoubleOrNull("start-x"), StartY = cli.GetDoubleOrNull("start-y"),
                        TargetX = cli.GetDoubleOrNull("target-x"), TargetY = cli.GetDoubleOrNull("target-y"),
                        Button = cli.GetString("button", "left"), DurationMs = cli.GetInt("duration", 500)
                    };
                    options.Validate();
                    await AttachAsync(processManager, cli, false);
                    var result = await bridge.DragAsync(options);
                    WriteJson(new { success = true, result, coordinateSpace = "screen_pixels" }, cli);
                    break;
                }

                case "touch":
                {
                    var options = new TouchGestureOptions
                    {
                        ElementHandle = cli.GetRequired("handle"), Gesture = cli.GetRequired("gesture"),
                        X = cli.GetDoubleOrNull("x"), Y = cli.GetDoubleOrNull("y"),
                        DeltaX = cli.GetDoubleOrNull("delta-x"), DeltaY = cli.GetDoubleOrNull("delta-y"),
                        Scale = cli.GetDoubleOrNull("scale"), AngleDegrees = cli.GetDoubleOrNull("angle"),
                        Radius = cli.GetDoubleOrNull("radius"), DurationMs = cli.GetIntOrNull("duration")
                    };
                    options.Validate();
                    await AttachAsync(processManager, cli, false);
                    var result = await bridge.TouchGestureAsync(options);
                    WriteJson(new { success = true, result, coordinateSpace = "screen_pixels" }, cli);
                    break;
                }

                case "select-item":
                {
                    await AttachAsync(processManager, cli, false);
                    var itemText = cli.GetStringOrNull("item-text");
                    var index = cli.GetIntOrNull("index");
                    if (string.IsNullOrEmpty(itemText) && index is null)
                        throw new ArgumentException("select-item requires --item-text or --index.");
                    var result = await bridge.SelectItemAsync(cli.GetRequired("handle"), itemText, index);
                    WriteJson(new
                    {
                        success = true,
                        method = result.Method,
                        elementType = result.ElementType,
                        detail = result.Detail,
                    }, cli);
                    break;
                }

                case "set-text":
                {
                    await AttachAsync(processManager, cli, false);
                    var result = await bridge.SetTextAsync(
                        cli.GetRequired("handle"),
                        cli.GetRequired("text"),
                        cli.Flags.Contains("physical"));
                    WriteJson(new
                    {
                        success = true,
                        method = result.Method,
                        elementType = result.ElementType,
                        detail = result.Detail,
                    }, cli);
                    break;
                }

                case "send-keys":
                {
                    await AttachAsync(processManager, cli, false);
                    var result = await bridge.SendKeysAsync(
                        cli.GetStringOrNull("handle"),
                        cli.GetRequired("keys"));
                    WriteJson(new
                    {
                        success = true,
                        method = result.Method,
                        elementType = result.ElementType,
                        detail = result.Detail,
                    }, cli);
                    break;
                }

                case "wait-for":
                {
                    await AttachAsync(processManager, cli, false);
                    var type = cli.GetStringOrNull("type");
                    var name = cli.GetStringOrNull("name");
                    var text = cli.GetStringOrNull("text");
                    if (string.IsNullOrEmpty(type) && string.IsNullOrEmpty(name) && string.IsNullOrEmpty(text))
                        throw new ArgumentException("wait-for requires --type, --name or --text to identify the element.");
                    WriteJson(await bridge.WaitForElementAsync(
                        cli.GetStringOrNull("root"), type, name, text,
                        cli.GetString("condition", "visible"),
                        cli.GetInt("timeout", 10000),
                        cli.GetInt("poll", 250)), cli);
                    break;
                }

                case "set-property":
                {
                    await AttachAsync(processManager, cli, false);
                    var result = await bridge.SetPropertyAsync(
                        cli.GetRequired("handle"),
                        cli.GetRequired("property"),
                        cli.GetString("value", ""));
                    WriteJson(new
                    {
                        success = true,
                        elementType = result.ElementType,
                        appliedValue = result.AppliedValue,
                        valueType = result.ValueType,
                        previousSource = result.PreviousSource,
                    }, cli);
                    break;
                }

                case "revert-property":
                {
                    await AttachAsync(processManager, cli, false);
                    var result = await bridge.RevertPropertyAsync(
                        cli.Flags.Contains("all"),
                        cli.GetStringOrNull("handle"),
                        cli.GetStringOrNull("property"));
                    WriteJson(new
                    {
                        success = true,
                        revertedCount = result.RevertedCount,
                        revertedHandle = result.RevertedHandle,
                        revertedProperty = result.RevertedProperty,
                        pendingCount = result.PendingCount,
                    }, cli);
                    break;
                }

                case "evaluate-binding":
                {
                    await AttachAsync(processManager, cli, false);
                    var result = await bridge.EvaluateBindingAsync(cli.GetRequired("handle"), cli.GetRequired("property"));
                    Console.Out.WriteLine(string.IsNullOrEmpty(result.Json) ? "{}" : result.Json!);
                    break;
                }

                case "explain-triggers":
                {
                    await AttachAsync(processManager, cli, false);
                    var result = await bridge.ExplainTriggersAsync(cli.GetRequired("handle"), cli.GetStringOrNull("property"));
                    Console.Out.WriteLine(string.IsNullOrEmpty(result.Json) ? "{}" : result.Json!);
                    break;
                }

                case "snapshot":
                {
                    await AttachAsync(processManager, cli, false);
                    var result = await bridge.SnapshotAsync(
                        cli.GetStringOrNull("handle"),
                        cli.GetStringOrNull("label"),
                        Math.Clamp(cli.GetInt("depth", 25), 1, 100));
                    WriteJson(new { success = true, label = result.Label, elementCount = result.ElementCount }, cli);
                    break;
                }

                case "diff":
                {
                    await AttachAsync(processManager, cli, false);
                    var result = await bridge.DiffAsync(cli.GetRequired("before"), cli.GetRequired("after"));
                    // Emit the diff document verbatim (it's already JSON), wrapped with counts.
                    var doc = string.IsNullOrEmpty(result.Json)
                        ? "{}"
                        : result.Json!;
                    Console.Out.WriteLine(doc);
                    break;
                }

                case "layout":
                {
                    await AttachAsync(processManager, cli, false);
                    WriteJson(await bridge.GetLayoutInfoAsync(cli.GetRequired("handle")), cli);
                    break;
                }

                case "export":
                {
                    await AttachAsync(processManager, cli, false);
                    var format = cli.GetString("format", "json").ToLowerInvariant();
                    if (format != "json" && format != "xaml")
                        throw new ArgumentException("--format must be 'json' or 'xaml'.");
                    var export = await bridge.ExportTreeAsync(cli.GetStringOrNull("handle"), format);
                    var outPath = cli.GetStringOrNull("out");
                    if (!string.IsNullOrEmpty(outPath))
                    {
                        await File.WriteAllTextAsync(outPath, export.Content);
                        WriteJson(new
                        {
                            path = Path.GetFullPath(outPath),
                            format = export.Format,
                            elementCount = export.ElementCount,
                        }, cli);
                    }
                    else
                    {
                        WriteJson(new
                        {
                            format = export.Format,
                            elementCount = export.ElementCount,
                            content = export.Content,
                        }, cli);
                    }
                    break;
                }

                case "screenshot":
                {
                    await AttachAsync(processManager, cli, false);
                    var fullContent = cli.Flags.Contains("full-content");
                    var maxW = Math.Clamp(cli.GetInt("max-width", 1920), 1, fullContent ? 16384 : 3840);
                    var maxH = Math.Clamp(cli.GetInt("max-height", 1080), 1, fullContent ? 16384 : 2160);
                    var shotMode = cli.GetString("mode", "auto").ToLowerInvariant();
                    if (shotMode != "auto" && shotMode != "render" && shotMode != "screen")
                        throw new ArgumentException("--mode must be 'auto', 'render' or 'screen'.");
                    if (fullContent && shotMode == "screen")
                        throw new ArgumentException("--full-content is supported with --mode render only.");
                    if (fullContent && shotMode == "auto") shotMode = "render";
                    var shot = await bridge.CaptureScreenshotAsync(cli.GetStringOrNull("handle"), maxW, maxH, shotMode, fullContent);
                    if (string.IsNullOrEmpty(shot.ImageBase64))
                        throw new InvalidOperationException("Screenshot capture returned no image data.");

                    var pid = processManager.CurrentSession!.ProcessId;
                    var outPath = cli.GetStringOrNull("out")
                        ?? Path.Combine(Environment.CurrentDirectory, $"wpf-screenshot-{pid}.png");
                    await File.WriteAllBytesAsync(outPath, Convert.FromBase64String(shot.ImageBase64));
                    WriteJson(new
                    {
                        path = Path.GetFullPath(outPath),
                        width = shot.Width,
                        height = shot.Height,
                        elementType = shot.ElementType ?? "Window",
                        captureMode = shot.CaptureMode,
                        detail = shot.Detail,
                    }, cli);
                    break;
                }

                default:
                    throw new ArgumentException($"Unknown command '{command}'. Run '{Exe} help' for usage.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (cli.Flags.Contains("verbose"))
                Console.Error.WriteLine(ex);
            else
                Console.Error.WriteLine($"error: {ex.Message}");

            WriteJson(new { error = ex.Message }, cli);
            return 1;
        }
    }

    /// <summary>
    /// Establishes the lightweight session every IPC command needs. With
    /// <paramref name="autoInject"/> = false this only records the target PID;
    /// with true it injects the Inspector DLL (used by the 'attach' command).
    /// </summary>
    private static Task<InspectionSession> AttachAsync(IProcessManager pm, CliArgs cli, bool autoInject)
    {
        var pid = cli.GetIntOrNull("pid");
        var process = cli.GetStringOrNull("process");
        if (pid is null && string.IsNullOrEmpty(process))
            throw new ArgumentException("Specify the target with --pid <id> or --process <name>.");
        return pm.AttachToProcessAsync(pid, process, autoInject);
    }

    private static Dictionary<string, string>? ParseFilter(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        if (JsonNode.Parse(json) is not JsonObject obj)
            throw new ArgumentException("--filter must be a JSON object, e.g. {\"Text\":\"OK\"}.");

        var dict = new Dictionary<string, string>();
        foreach (var kv in obj)
            dict[kv.Key] = kv.Value?.ToString() ?? "";
        return dict;
    }

    private static void WriteJson(object value, CliArgs cli)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = !cli.Flags.Contains("compact"),
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        Console.Out.WriteLine(JsonSerializer.Serialize(value, options));
    }

    private static void PrintGeneralHelp()
    {
        Console.Out.WriteLine($@"
WpfVisualTreeMcp CLI - inspect running WPF applications from the command line.

USAGE
  {Exe} <command> [options]
  {Exe}                       (no command) starts the MCP stdio server.

TARGETING
  Every inspection command except 'list' targets a process with either:
    --pid <id>                process id (from 'list')
    --process <name>          process name (e.g. SampleWpfApp)

GLOBAL OPTIONS
  --compact                   single-line JSON instead of indented
  --verbose                   log diagnostics to stderr
  --help, -h                  show help ('<command> --help' for one command)

COMMANDS
  update        [--check | --status]  update this installation (no PID required)
  list                              list inspectable WPF processes
  attach        --pid|--process [--auto-inject]
  tree          --pid [--root H] [--depth N]
  report        --pid [--root H] [--max N] [--depth N] [--max-findings N]
  record        start|stop|status|clear --pid [--name N]
  get-recording --pid [--out workflow.json]
  assert        --pid --handle H --property P --expected V
  replay        --pid --file workflow.json [--timeout MS]                  (changes app state)
  export-test   --pid [--format xunit|json] [--out NEW_DIRECTORY]
  props         --pid --handle H
  find          --pid [--type T] [--name N] [--text S] [--visible-only] [--root H] [--max N] [--filter JSON]
  find-deep     --pid (--type T | --name N | --text S) [--visible-only] [--root H] [--filter JSON]
  bindings      --pid --handle H
  binding-errors        --pid
  clear-binding-errors  --pid
  data-context  --pid --handle H
  resources     --pid [--scope application|element] [--handle H]
  styles        --pid --handle H
  watch-property        --pid --handle H --property P
  highlight     --pid --handle H [--duration MS]
  click         --pid --handle H [--physical] [--click-type single|double|right] [--x X --y Y]  (changes app state)
  move-mouse    --pid --handle H [--x X --y Y] [--hover-ms 0-5000]
  scroll        --pid --handle H --delta N [--horizontal] [--x X --y Y]
  drag          --pid --handle H (--target H | --delta-x X / --delta-y Y) [--duration MS] [--button left|right|middle]
  touch         --pid --handle H --gesture tap|long_press|swipe|pinch|rotate [--delta-x X] [--scale N] [--angle N]
  select-item   --pid --handle H (--item-text S | --index N)  (changes app state)
  set-text      --pid --handle H --text 'value' [--physical]  (changes app state)
  send-keys     --pid --keys 'Ctrl+S' [--handle H]            (changes app state)
  wait-for      --pid (--type T | --name N | --text S) [--condition visible|exists|enabled|hidden] [--timeout MS] [--poll MS]
  set-property  --pid --handle H --property P --value V                    (changes app state, reversible)
  revert-property --pid (--all | [--handle H] [--property P])              (undo set-property edits)
  snapshot      --pid [--handle H] [--label L] [--depth N]                 (capture state for diff)
  diff          --pid --before L1 --after L2                               (compare two snapshots)
  evaluate-binding --pid --handle H --property P                           (why does this property have its value?)
  explain-triggers --pid --handle H [--property P]                         (evaluate Style/Template triggers; attribute a value)
  layout        --pid --handle H
  export        --pid [--handle H] [--format json|xaml] [--out FILE]
  screenshot    --pid [--handle H] [--out FILE] [--max-width N] [--max-height N] [--mode auto|render|screen] [--full-content]

Element handles (elem_XXXXXXXX) come from 'tree' / 'find' and stay valid while
the target app keeps running.

TYPICAL WORKFLOW
  {Exe} list
  {Exe} attach --pid 1234 --auto-inject
  {Exe} find --pid 1234 --type Button
  {Exe} props --pid 1234 --handle elem_00000052
  {Exe} screenshot --pid 1234 --out app.png
");
    }

    private static void PrintCommandHelp(string command)
    {
        var usage = command switch
        {
            "update" => "update [--check | --status]\n"
                      + "  Update this installation to the latest stable release. No PID required.\n"
                      + "  Supports global/custom-path dotnet tools and portable publish folders.\n"
                      + "  --check: show current/latest versions without installing.\n"
                      + "  --status: show the last update result (exit 1 if it failed).\n"
                      + "  Windows applies the update after this command exits; 'scheduled' is\n"
                      + "  not completion. Check --status, then restart your MCP client and apps.\n"
                      + "  Source builds/local tool caches must be updated with their build/package workflow.",
            "list" => "list\n  List WPF processes that can be inspected. Takes no targeting options.",
            "attach" => "attach --pid <id> | --process <name> [--auto-inject]\n"
                      + "  Create a session. --auto-inject loads the Inspector DLL into a process\n"
                      + "  that does not already host it (one-time; survives for later commands).",
            "tree" => "tree --pid <id> [--root <handle>] [--depth <1-100>]\n"
                    + "  Dump the visual tree. --root zooms into a subtree; --depth defaults to 25.",
            "report" => "report --pid <id> [--root H] [--max 1-2000] [--depth 1-100] [--max-findings 1-500]\n"
                      + "  Aggregate current binding/validation errors and UI state counts.\n"
                      + "  Defaults: 500 elements, depth 25, 100 findings. Reports truncation.\n"
                      + "  Recent binding traces are process-wide history; state counts are not defects.",
            "record" => "record start|stop|status|clear --pid <id> [--name N]\n"
                      + "  Record sequential actions, finds, successful waits and assertions using stable selectors.\n"
                      + "  Storage lives in the target process. Stop/export before clear. Limit: 100 steps.",
            "get-recording" => "get-recording --pid <id> [--out workflow.json]\n"
                      + "  Return recording state or save workflow JSON to a new file.",
            "assert" => "assert --pid <id> --handle H --property P --expected V\n"
                      + "  Compare a dependency-property value exactly (invariant string; {null} for null).\n"
                      + "  Exits 1 on mismatch. Successful assertions are recorded while recording is active.",
            "replay" => "replay --pid <id> --file workflow.json [--timeout 1000-120000]\n"
                      + "  Resolve fresh selectors and execute sequentially. Stop recording first.\n"
                      + "  Exits 1 at the first failure, with its one-based step index. No rollback.\n"
                      + "  Include explicit waits/assertions; physical input requires an interactive desktop.",
            "export-test" => "export-test --pid <id> [--format xunit|json] [--out NEW_DIRECTORY]\n"
                      + "  Export a stopped recording. Default: xUnit project with workflow.json.\n"
                      + "  Set WPF_TEST_APP and WPFINSPECT_PATH to absolute executable paths, then dotnet test.\n"
                      + "  The generated test starts a fresh app and terminates only its own app after testing.",
            "props" => "props --pid <id> --handle <handle>\n  List the dependency properties of an element.",
            "find" => "find --pid <id> [--type T] [--name N] [--text S] [--visible-only] [--root H] [--max N] [--filter JSON]\n"
                    + "  Search elements (up to --max, default 50). Filters combine with AND:\n"
                    + "  --type: type name (partial match, e.g. Button)\n"
                    + "  --text: visible text content (button caption, TextBlock text, window\n"
                    + "          title, tooltip; case-insensitive substring)\n"
                    + "  --name: x:Name substring\n"
                    + "  --visible-only: exclude collapsed/hidden elements\n"
                    + "  --filter: JSON object of property=value pairs, e.g. '{\"IsEnabled\":\"True\"}'\n"
                    + "  Results include text, automationId, isVisible/isEnabled and screenBounds.\n"
                    + "  Example: find --pid 1234 --type Button --text Save --visible-only",
            "find-deep" => "find-deep --pid <id> (--type T | --name N | --text S) [--visible-only] [--root H] [--filter JSON]\n"
                         + "  Unbounded search; requires --type, --name or --text. Same filters as 'find'.",
            "bindings" => "bindings --pid <id> --handle <handle>\n  Show data bindings and their status for an element.",
            "binding-errors" => "binding-errors --pid <id>\n  List binding errors captured since the app started.",
            "clear-binding-errors" => "clear-binding-errors --pid <id>\n  Reset the captured binding-error list.",
            "data-context" => "data-context --pid <id> --handle <handle>\n"
                            + "  Inspect the DataContext and its inheritance chain.",
            "resources" => "resources --pid <id> [--scope application|element] [--handle <handle>]\n"
                         + "  Enumerate resource dictionaries. --handle required when --scope is 'element'.",
            "styles" => "styles --pid <id> --handle <handle>\n  Show applied styles and templates for an element.",
            "watch-property" => "watch-property --pid <id> --handle <handle> --property <name>\n"
                              + "  Register a property watch in the Inspector.",
            "highlight" => "highlight --pid <id> --handle <handle> [--duration <ms>]\n"
                         + "  Flash an element in the running app. --duration defaults to 2000.",
            "click" => "click --pid <id> --handle <handle> [--physical] [--click-type single|double|right] [--x X --y Y]\n"
                     + "  Click an element. Default: UI Automation invoke (buttons, checkboxes,\n"
                     + "  menu items, tabs, list items, expanders) — no cursor movement.\n"
                     + "  --x/--y: paired local WPF DIPs, imply physical input; default center.\n"
                     + "  --physical: real OS mouse click at the element (moves the cursor,\n"
                     + "  brings the window forward, auto-scrolls the element into view).\n"
                     + "  --click-type double/right: always physical; right opens context menus\n"
                     + "  (capture them with: screenshot --mode screen).\n"
                     + "  This command CHANGES application state.",
            "move-mouse" => "move-mouse --pid <id> --handle H [--x X --y Y] [--hover-ms 0-5000]\n"
                          + "  Moves the OS pointer without clicking; default hover 500 ms. Coordinates are local WPF DIPs.\n",
            "scroll" => "scroll --pid <id> --handle H --delta N [--horizontal] [--x X --y Y]\n"
                      + "  Wheel units: 120 per notch; positive up/right, negative down/left, nonzero -12000..12000.\n"
                      + "  The application may scroll, zoom or ignore input. Verify the effect.\n",
            "drag" => "drag --pid <id> --handle <handle> (--target <handle> | --delta-x <x> / --delta-y <y>)\n"
                    + "  [--start-x X --start-y Y] [--target-x X --target-y Y]\n"
                    + "  [--button left|right|middle] [--duration <100-10000 ms>]\n"
                    + "  Physical mouse drag; moves the cursor and brings the app forward.\n"
                    + "  Coordinates and deltas are local WPF DIPs; defaults to element centers.\n"
                    + "  Supply a target OR displacement; an omitted delta axis is zero.\n"
                    + "  Both endpoints must be visible and unobstructed. Result coordinates\n"
                    + "  are screen pixels. Success confirms input sent, not drop acceptance.\n"
                    + "  CHANGES application state. Example: drag --pid 123 --handle elem_1 --delta-x -80\n",
            "touch" => "touch --pid <id> --handle <handle> --gesture tap|long_press|swipe|pinch|rotate\n"
                     + "  [--x X --y Y] [--delta-x X --delta-y Y] [--scale N] [--angle N]\n"
                     + "  [--radius DIPS] [--duration MS]\n"
                     + "  Native Windows touch. swipe: nonzero delta; pinch: scale 0.1-10 except 1;\n"
                     + "  rotate: nonzero angle -360..360, positive clockwise. Local WPF DIPs.\n"
                     + "  Defaults: center, radius fitted to bounds; duration tap 100ms, hold 1000ms, others 700ms.\n"
                     + "  Duration limits: tap 50-500, long_press 500-10000, others 100-10000ms.\n"
                     + "  The path must stay visible inside the element. Requires an unlocked\n"
                     + "  Windows desktop and application touch support. CHANGES application state.\n"
                     + "  Success confirms injection; verify the application's response separately.",
            "select-item" => "select-item --pid <id> --handle <handle> (--item-text <text> | --index <n>)\n"
                           + "  Select an item in a ComboBox/ListBox/ListView/TabControl by visible\n"
                           + "  text (case-insensitive substring) or zero-based index. Works with\n"
                           + "  virtualized items and raises proper selection events — prefer this\n"
                           + "  over clicking dropdown items. On failure the error lists available\n"
                           + "  items. This command CHANGES application state.",
            "set-text" => "set-text --pid <id> --handle <handle> --text <value> [--physical]\n"
                        + "  Replace the text/value of an element (TextBox, ComboBox, ...).\n"
                        + "  Default: UI Automation IValueProvider.SetValue, with a\n"
                        + "  TextBox.Text / PasswordBox.Password / reflected-Text fallback.\n"
                        + "  --physical: focus the element and type via OS keyboard input\n"
                        + "  (clears existing text with Ctrl+A/Delete first, then types each\n"
                        + "  character). This command CHANGES application state.",
            "send-keys" => "send-keys --pid <id> --keys <combo> [--handle <handle>]\n"
                         + "  Send a keyboard shortcut to an element, or to whatever has focus\n"
                         + "  when --handle is omitted. Modifiers: Ctrl, Shift, Alt, Win.\n"
                         + "  Keys: A-Z, 0-9, F1-F12, Enter, Esc, Tab, Space, Backspace,\n"
                         + "        Delete, Insert, Home, End, PageUp, PageDown, Up/Down/Left/Right.\n"
                         + "  Examples: 'Ctrl+S', 'Ctrl+Shift+F', 'Enter', 'F5', 'Alt+F4', 'Win+R'.\n"
                         + "  This command CHANGES application state.",
            "wait-for" => "wait-for --pid <id> (--type T | --name N | --text S) [--condition visible|exists|enabled|hidden] [--timeout <ms>] [--poll <ms>]\n"
                        + "  Poll in the target app until an element matching the criteria satisfies\n"
                        + "  the condition, instead of sleep-and-retry. Conditions:\n"
                        + "    visible (default) - element exists and is on screen\n"
                        + "    exists            - in the tree even if not visible\n"
                        + "    enabled           - visible and IsEnabled=true\n"
                        + "    hidden            - no matching visible element (e.g. a spinner cleared)\n"
                        + "  --timeout defaults to 10000 (max 25000); --poll defaults to 250.\n"
                        + "  Returns matched (bool), waitedMs, and matchedHandle/elementType when found.",
            "set-property" => "set-property --pid <id> --handle <handle> --property <name> --value <value>\n"
                            + "  Live-edit a dependency property to test a UI change without rebuilding.\n"
                            + "  The value is converted to the property's type:\n"
                            + "    --property Margin --value '20,0,20,0'   (Thickness)\n"
                            + "    --property Visibility --value Collapsed\n"
                            + "    --property Background --value Red        (or '#FF0000')\n"
                            + "    --property Width --value 300\n"
                            + "    --value '{null}'                        (null)\n"
                            + "  Returns the coerced value read back and what previously held the\n"
                            + "  property (Binding/Local/Unset). Setting a bound property replaces the\n"
                            + "  binding with a local value; revert-property restores it. Pair with\n"
                            + "  'screenshot' to see the effect. CHANGES app state (reversible).",
            "revert-property" => "revert-property --pid <id> (--all | [--handle <handle>] [--property <name>])\n"
                               + "  Undo set-property edits. Default: revert the most recent edit.\n"
                               + "  --handle/--property target a specific one; --all reverts everything.\n"
                               + "  Restores the prior binding, local value, or default.",
            "snapshot" => "snapshot --pid <id> [--handle <handle>] [--label <name>] [--depth <1-100>]\n"
                        + "  Capture a subtree's state (layout, visibility, alignment, brushes, text)\n"
                        + "  under a label, to diff later. --handle sets the root (default: main\n"
                        + "  window). --label auto-generated if omitted. Pairs with 'diff'.",
            "diff" => "diff --pid <id> --before <label1> --after <label2>\n"
                    + "  Compare two snapshots and report what changed: for each changed element,\n"
                    + "  the exact properties (from → to), plus elements added/removed. Use to\n"
                    + "  measure the effect of a change:\n"
                    + "    snapshot --label before  ->  set-property ...  ->  snapshot --label after\n"
                    + "    diff --before before --after after",
            "evaluate-binding" => "evaluate-binding --pid <id> --handle <handle> --property <name>\n"
                                + "  Explain why a property has its value. Reports the value source\n"
                                + "  (Local/Style/Binding/Inherited/Default) and, for bindings, resolves\n"
                                + "  the path hop-by-hop against the source, showing each segment's value\n"
                                + "  and exactly where a broken binding fails (null intermediate, or a\n"
                                + "  property that doesn't exist on the DataContext type).",
            "explain-triggers" => "explain-triggers --pid <id> --handle <handle> [--property <name>]\n"
                                + "  Evaluate the element's Style and ControlTemplate triggers against the\n"
                                + "  current state: for each, the condition, the property's current value,\n"
                                + "  whether it matches, whether the trigger is active, and the setters it\n"
                                + "  applies. Answers 'why doesn't this trigger fire?' (its condition\n"
                                + "  property currently holds a different value). With --property, also\n"
                                + "  attribute that property's value to the style setter or active trigger\n"
                                + "  that set it (or local/inherited/default).",
            "layout" => "layout --pid <id> --handle <handle>\n  Show layout info (sizes, margin, alignment, visibility).",
            "screenshot" => "screenshot --pid <id> [--handle <handle>] [--out <file>] [--max-width N] [--max-height N] [--mode auto|render|screen] [--full-content]\n"
                           + "  Capture the window (or one element) as PNG and print the file path.\n"
                           + "  --mode auto (default): screen for native HwndHost content, render otherwise.\n"
                           + "  --mode render: off-screen re-render; rejects native content; works when covered,\n"
                           + "  but cannot see open popups/dropdowns/context menus.\n"
                           + "  --mode screen: capture the actual screen pixels (includes popups,\n"
                           + "  ComboBox dropdowns, context menus, tooltips); the window must be\n"
                           + "  visible on screen.\n"
                           + "  --full-content: with render mode, capture all content in a ScrollViewer,\n"
                           + "  including unscrolled and virtualized content; restores the scroll position.\n"
                           + "  Large full-content output is area-downscaled and memory/deadline bounded.",
            "export" => "export --pid <id> [--handle <handle>] [--format json|xaml] [--out <file>]\n"
                      + "  Export the tree. Writes to --out if given, otherwise prints content inline.",
            _ => null,
        };

        if (usage is null)
        {
            PrintGeneralHelp();
            return;
        }
        Console.Out.WriteLine($"{Exe} {usage}");
    }

    /// <summary>Minimal <c>--key value</c> / <c>--key=value</c> / <c>--flag</c> argument parser.</summary>
    private sealed class CliArgs
    {
        public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static CliArgs Parse(string[] tokens, HashSet<string> knownFlags)
        {
            var a = new CliArgs();
            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (!token.StartsWith('-')) continue; // stray positional - ignored

                var body = token.TrimStart('-');
                if (body.Length == 0) continue;

                var eq = body.IndexOf('=');
                if (eq >= 0)
                {
                    a.Options[body[..eq]] = body[(eq + 1)..];
                    continue;
                }

                if (knownFlags.Contains(body))
                {
                    a.Flags.Add(body);
                }
                else if (i + 1 < tokens.Length && (!tokens[i + 1].StartsWith('-') ||
                    double.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
                {
                    a.Options[body] = tokens[++i];
                }
                else
                {
                    a.Flags.Add(body); // an option given without a value
                }
            }
            return a;
        }

        public string? GetStringOrNull(string name) => Options.TryGetValue(name, out var v) ? v : null;

        public string GetString(string name, string fallback) => GetStringOrNull(name) ?? fallback;

        public string GetRequired(string name) =>
            GetStringOrNull(name) ?? throw new ArgumentException($"--{name} is required.");

        public int? GetIntOrNull(string name)
        {
            var v = GetStringOrNull(name);
            if (v is null) return null;
            if (int.TryParse(v, out var n)) return n;
            throw new ArgumentException($"--{name} must be an integer (got '{v}').");
        }

        public int GetInt(string name, int fallback) => GetIntOrNull(name) ?? fallback;

        public double? GetDoubleOrNull(string name)
        {
            var value = GetStringOrNull(name);
            if (value == null && !Flags.Contains(name)) return null;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
            throw new ArgumentException($"--{name} must be a number using '.' as the decimal separator.");
        }
    }
}
