using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>Resolves an exact ID only when uniqueness in the requested scope is known.</summary>
internal static class AutomationIdResolver
{
    internal static DependencyObject Resolve(string automationId, IEnumerable<DependencyObject> roots)
        => ResolveOrDefault(automationId, roots) ?? throw new InvalidOperationException("AutomationId '" + automationId + "' was not found in the requested scope.");

    internal static DependencyObject? ResolveOrDefault(string automationId, IEnumerable<DependencyObject> roots)
    {
        if (string.IsNullOrWhiteSpace(automationId) || automationId.Length > 2048)
            throw new ArgumentException("automation_id must contain 1-2048 characters and cannot be blank.");

        DependencyObject? match = null;
        var queue = new Queue<DependencyObject>(roots);
        var visited = new HashSet<DependencyObject>();
        var clock = Stopwatch.StartNew();
        while (queue.Count > 0)
        {
            if (visited.Count >= 10000 || queue.Count > 10000 || clock.ElapsedMilliseconds > 2000)
                throw new InvalidOperationException("AutomationId search exceeded its 10000-element / two-second budget; uniqueness is unknown. Narrow the search with root_handle.");
            var element = queue.Dequeue();
            if (!visited.Add(element)) continue;
            if (string.Equals(AutomationProperties.GetAutomationId(element), automationId, StringComparison.Ordinal))
            {
                if (match != null)
                    throw new InvalidOperationException("Ambiguous AutomationId '" + automationId + "'. Use root_handle to scope the search, or find the controls and use an element_handle.");
                match = element;
            }
            foreach (var child in TreeWalker.GetAllVisualChildren(element)) queue.Enqueue(child);
        }
        return match;
    }
}
