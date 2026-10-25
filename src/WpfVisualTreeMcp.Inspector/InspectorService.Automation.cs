using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Inspector;

public partial class InspectorService
{
    private IpcResponse HandleControlOperation(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<ControlOperationRequest>(data)!;
        var element = _treeWalker.ResolveHandle(request.ElementHandle)
            ?? throw new InvalidOperationException(StaleHandleError(request.ElementHandle));
        return new ControlOperations(_treeWalker).Execute(element, request);
    }

    private IpcResponse HandleUnwatchProperty(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<UnwatchPropertyRequest>(data)!;
        _propertyWatcher.Unwatch(request.WatchId);
        return new UnwatchPropertyResponse { RequestId = request.RequestId };
    }

    private IpcResponse HandleWindow(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<WindowRequest>(data)!;
        var windows = Application.Current.Windows.Cast<Window>().ToArray();
        if (request.Action != "list")
        {
            var window = _treeWalker.ResolveHandle(request.ElementHandle!) as Window;
            if (window == null || !windows.Contains(window)) throw new InvalidOperationException("WPF window is no longer open.");
            switch (request.Action)
            {
                case "activate":
                    if (!window.Activate()) throw new InvalidOperationException("WPF could not activate this window.");
                    break;
                case "minimize": window.WindowState = WindowState.Minimized; break;
                case "maximize": window.WindowState = WindowState.Maximized; break;
                case "restore": window.WindowState = WindowState.Normal; break;
                // Closing handlers may open a modal dialog. Return acceptance before invoking them.
                case "close": window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(window.Close)); break;
                default: throw new ArgumentException("Unknown window action: " + request.Action);
            }
        }
        return new WindowResponse
        {
            RequestId = request.RequestId,
            Windows = windows.Select(w => new WpfWindowInfo
            {
                Handle = _treeWalker.GetOrCreateHandle(w), AutomationId = AutomationProperties.GetAutomationId(w),
                Name = w.Name, Title = w.Title, OwnerHandle = w.Owner == null ? null : _treeWalker.GetOrCreateHandle(w.Owner),
                IsMain = ReferenceEquals(Application.Current.MainWindow, w), IsActive = w.IsActive,
                IsVisible = w.IsVisible, IsEnabled = w.IsEnabled, State = w.WindowState.ToString()
            }).ToList()
        };
    }
}
