using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Server.Services;

/// <summary>
/// Implementation of IPC bridge using named pipes.
/// Communicates with the Inspector DLL injected into the target WPF process.
/// </summary>
public class NamedPipeBridge : IIpcBridge
{
    private readonly ILogger<NamedPipeBridge> _logger;
    private readonly IProcessManager _processManager;
    private readonly JsonSerializerOptions _jsonOptions;

    public NamedPipeBridge(ILogger<NamedPipeBridge> logger, IProcessManager processManager)
    {
        _logger = logger;
        _processManager = processManager;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<VisualTreeResult> GetVisualTreeAsync(string? rootHandle, int maxDepth)
    {
        EnsureConnected();

        // TODO: When inspector DLL is injected, send request over named pipe
        // For now, return a stub response

        _logger.LogDebug("GetVisualTree called with rootHandle={RootHandle}, maxDepth={MaxDepth}",
            rootHandle, maxDepth);

        // Return stub data for development
        return new VisualTreeResult
        {
            Root = new VisualTreeNode
            {
                Handle = rootHandle ?? "window_main",
                TypeName = "System.Windows.Window",
                Name = "MainWindow",
                Children = new List<VisualTreeNode>
                {
                    new()
                    {
                        Handle = "elem_grid_1",
                        TypeName = "System.Windows.Controls.Grid",
                        Name = "LayoutRoot",
                        Children = new List<VisualTreeNode>()
                    }
                }
            },
            TotalElements = 2,
            MaxDepthReached = false
        };
    }

    public async Task<ElementPropertiesResult> GetElementPropertiesAsync(string elementHandle)
    {
        EnsureConnected();

        _logger.LogDebug("GetElementProperties called for {ElementHandle}", elementHandle);

        // Return stub data
        return new ElementPropertiesResult
        {
            Element = new ElementInfo
            {
                Handle = elementHandle,
                TypeName = "System.Windows.Controls.Button"
            },
            Properties = new List<PropertyInfo>
            {
                new() { Name = "Content", TypeName = "System.String", Value = "Click Me", Source = "Local" },
                new() { Name = "IsEnabled", TypeName = "System.Boolean", Value = "True", Source = "Default" },
                new() { Name = "Width", TypeName = "System.Double", Value = "NaN", Source = "Default" },
                new() { Name = "Height", TypeName = "System.Double", Value = "NaN", Source = "Default" }
            }
        };
    }

    public async Task<FindElementsResult> FindElementsAsync(string? typeName, string? elementName, Dictionary<string, string>? propertyFilter)
    {
        EnsureConnected();

        _logger.LogDebug("FindElements called with typeName={TypeName}, elementName={ElementName}",
            typeName, elementName);

        // Return stub data
        return new FindElementsResult
        {
            Elements = new List<FoundElement>
            {
                new()
                {
                    Handle = "elem_button_1",
                    TypeName = "System.Windows.Controls.Button",
                    Name = "SubmitButton",
                    Path = "Window > Grid > StackPanel > Button"
                }
            },
            Count = 1
        };
    }

    public async Task<BindingsResult> GetBindingsAsync(string elementHandle)
    {
        EnsureConnected();

        _logger.LogDebug("GetBindings called for {ElementHandle}", elementHandle);

        // Return stub data
        return new BindingsResult
        {
            Element = new ElementInfo
            {
                Handle = elementHandle,
                TypeName = "System.Windows.Controls.TextBox"
            },
            Bindings = new List<BindingInfo>
            {
                new()
                {
                    Property = "Text",
                    Path = "UserName",
                    Source = "ViewModel",
                    Mode = "TwoWay",
                    UpdateTrigger = "PropertyChanged",
                    Status = "Active",
                    CurrentValue = "John Doe"
                }
            }
        };
    }

    public async Task<BindingErrorsResult> GetBindingErrorsAsync()
    {
        EnsureConnected();

        _logger.LogDebug("GetBindingErrors called");

        // Return stub data (empty for now)
        return new BindingErrorsResult
        {
            Errors = new List<BindingError>(),
            Count = 0
        };
    }

    public async Task<ResourcesResult> GetResourcesAsync(string scope, string? elementHandle)
    {
        EnsureConnected();

        _logger.LogDebug("GetResources called with scope={Scope}, elementHandle={ElementHandle}",
            scope, elementHandle);

        // Return stub data
        return new ResourcesResult
        {
            Resources = new List<ResourceInfo>
            {
                new()
                {
                    Key = "PrimaryBrush",
                    TypeName = "System.Windows.Media.SolidColorBrush",
                    Value = "#FF0078D7",
                    Source = "App.xaml"
                }
            }
        };
    }

    public async Task<StylesResult> GetStylesAsync(string elementHandle)
    {
        EnsureConnected();

        _logger.LogDebug("GetStyles called for {ElementHandle}", elementHandle);

        // Return stub data
        return new StylesResult
        {
            Element = new ElementInfo
            {
                Handle = elementHandle,
                TypeName = "System.Windows.Controls.Button"
            },
            Style = new StyleInfo
            {
                Key = "DefaultButtonStyle",
                TargetType = "System.Windows.Controls.Button",
                Setters = new List<SetterInfo>()
            }
        };
    }

    public async Task<string> WatchPropertyAsync(string elementHandle, string propertyName)
    {
        EnsureConnected();

        _logger.LogDebug("WatchProperty called for {ElementHandle}.{PropertyName}",
            elementHandle, propertyName);

        // Return a stub watch ID
        return $"watch_{Guid.NewGuid():N}";
    }

    public async Task HighlightElementAsync(string elementHandle, int durationMs)
    {
        EnsureConnected();

        _logger.LogDebug("HighlightElement called for {ElementHandle} with duration {DurationMs}ms",
            elementHandle, durationMs);

        // TODO: Send highlight request to inspector DLL
    }

    public async Task<LayoutInfoResult> GetLayoutInfoAsync(string elementHandle)
    {
        EnsureConnected();

        _logger.LogDebug("GetLayoutInfo called for {ElementHandle}", elementHandle);

        // Return stub data
        return new LayoutInfoResult
        {
            Element = new ElementInfo
            {
                Handle = elementHandle,
                TypeName = "System.Windows.Controls.Grid"
            },
            Layout = new LayoutInfo
            {
                ActualWidth = 800,
                ActualHeight = 600,
                DesiredSize = new SizeInfo { Width = 800, Height = 600 },
                RenderSize = new SizeInfo { Width = 800, Height = 600 },
                Margin = new ThicknessInfo { Left = 10, Top = 10, Right = 10, Bottom = 10 },
                Padding = new ThicknessInfo { Left = 5, Top = 5, Right = 5, Bottom = 5 },
                HorizontalAlignment = "Stretch",
                VerticalAlignment = "Stretch",
                Visibility = "Visible"
            }
        };
    }

    public async Task<ExportResult> ExportTreeAsync(string? elementHandle, string format)
    {
        EnsureConnected();

        _logger.LogDebug("ExportTree called with elementHandle={ElementHandle}, format={Format}",
            elementHandle, format);

        // Get the visual tree first
        var tree = await GetVisualTreeAsync(elementHandle, 100);

        if (format == "json")
        {
            var json = JsonSerializer.Serialize(tree, _jsonOptions);
            return new ExportResult
            {
                Format = "json",
                Content = json,
                ElementCount = tree.TotalElements
            };
        }
        else if (format == "xaml")
        {
            // Generate a simple XAML representation
            var xaml = GenerateXaml(tree.Root);
            return new ExportResult
            {
                Format = "xaml",
                Content = xaml,
                ElementCount = tree.TotalElements
            };
        }

        throw new ArgumentException($"Unknown format: {format}");
    }

    private void EnsureConnected()
    {
        var session = _processManager.CurrentSession;
        if (session == null)
        {
            throw new InvalidOperationException("Not attached to any WPF process. Use wpf_attach first.");
        }
    }

    private string GenerateXaml(VisualTreeNode node, int indent = 0)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent * 2);
        var shortTypeName = node.TypeName.Split('.').Last();

        if (node.Children.Count == 0)
        {
            sb.Append($"{indentStr}<{shortTypeName}");
            if (!string.IsNullOrEmpty(node.Name))
                sb.Append($" x:Name=\"{node.Name}\"");
            sb.AppendLine(" />");
        }
        else
        {
            sb.Append($"{indentStr}<{shortTypeName}");
            if (!string.IsNullOrEmpty(node.Name))
                sb.Append($" x:Name=\"{node.Name}\"");
            sb.AppendLine(">");

            foreach (var child in node.Children)
            {
                sb.Append(GenerateXaml(child, indent + 1));
            }

            sb.AppendLine($"{indentStr}</{shortTypeName}>");
        }

        return sb.ToString();
    }
}
