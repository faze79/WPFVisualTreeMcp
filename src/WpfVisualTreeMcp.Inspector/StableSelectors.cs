using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using WpfVisualTreeMcp.Shared;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Inspector;

internal sealed class StableSelectors
{
    private readonly Func<IEnumerable<DependencyObject>> _roots;
    internal StableSelectors(Func<IEnumerable<DependencyObject>>? roots = null)
        => _roots = roots ?? (() => TreeWalker.GetAllSearchRoots());

    internal ElementSelector Capture(DependencyObject element)
    {
        var candidates = Identities(element).ToList();
        foreach (var candidate in candidates)
            if (IsUnique(candidate, element)) return candidate;
        // Template parts (e.g. a slider Thumb) often have no stable identity of
        // their own. Identify a named ancestor and require a unique descendant.
        if (candidates.Count == 0) candidates.Add(new ElementSelector { TypeName = element.GetType().FullName! });
        var parent = Parent(element);
        for (var depth = 0; parent != null && depth < 32; depth++, parent = Parent(parent))
        {
            foreach (var anchor in Identities(parent))
            {
                if (!IsUnique(anchor, parent)) continue;
                foreach (var candidate in candidates)
                {
                    candidate.Ancestor = anchor;
                    if (IsUnique(candidate, element)) return candidate;
                }
            }
        }
        throw new InvalidOperationException("Cannot record a unique stable selector for " + element.GetType().Name +
            ". Give the control or an ancestor a unique x:Name or AutomationId before recording.");
    }

    internal DependencyObject? Resolve(ElementSelector selector, bool allowMissing = false)
    {
        WorkflowCodec.ValidateSelector(selector);
        var matches = Matches(selector);
        if (matches.Count > 1) throw new InvalidOperationException("Ambiguous workflow selector: " + Describe(selector));
        if (matches.Count == 0 && !allowMissing) throw new InvalidOperationException("Workflow target not found: " + Describe(selector));
        return matches.FirstOrDefault();
    }

    private bool IsUnique(ElementSelector selector, DependencyObject expected)
    {
        var matches = Matches(selector);
        return matches.Count == 1 && ReferenceEquals(matches[0], expected);
    }

    private List<DependencyObject> Matches(ElementSelector selector)
    {
        IEnumerable<DependencyObject> roots = _roots();
        if (selector.Ancestor != null)
        {
            var parent = Resolve(selector.Ancestor, allowMissing: true);
            if (parent == null) return new List<DependencyObject>();
            roots = TreeWalker.GetAllVisualChildren(parent);
        }
        var matches = new List<DependencyObject>();
        var queue = new Queue<DependencyObject>(roots);
        var visited = new HashSet<DependencyObject>();
        var clock = Stopwatch.StartNew();
        while (queue.Count > 0)
        {
            if (visited.Count >= 10000 || queue.Count > 10000 || clock.ElapsedMilliseconds > 2000)
                throw new InvalidOperationException("Selector search exceeded its 10000-element / two-second budget; uniqueness is unknown.");
            var item = queue.Dequeue();
            if (!visited.Add(item)) continue;
            if (item.GetType().FullName == selector.TypeName
                && (selector.AutomationId == null || AutomationProperties.GetAutomationId(item) == selector.AutomationId)
                && (selector.Name == null || item is FrameworkElement fe && fe.Name == selector.Name)
                && (selector.Text == null || TreeWalker.GetSearchableText(item) == selector.Text))
            {
                matches.Add(item);
                if (matches.Count > 1) return matches;
            }
            foreach (var child in TreeWalker.GetAllVisualChildren(item)) queue.Enqueue(child);
        }
        return matches;
    }

    private static IEnumerable<ElementSelector> Identities(DependencyObject element)
    {
        string type = element.GetType().FullName!;
        var id = AutomationProperties.GetAutomationId(element);
        if (!string.IsNullOrEmpty(id) && id.Length <= 2048) yield return new ElementSelector { TypeName = type, AutomationId = id };
        if (element is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
            yield return new ElementSelector { TypeName = type, Name = fe.Name };
        var text = TreeWalker.GetSearchableText(element);
        if (!string.IsNullOrEmpty(text) && text!.Length <= 2048)
            yield return new ElementSelector { TypeName = type, Text = text };
    }

    private static DependencyObject? Parent(DependencyObject element) =>
        (element is Visual ? VisualTreeHelper.GetParent(element) : null) ?? LogicalTreeHelper.GetParent(element);
    private static string Describe(ElementSelector selector) => selector.TypeName + " " + (selector.AutomationId ?? selector.Name ?? selector.Text ?? "(ancestor scoped)");
}
