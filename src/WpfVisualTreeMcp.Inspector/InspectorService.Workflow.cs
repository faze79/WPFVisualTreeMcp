using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WpfVisualTreeMcp.Shared;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Inspector;

public partial class InspectorService
{
    private static Task<T> OnDispatcher<T>(Func<T> callback, CancellationToken cancellationToken,
        DispatcherPriority priority = DispatcherPriority.Normal) =>
        Task.Run(() => Application.Current.Dispatcher.Invoke(callback, priority,
            cancellationToken, TimeSpan.FromSeconds(10)), cancellationToken);

    private Task<IpcResponse> ExecuteOperationAsync(string type, JsonElement data, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return type switch
        {
            "Drag" => HandleDragAsync(data, token),
            "TouchGesture" => HandleTouchGestureAsync(data, token),
            "WaitForElement" => HandleWaitForElementAsync(data, token),
            _ => OnDispatcher(() => HandleRequest(type, data, token), token)
        };
    }

    private IpcResponse HandleRecord(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<RecordRequest>(data)!;
        return new RecordResponse { RequestId = request.RequestId, State = _recorder.Control(request.Action, request.Name) };
    }

    private IpcResponse HandleAssertElement(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<AssertElementRequest>(data)!;
        WorkflowCodec.ValidateRequest(request);
        var element = _treeWalker.ResolveHandle(request.ElementHandle)
            ?? throw new InvalidOperationException(StaleHandleError(request.ElementHandle));
        var property = _propertyReader.GetDependencyProperties(element).FirstOrDefault(p => p.Name == request.PropertyName)
            ?? throw new ArgumentException("Unknown dependency property: " + request.PropertyName);
        var value = element.GetValue(property);
        var actual = value == null ? "{null}" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        bool matches = string.Equals(actual, request.ExpectedValue, StringComparison.Ordinal);
        return new AssertElementResponse
        {
            RequestId = request.RequestId, Success = matches, ActualValue = actual,
            Error = matches ? null : $"Assertion failed for {request.PropertyName}: expected '{request.ExpectedValue}', actual '{actual}'."
        };
    }

    private async Task<IpcResponse> HandleReplayAsync(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<ReplayRequest>(data)!;
        WorkflowCodec.Validate(request.Workflow);
        if (request.TimeoutMs < 1000 || request.TimeoutMs > 120000)
            throw new ArgumentException("Replay timeout must be between 1000 and 120000 ms.");
        if (_recorder.Active) throw new InvalidOperationException("Stop recording before replaying a workflow.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_inputShutdown.Token);
        timeout.CancelAfter(request.TimeoutMs);
        var result = new ReplayResult();
        try
        {
            foreach (var step in request.Workflow.Steps)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var operation = WorkflowCodec.ReadRequest(step.Action, step.Arguments);
                await OnDispatcher(() =>
                {
                    string? Resolve(ElementSelector? selector) => selector == null ? null :
                        _treeWalker.GetOrCreateHandle(_recorder.Selectors.Resolve(selector)!);
                    WorkflowCodec.BindHandles(operation, Resolve(step.Target), Resolve(step.Destination), Resolve(step.Root));
                    return true;
                }, timeout.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(IpcSerializer.Serialize<object>(operation));
                var response = await ExecuteOperationAsync(operation.RequestType, document.RootElement, timeout.Token).ConfigureAwait(false);
                if (!response.Success) throw new InvalidOperationException(response.Error ?? "Step failed.");
                if (response is WaitForElementResponse wait && !wait.Matched)
                    throw new InvalidOperationException("Recorded wait condition was not satisfied before its timeout.");
                // UI Automation Invoke and OS input can enqueue work below
                // Normal priority. Let input, binding and layout callbacks run
                // before resolving/asserting the next step. App async work still
                // needs a recorded explicit wait.
                await OnDispatcher(() => true, timeout.Token, DispatcherPriority.Background).ConfigureAwait(false);
                result.CompletedSteps++;
            }
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.FailedStep = result.CompletedSteps + 1;
            result.Error = ex is OperationCanceledException ? "Replay deadline expired or Inspector is shutting down." : ex.Message;
        }
        return new ReplayResponse { RequestId = request.RequestId, Result = result };
    }
}
