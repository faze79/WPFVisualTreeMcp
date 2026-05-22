using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WpfVisualTreeMcp.Server.Services;

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
        "list", "attach", "tree", "props", "find", "find-deep", "bindings",
        "binding-errors", "clear-binding-errors", "data-context", "resources",
        "styles", "watch-property", "highlight", "click", "layout", "export",
        "screenshot",
    };

    /// <summary>Options that never take a value (presence alone is meaningful).</summary>
    private static readonly HashSet<string> KnownFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "auto-inject", "compact", "verbose", "physical", "help", "h",
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
                        success = true,
                        processId = session.ProcessId,
                        sessionId = session.SessionId,
                        mainWindowHandle = session.MainWindowHandle,
                        inspectorStatus = session.InspectorStatus,
                    }, cli);
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
                        ParseFilter(cli.GetStringOrNull("filter")),
                        cli.GetInt("max", 50));
                    WriteJson(result, cli);
                    break;
                }

                case "find-deep":
                {
                    await AttachAsync(processManager, cli, false);
                    var type = cli.GetStringOrNull("type");
                    var name = cli.GetStringOrNull("name");
                    if (string.IsNullOrEmpty(type) && string.IsNullOrEmpty(name))
                        throw new ArgumentException("find-deep requires --type or --name to bound the search.");
                    WriteJson(await bridge.FindElementsDeepAsync(cli.GetStringOrNull("root"), type, name), cli);
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
                        cli.GetRequired("handle"), cli.Flags.Contains("physical"));
                    WriteJson(new
                    {
                        success = true,
                        method = result.Method,
                        elementType = result.ElementType,
                        detail = result.Detail,
                    }, cli);
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
                    var maxW = Math.Clamp(cli.GetInt("max-width", 1920), 1, 3840);
                    var maxH = Math.Clamp(cli.GetInt("max-height", 1080), 1, 2160);
                    var shot = await bridge.CaptureScreenshotAsync(cli.GetStringOrNull("handle"), maxW, maxH);
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
  Every command except 'list' targets a process with either:
    --pid <id>                process id (from 'list')
    --process <name>          process name (e.g. SampleWpfApp)

GLOBAL OPTIONS
  --compact                   single-line JSON instead of indented
  --verbose                   log diagnostics to stderr
  --help, -h                  show help ('<command> --help' for one command)

COMMANDS
  list                              list inspectable WPF processes
  attach        --pid|--process [--auto-inject]
  tree          --pid [--root H] [--depth N]
  props         --pid --handle H
  find          --pid [--type T] [--name N] [--root H] [--max N] [--filter JSON]
  find-deep     --pid (--type T | --name N) [--root H]
  bindings      --pid --handle H
  binding-errors        --pid
  clear-binding-errors  --pid
  data-context  --pid --handle H
  resources     --pid [--scope application|element] [--handle H]
  styles        --pid --handle H
  watch-property        --pid --handle H --property P
  highlight     --pid --handle H [--duration MS]
  click         --pid --handle H [--physical]      (changes app state)
  layout        --pid --handle H
  export        --pid [--handle H] [--format json|xaml] [--out FILE]
  screenshot    --pid [--handle H] [--out FILE] [--max-width N] [--max-height N]

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
            "list" => "list\n  List WPF processes that can be inspected. Takes no targeting options.",
            "attach" => "attach --pid <id> | --process <name> [--auto-inject]\n"
                      + "  Create a session. --auto-inject loads the Inspector DLL into a process\n"
                      + "  that does not already host it (one-time; survives for later commands).",
            "tree" => "tree --pid <id> [--root <handle>] [--depth <1-100>]\n"
                    + "  Dump the visual tree. --root zooms into a subtree; --depth defaults to 25.",
            "props" => "props --pid <id> --handle <handle>\n  List the dependency properties of an element.",
            "find" => "find --pid <id> [--type T] [--name N] [--root H] [--max N] [--filter JSON]\n"
                    + "  Search elements (up to --max, default 50). --filter is a JSON object of\n"
                    + "  property=value pairs, e.g. --filter '{\"Text\":\"OK\"}'.",
            "find-deep" => "find-deep --pid <id> (--type T | --name N) [--root H]\n"
                         + "  Unbounded search; requires --type or --name.",
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
            "click" => "click --pid <id> --handle <handle> [--physical]\n"
                     + "  Click an element. Default: UI Automation invoke (buttons, checkboxes,\n"
                     + "  menu items, tabs, list items, expanders) — no cursor movement.\n"
                     + "  --physical: real OS mouse click at the element (moves the cursor and\n"
                     + "  brings the window forward). This command CHANGES application state.",
            "layout" => "layout --pid <id> --handle <handle>\n  Show layout info (sizes, margin, alignment, visibility).",
            "export" => "export --pid <id> [--handle <handle>] [--format json|xaml] [--out <file>]\n"
                      + "  Export the tree. Writes to --out if given, otherwise prints content inline.",
            "screenshot" => "screenshot --pid <id> [--handle <handle>] [--out <file>] [--max-width N] [--max-height N]\n"
                          + "  Capture the window (or an element) to a PNG file and print its path.",
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
                else if (i + 1 < tokens.Length && !tokens[i + 1].StartsWith('-'))
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
    }
}
