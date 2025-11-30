namespace WpfVisualTreeMcp.Shared.Ipc;

/// <summary>
/// Base class for all IPC requests.
/// </summary>
public abstract class IpcRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public abstract string RequestType { get; }
}

/// <summary>
/// Base class for all IPC responses.
/// </summary>
public abstract class IpcResponse
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; } = true;
    public string? Error { get; set; }
}

// Visual Tree
public class GetVisualTreeRequest : IpcRequest
{
    public override string RequestType => "GetVisualTree";
    public string? RootHandle { get; set; }
    public int MaxDepth { get; set; } = 10;
}

public class GetVisualTreeResponse : IpcResponse
{
    public string? TreeJson { get; set; }
    public int TotalElements { get; set; }
    public bool MaxDepthReached { get; set; }
}

// Element Properties
public class GetElementPropertiesRequest : IpcRequest
{
    public override string RequestType => "GetElementProperties";
    public string ElementHandle { get; set; } = string.Empty;
}

public class GetElementPropertiesResponse : IpcResponse
{
    public string? PropertiesJson { get; set; }
}

// Find Elements
public class FindElementsRequest : IpcRequest
{
    public override string RequestType => "FindElements";
    public string? TypeName { get; set; }
    public string? ElementName { get; set; }
    public Dictionary<string, string>? PropertyFilter { get; set; }
}

public class FindElementsResponse : IpcResponse
{
    public string? ElementsJson { get; set; }
    public int Count { get; set; }
}

// Layout Info
public class GetLayoutInfoRequest : IpcRequest
{
    public override string RequestType => "GetLayoutInfo";
    public string ElementHandle { get; set; } = string.Empty;
}

public class GetLayoutInfoResponse : IpcResponse
{
    public string? LayoutJson { get; set; }
}

// Bindings
public class GetBindingsRequest : IpcRequest
{
    public override string RequestType => "GetBindings";
    public string ElementHandle { get; set; } = string.Empty;
}

public class GetBindingsResponse : IpcResponse
{
    public string? BindingsJson { get; set; }
}

public class GetBindingErrorsRequest : IpcRequest
{
    public override string RequestType => "GetBindingErrors";
}

public class GetBindingErrorsResponse : IpcResponse
{
    public string? ErrorsJson { get; set; }
    public int Count { get; set; }
}

// Resources & Styles
public class GetResourcesRequest : IpcRequest
{
    public override string RequestType => "GetResources";
    public string Scope { get; set; } = "application";
    public string? ElementHandle { get; set; }
}

public class GetResourcesResponse : IpcResponse
{
    public string? ResourcesJson { get; set; }
}

public class GetStylesRequest : IpcRequest
{
    public override string RequestType => "GetStyles";
    public string ElementHandle { get; set; } = string.Empty;
}

public class GetStylesResponse : IpcResponse
{
    public string? StylesJson { get; set; }
}

// Highlight
public class HighlightElementRequest : IpcRequest
{
    public override string RequestType => "HighlightElement";
    public string ElementHandle { get; set; } = string.Empty;
    public int DurationMs { get; set; } = 2000;
}

public class HighlightElementResponse : IpcResponse { }

// Property Watching
public class WatchPropertyRequest : IpcRequest
{
    public override string RequestType => "WatchProperty";
    public string ElementHandle { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
}

public class WatchPropertyResponse : IpcResponse
{
    public string WatchId { get; set; } = string.Empty;
    public string? InitialValue { get; set; }
}

// Export
public class ExportTreeRequest : IpcRequest
{
    public override string RequestType => "ExportTree";
    public string? ElementHandle { get; set; }
    public string Format { get; set; } = "json";
}

public class ExportTreeResponse : IpcResponse
{
    public string? Content { get; set; }
    public int ElementCount { get; set; }
}

// Notifications (Inspector -> Server)
public class PropertyChangedNotification
{
    public string NotificationType => "PropertyChanged";
    public string WatchId { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class BindingErrorNotification
{
    public string NotificationType => "BindingError";
    public string ElementType { get; set; } = string.Empty;
    public string? ElementName { get; set; }
    public string Property { get; set; } = string.Empty;
    public string BindingPath { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
