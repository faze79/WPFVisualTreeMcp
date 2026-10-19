using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using WpfVisualTreeMcp.Shared;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>Accessed on the Dispatcher while the interaction gate is held.</summary>
internal sealed class WorkflowRecorder
{
    private volatile bool _active;
    internal bool Active { get => _active; private set => _active = value; }
    private RecordedWorkflow? _workflow;
    private readonly TreeWalker _walker;
    internal StableSelectors Selectors { get; } = new();

    internal WorkflowRecorder(TreeWalker walker) => _walker = walker;

    internal RecordingState Control(string action, string? name)
    {
        switch (action)
        {
            case "start":
                if (_workflow != null) throw new InvalidOperationException("A recording already exists. Export it, then clear it before starting another.");
                if (name?.Length > 200) throw new ArgumentException("Recording name must not exceed 200 characters.");
                _workflow = new RecordedWorkflow { Name = name ?? "Recorded workflow" };
                Active = true;
                break;
            case "stop": Active = false; break;
            case "clear": Active = false; _workflow = null; break;
            case "status": break;
            default: throw new ArgumentException("Record action must be start, stop, status or clear.");
        }
        // A copy keeps serialization on the pipe worker independent of later edits.
        return new RecordingState { Active = Active, Workflow = _workflow == null ? null : IpcSerializer.Deserialize<RecordedWorkflow>(IpcSerializer.Serialize(_workflow)) };
    }

    internal WorkflowStep Prepare(string action, JsonElement data)
    {
        if (!Active || _workflow == null) throw new InvalidOperationException("No active recording.");
        if (_workflow.Steps.Count >= WorkflowCodec.MaxSteps) throw new InvalidOperationException("Recording reached 100 steps. Stop and export it before continuing.");
        var request = WorkflowCodec.ParseLiveRequest(action, data);
        WorkflowCodec.ValidateRequest(request);
        var handles = WorkflowCodec.Handles(request);
        // Keyboard input without an explicit handle still needs a reproducible
        // focus target. Never persist an implicit global keyboard destination.
        if (request is SendKeysRequest && string.IsNullOrEmpty(handles.target))
        {
            var focused = Keyboard.FocusedElement as DependencyObject
                ?? throw new InvalidOperationException("Recording send-keys requires an explicit handle or a focused WPF element.");
            handles.target = _walker.GetOrCreateHandle(focused);
        }
        ElementSelector? Capture(string? handle) => string.IsNullOrEmpty(handle) ? null :
            Selectors.Capture(_walker.ResolveHandle(handle!) ?? throw new InvalidOperationException("Stale recording handle: " + handle));
        var step = new WorkflowStep
        {
            Action = action, Target = Capture(handles.target), Destination = Capture(handles.destination),
            Root = Capture(handles.root), Arguments = WorkflowCodec.Arguments(request)
        };
        // Check the complete prospective recording before performing an action.
        _workflow.Steps.Add(step);
        try { WorkflowCodec.Validate(_workflow); }
        finally { _workflow.Steps.RemoveAt(_workflow.Steps.Count - 1); }
        return step;
    }

    internal void Commit(WorkflowStep step) => _workflow!.Steps.Add(step);
}
