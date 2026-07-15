using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Search criteria for finding elements in the visual tree.
/// All specified filters must match (AND semantics).
/// </summary>
public class FindCriteria
{
    /// <summary>Type name, matched case-insensitively (substring of full name or exact short name).</summary>
    public string? TypeName { get; set; }

    /// <summary>x:Name, matched as case-insensitive substring.</summary>
    public string? ElementName { get; set; }

    /// <summary>
    /// Visible text content, matched as case-insensitive substring against the element's own text,
    /// text aggregated from shallow descendants (e.g. the TextBlock inside a Button),
    /// AutomationProperties.Name, ToolTip and Window.Title.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>Property name → expected value (case-insensitive substring match on the value's ToString()).</summary>
    public Dictionary<string, string>? PropertyFilter { get; set; }

    /// <summary>When true, only elements that are currently visible on screen are returned.</summary>
    public bool VisibleOnly { get; set; }
}

/// <summary>
/// One element's captured state within a snapshot: identity plus a curated set of
/// properties that typically change when a UI is modified.
/// </summary>
public class SnapshotNode
{
    public string Handle { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Path { get; set; } = string.Empty;

    /// <summary>Property name → stringified value.</summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}

/// <summary>
/// Walks the visual tree and logical tree of WPF elements.
/// </summary>
public class TreeWalker
{
    // Weak handle cache: must not keep detached elements (and their subtrees) alive.
    private readonly ConditionalWeakTable<DependencyObject, string> _handleCache = new();
    private readonly Dictionary<string, WeakReference<DependencyObject>> _handleLookup = new();
    private int _handleCounter;

    public int HandleCacheCount => _handleLookup.Count;

    /// <summary>
    /// Walks the visual tree starting from the specified root element.
    /// </summary>
    /// <param name="root">The root element to start from.</param>
    /// <param name="maxDepth">Maximum depth to traverse.</param>
    /// <returns>JSON representation of the visual tree.</returns>
    public string WalkVisualTree(DependencyObject root, int maxDepth = 10)
    {
        var sb = new StringBuilder();
        var elementCount = 0;
        var maxDepthReached = false;

        sb.Append("{\"root\":");
        WalkVisualTreeRecursive(root, sb, 0, maxDepth, ref elementCount, ref maxDepthReached);
        sb.Append($",\"totalElements\":{elementCount},\"maxDepthReached\":{maxDepthReached.ToString().ToLower()}}}");

        return sb.ToString();
    }

    private void WalkVisualTreeRecursive(
        DependencyObject element,
        StringBuilder sb,
        int depth,
        int maxDepth,
        ref int elementCount,
        ref bool maxDepthReached)
    {
        elementCount++;
        var handle = GetOrCreateHandle(element);
        var typeName = element.GetType().FullName ?? element.GetType().Name;
        var name = GetElementName(element);

        sb.Append("{");
        sb.Append($"\"handle\":\"{handle}\"");
        sb.Append($",\"typeName\":\"{EscapeJson(typeName)}\"");

        if (!string.IsNullOrEmpty(name))
        {
            sb.Append($",\"name\":\"{EscapeJson(name)}\"");
        }

        sb.Append($",\"depth\":{depth}");

        // Get children (including adorners and popup content)
        sb.Append(",\"children\":[");

        if (depth < maxDepth)
        {
            var first = true;
            foreach (var child in GetAllVisualChildren(element))
            {
                if (!first) sb.Append(",");
                first = false;

                WalkVisualTreeRecursive(child, sb, depth + 1, maxDepth, ref elementCount, ref maxDepthReached);
            }
        }
        else if (HasAnyVisualChildren(element))
        {
            maxDepthReached = true;
        }

        sb.Append("]}");
    }

    /// <summary>
    /// Walks the logical tree starting from the specified root element.
    /// </summary>
    /// <param name="root">The root element to start from.</param>
    /// <param name="maxDepth">Maximum depth to traverse.</param>
    /// <returns>JSON representation of the logical tree.</returns>
    public string WalkLogicalTree(DependencyObject root, int maxDepth = 10)
    {
        var sb = new StringBuilder();
        var elementCount = 0;
        var maxDepthReached = false;

        sb.Append("{\"root\":");
        WalkLogicalTreeRecursive(root, sb, 0, maxDepth, ref elementCount, ref maxDepthReached);
        sb.Append($",\"totalElements\":{elementCount},\"maxDepthReached\":{maxDepthReached.ToString().ToLower()}}}");

        return sb.ToString();
    }

    private void WalkLogicalTreeRecursive(
        DependencyObject element,
        StringBuilder sb,
        int depth,
        int maxDepth,
        ref int elementCount,
        ref bool maxDepthReached)
    {
        elementCount++;
        var handle = GetOrCreateHandle(element);
        var typeName = element.GetType().FullName ?? element.GetType().Name;
        var name = GetElementName(element);

        sb.Append("{");
        sb.Append($"\"handle\":\"{handle}\"");
        sb.Append($",\"typeName\":\"{EscapeJson(typeName)}\"");

        if (!string.IsNullOrEmpty(name))
        {
            sb.Append($",\"name\":\"{EscapeJson(name)}\"");
        }

        sb.Append($",\"depth\":{depth}");

        // Get logical children
        sb.Append(",\"children\":[");

        if (depth < maxDepth)
        {
            var first = true;
            foreach (var child in LogicalTreeHelper.GetChildren(element))
            {
                if (child is not DependencyObject depChild) continue;

                if (!first) sb.Append(",");
                first = false;

                WalkLogicalTreeRecursive(depChild, sb, depth + 1, maxDepth, ref elementCount, ref maxDepthReached);
            }
        }
        else
        {
            var hasChildren = false;
            foreach (var _ in LogicalTreeHelper.GetChildren(element))
            {
                hasChildren = true;
                break;
            }
            if (hasChildren) maxDepthReached = true;
        }

        sb.Append("]}");
    }

    /// <summary>
    /// Gets the path from root to the specified element.
    /// </summary>
    public string GetElementPath(DependencyObject element)
    {
        var path = new List<string>();
        var current = element;

        while (current != null)
        {
            var typeName = current.GetType().Name;
            var name = GetElementName(current);

            if (!string.IsNullOrEmpty(name))
            {
                path.Insert(0, $"{typeName}[{name}]");
            }
            else
            {
                path.Insert(0, typeName);
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return string.Join(" > ", path);
    }

    /// <summary>
    /// Resolves an element handle to the actual DependencyObject.
    /// Returns null when the handle was never issued or the element has been garbage-collected.
    /// </summary>
    public DependencyObject? ResolveHandle(string handle)
    {
        if (_handleLookup.TryGetValue(handle, out var weakRef))
        {
            if (weakRef.TryGetTarget(out var element))
            {
                return element;
            }
            _handleLookup.Remove(handle);
        }
        return null;
    }

    private string GetOrCreateHandle(DependencyObject element)
    {
        if (_handleCache.TryGetValue(element, out var handle))
        {
            return handle;
        }

        handle = $"elem_{_handleCounter++:X8}";
        _handleCache.Add(element, handle);
        _handleLookup[handle] = new WeakReference<DependencyObject>(element);
        return handle;
    }

    private static string? GetElementName(DependencyObject element)
    {
        if (element is FrameworkElement fe)
        {
            return string.IsNullOrEmpty(fe.Name) ? null : fe.Name;
        }
        if (element is FrameworkContentElement fce)
        {
            return string.IsNullOrEmpty(fce.Name) ? null : fce.Name;
        }
        return null;
    }

    /// <summary>
    /// Finds elements matching the specified criteria.
    /// </summary>
    /// <param name="root">The root element to search from.</param>
    /// <param name="criteria">Search criteria (type, name, text, property values, visibility).</param>
    /// <param name="maxResults">Maximum number of results to return (default: 50, max: 10000).</param>
    /// <returns>JSON array of matching elements.</returns>
    public string FindElements(DependencyObject root, FindCriteria criteria, int maxResults = 50)
    {
        // Clamp maxResults to reasonable limit to prevent memory issues
        if (maxResults > 10000) maxResults = 10000;
        if (maxResults < 1) maxResults = 1;

        var results = new List<string>();
        FindElementsRecursive(root, criteria, results, maxResults);

        var sb = new StringBuilder();
        sb.Append("{\"elements\":[");
        for (int i = 0; i < results.Count; i++)
        {
            if (i > 0) sb.Append(",");
            sb.Append(results[i]);
        }
        sb.Append($"],\"count\":{results.Count}}}");
        return sb.ToString();
    }

    /// <summary>
    /// Finds ALL elements matching the specified criteria without limit (deep search).
    /// WARNING: This can return a large number of results. Use with caution.
    /// </summary>
    /// <param name="root">The root element to search from.</param>
    /// <param name="criteria">Search criteria (type, name, text, property values, visibility).</param>
    /// <returns>JSON array of matching elements.</returns>
    public string FindElementsDeep(DependencyObject root, FindCriteria criteria, int maxResults = 100000)
    {
        if (maxResults > 100000) maxResults = 100000;
        if (maxResults < 1) maxResults = 1;

        var results = new List<string>();
        FindElementsRecursive(root, criteria, results, maxResults);

        var sb = new StringBuilder();
        sb.Append("{\"elements\":[");
        for (int i = 0; i < results.Count; i++)
        {
            if (i > 0) sb.Append(",");
            sb.Append(results[i]);
        }
        sb.Append($"],\"count\":{results.Count},\"truncated\":{(results.Count >= maxResults).ToString().ToLower()}}}");
        return sb.ToString();
    }

    /// <summary>
    /// Captures a snapshot of the subtree rooted at <paramref name="root"/> as a map of
    /// element handle → curated state. Handles are stable per element, so two snapshots
    /// can be diffed by handle: same handle in both = compare properties; handle only in
    /// the later one = added; only in the earlier one = removed.
    /// </summary>
    public Dictionary<string, SnapshotNode> CaptureSnapshot(DependencyObject root, int maxDepth)
    {
        var nodes = new Dictionary<string, SnapshotNode>();
        CaptureSnapshotRecursive(root, 0, maxDepth, nodes);
        return nodes;
    }

    private void CaptureSnapshotRecursive(DependencyObject element, int depth, int maxDepth, Dictionary<string, SnapshotNode> nodes)
    {
        var handle = GetOrCreateHandle(element);
        var type = element.GetType();
        nodes[handle] = new SnapshotNode
        {
            Handle = handle,
            TypeName = type.FullName ?? type.Name,
            Name = GetElementName(element),
            Path = GetElementPath(element),
            Properties = GetSnapshotProperties(element)
        };

        if (depth >= maxDepth) return;
        foreach (var child in GetAllVisualChildren(element))
        {
            CaptureSnapshotRecursive(child, depth + 1, maxDepth, nodes);
        }
    }

    /// <summary>
    /// The curated property set captured per element — the things that visibly change
    /// when you tweak a UI: geometry, visibility, alignment, brushes and text.
    /// </summary>
    private static Dictionary<string, string> GetSnapshotProperties(DependencyObject element)
    {
        var p = new Dictionary<string, string>();

        if (element is UIElement ui)
        {
            p["IsVisible"] = ui.IsVisible.ToString();
            p["IsEnabled"] = ui.IsEnabled.ToString();
            p["Visibility"] = ui.Visibility.ToString();
            p["Opacity"] = ui.Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (element is FrameworkElement fe)
        {
            p["ActualWidth"] = fe.ActualWidth.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            p["ActualHeight"] = fe.ActualHeight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            var m = fe.Margin;
            p["Margin"] = $"{m.Left},{m.Top},{m.Right},{m.Bottom}";
            p["HorizontalAlignment"] = fe.HorizontalAlignment.ToString();
            p["VerticalAlignment"] = fe.VerticalAlignment.ToString();
        }

        switch (element)
        {
            case Control c:
                p["Background"] = c.Background?.ToString() ?? "(null)";
                p["Foreground"] = c.Foreground?.ToString() ?? "(null)";
                var pad = c.Padding;
                p["Padding"] = $"{pad.Left},{pad.Top},{pad.Right},{pad.Bottom}";
                break;
            case Panel panel:
                p["Background"] = panel.Background?.ToString() ?? "(null)";
                break;
            case Border border:
                p["Background"] = border.Background?.ToString() ?? "(null)";
                p["BorderBrush"] = border.BorderBrush?.ToString() ?? "(null)";
                break;
            case TextBlock tb:
                p["Text"] = tb.Text ?? "";
                p["Foreground"] = tb.Foreground?.ToString() ?? "(null)";
                break;
        }

        // Own text (button caption, textbox value, ...) if any, for content-change detection.
        var own = GetOwnText(element);
        if (!string.IsNullOrEmpty(own) && !p.ContainsKey("Text"))
        {
            p["Text"] = own!;
        }

        return p;
    }

    /// <summary>
    /// Returns the first element (across all open windows) matching the criteria, as a
    /// (handle, typeName) pair, or null when none matches. Used by the wait/poll loop —
    /// cheaper than building a full result list when only existence matters.
    /// </summary>
    public (string Handle, string TypeName)? FindFirstMatch(FindCriteria criteria)
    {
        foreach (var root in GetAllSearchRoots())
        {
            var hit = FindFirstMatchRecursive(root, criteria);
            if (hit != null) return hit;
        }
        return null;
    }

    private (string Handle, string TypeName)? FindFirstMatchRecursive(DependencyObject element, FindCriteria criteria)
    {
        if (criteria.VisibleOnly && element is UIElement ui && !ui.IsVisible && element is not Popup)
        {
            return null;
        }

        if (MatchesCriteria(element, criteria))
        {
            var type = element.GetType();
            return (GetOrCreateHandle(element), type.FullName ?? type.Name);
        }

        foreach (var child in GetAllVisualChildren(element))
        {
            var hit = FindFirstMatchRecursive(child, criteria);
            if (hit != null) return hit;
        }
        return null;
    }

    private void FindElementsRecursive(DependencyObject element, FindCriteria criteria, List<string> results, int maxResults)
    {
        if (results.Count >= maxResults)
        {
            return;
        }

        // IsVisible propagates to descendants, so an invisible subtree can be pruned entirely.
        // Popup is exempt: the Popup element itself never renders, but its child tree does when open.
        if (criteria.VisibleOnly && element is UIElement ui && !ui.IsVisible && element is not Popup)
        {
            return;
        }

        if (MatchesCriteria(element, criteria))
        {
            results.Add(SerializeFoundElement(element));

            if (results.Count >= maxResults)
            {
                return;
            }
        }

        foreach (var child in GetAllVisualChildren(element))
        {
            FindElementsRecursive(child, criteria, results, maxResults);

            if (results.Count >= maxResults)
            {
                return;
            }
        }
    }

    private static bool MatchesCriteria(DependencyObject element, FindCriteria criteria)
    {
        var type = element.GetType();

        if (!string.IsNullOrEmpty(criteria.TypeName))
        {
            var fullTypeName = type.FullName ?? type.Name;
            // Use IndexOf for .NET Framework 4.8 compatibility (no Contains with StringComparison)
            var typeMatches = fullTypeName.IndexOf(criteria.TypeName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                              type.Name.Equals(criteria.TypeName, StringComparison.OrdinalIgnoreCase);
            if (!typeMatches) return false;
        }

        if (!string.IsNullOrEmpty(criteria.ElementName))
        {
            var name = GetElementName(element);
            if (name == null || name.IndexOf(criteria.ElementName, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }

        if (criteria.VisibleOnly && element is UIElement ui && !ui.IsVisible)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(criteria.Text))
        {
            var searchable = GetSearchableText(element);
            if (searchable == null || searchable.IndexOf(criteria.Text, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }

        if (criteria.PropertyFilter != null && criteria.PropertyFilter.Count > 0)
        {
            foreach (var kvp in criteria.PropertyFilter)
            {
                if (!PropertyValueMatches(element, kvp.Key, kvp.Value))
                    return false;
            }
        }

        return true;
    }

    private static bool PropertyValueMatches(DependencyObject element, string propertyName, string expectedValue)
    {
        try
        {
            var prop = element.GetType().GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null || !prop.CanRead) return false;

            var value = prop.GetValue(element)?.ToString();
            if (value == null) return string.IsNullOrEmpty(expectedValue);

            return value.IndexOf(expectedValue, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    private string SerializeFoundElement(DependencyObject element)
    {
        var type = element.GetType();
        var handle = GetOrCreateHandle(element);
        var name = GetElementName(element);
        var text = GetSearchableText(element);
        var path = GetElementPath(element);

        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"handle\":\"{handle}\"");
        sb.Append($",\"typeName\":\"{EscapeJson(type.FullName ?? type.Name)}\"");

        if (!string.IsNullOrEmpty(name))
        {
            sb.Append($",\"name\":\"{EscapeJson(name)}\"");
        }

        if (!string.IsNullOrEmpty(text))
        {
            sb.Append($",\"text\":\"{EscapeJson(Truncate(text!, 120))}\"");
        }

        var automationId = GetAutomationId(element);
        if (!string.IsNullOrEmpty(automationId))
        {
            sb.Append($",\"automationId\":\"{EscapeJson(automationId)}\"");
        }

        if (element is UIElement ui)
        {
            sb.Append($",\"isVisible\":{ui.IsVisible.ToString().ToLower()}");
            sb.Append($",\"isEnabled\":{ui.IsEnabled.ToString().ToLower()}");

            var bounds = GetScreenBoundsJson(ui);
            if (bounds != null)
            {
                sb.Append($",\"screenBounds\":{bounds}");
            }
        }

        sb.Append($",\"path\":\"{EscapeJson(path)}\"");
        sb.Append("}");
        return sb.ToString();
    }

    private static string? GetAutomationId(DependencyObject element)
    {
        try
        {
            var id = AutomationProperties.GetAutomationId(element);
            return string.IsNullOrEmpty(id) ? null : id;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Screen-space bounding rect of a visible element, in physical (device) pixels —
    /// the same coordinate space used by the OS mouse, so it is directly usable for physical clicks.
    /// </summary>
    private static string? GetScreenBoundsJson(UIElement element)
    {
        try
        {
            if (!element.IsVisible || PresentationSource.FromVisual(element) == null)
                return null;

            var topLeft = element.PointToScreen(new Point(0, 0));
            var bottomRight = element.PointToScreen(new Point(element.RenderSize.Width, element.RenderSize.Height));

            return $"{{\"x\":{(int)topLeft.X},\"y\":{(int)topLeft.Y}," +
                   $"\"width\":{(int)(bottomRight.X - topLeft.X)},\"height\":{(int)(bottomRight.Y - topLeft.Y)}}}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the human-visible text associated with an element: its own text, or text
    /// aggregated from shallow descendants (e.g. the TextBlock inside a Button's template),
    /// plus AutomationProperties.Name, ToolTip and Window.Title.
    /// Internal so ControlInteractor can describe item containers the same way.
    /// </summary>
    internal static string? GetSearchableText(DependencyObject element)
    {
        var parts = new List<string>();

        var own = GetOwnText(element);
        if (!string.IsNullOrWhiteSpace(own))
        {
            parts.Add(own!.Trim());
        }
        else
        {
            // No direct text: aggregate from shallow descendants (covers Button > TextBlock etc.)
            CollectDescendantText(element, parts, depth: 0, maxDepth: 4, maxParts: 5);
        }

        try
        {
            var autoName = AutomationProperties.GetName(element);
            if (!string.IsNullOrWhiteSpace(autoName) && !parts.Contains(autoName))
                parts.Add(autoName);
        }
        catch { /* attached property not readable on all elements */ }

        if (element is FrameworkElement fe && fe.ToolTip is string tooltip && !string.IsNullOrWhiteSpace(tooltip))
        {
            parts.Add(tooltip);
        }

        return parts.Count == 0 ? null : string.Join(" ", parts.Distinct());
    }

    private static string? GetOwnText(DependencyObject element)
    {
        return element switch
        {
            Window w => w.Title,
            TextBlock tb => tb.Text,
            AccessText at => at.Text,
            TextBox tb => tb.Text,
            Run run => run.Text,
            HeaderedContentControl hcc when hcc.Header is string h => h,
            HeaderedItemsControl hic when hic.Header is string h => h,
            ContentControl cc when cc.Content is string s => s,
            _ => null
        };
    }

    private static void CollectDescendantText(DependencyObject element, List<string> parts, int depth, int maxDepth, int maxParts)
    {
        if (depth >= maxDepth || parts.Count >= maxParts) return;

        foreach (var child in GetAllVisualChildren(element))
        {
            if (parts.Count >= maxParts) return;

            var text = GetOwnText(child);
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text!.Trim());
            }
            else
            {
                CollectDescendantText(child, parts, depth + 1, maxDepth, maxParts);
            }
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text.Substring(0, maxLength) + "…";
    }

    /// <summary>
    /// Exports the visual tree to XAML-like format.
    /// </summary>
    /// <param name="root">The root element to export from.</param>
    /// <returns>XAML representation of the visual tree.</returns>
    public string ExportToXaml(DependencyObject root)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<!-- Visual Tree Export -->");
        ExportToXamlRecursive(root, sb, 0);
        return sb.ToString();
    }

    private void ExportToXamlRecursive(DependencyObject element, StringBuilder sb, int indent)
    {
        var indentStr = new string(' ', indent * 2);
        var typeName = element.GetType().Name;
        var name = GetElementName(element);

        var children = GetAllVisualChildren(element).ToList();

        if (children.Count == 0)
        {
            sb.Append($"{indentStr}<{typeName}");
            if (!string.IsNullOrEmpty(name))
            {
                sb.Append($" x:Name=\"{EscapeXml(name)}\"");
            }
            sb.AppendLine(" />");
        }
        else
        {
            sb.Append($"{indentStr}<{typeName}");
            if (!string.IsNullOrEmpty(name))
            {
                sb.Append($" x:Name=\"{EscapeXml(name)}\"");
            }
            sb.AppendLine(">");

            foreach (var child in children)
            {
                ExportToXamlRecursive(child, sb, indent + 1);
            }

            sb.AppendLine($"{indentStr}</{typeName}>");
        }
    }

    /// <summary>
    /// Gets all visual children of an element, including adorner children
    /// and popup visual trees that VisualTreeHelper alone would miss.
    /// </summary>
    private static List<DependencyObject> GetAllVisualChildren(DependencyObject element)
    {
        var children = new List<DependencyObject>();

        // Standard visual children
        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child != null)
                children.Add(child);
        }

        // AdornerLayer: also enumerate adorners explicitly
        // Some adorners may not appear as standard visual children
        if (element is UIElement uiElement)
        {
            try
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(uiElement);
                if (adornerLayer != null)
                {
                    var adorners = adornerLayer.GetAdorners(uiElement);
                    if (adorners != null)
                    {
                        foreach (var adorner in adorners)
                        {
                            if (!children.Contains(adorner))
                                children.Add(adorner);
                        }
                    }
                }
            }
            catch
            {
                // AdornerLayer may not be available for all elements
            }
        }

        // Popup: traverse into the popup's separate visual tree
        if (element is Popup popup && popup.Child != null)
        {
            if (!children.Contains(popup.Child))
                children.Add(popup.Child);
        }

        return children;
    }

    /// <summary>
    /// Checks if an element has any visual children (including adorners/popups).
    /// </summary>
    private static bool HasAnyVisualChildren(DependencyObject element)
    {
        if (VisualTreeHelper.GetChildrenCount(element) > 0)
            return true;

        if (element is Popup popup && popup.Child != null)
            return true;

        return false;
    }

    /// <summary>
    /// Gets all root elements to search across, including all open windows and popups.
    /// </summary>
    public static List<DependencyObject> GetAllSearchRoots()
    {
        var roots = new List<DependencyObject>();
        var app = Application.Current;
        if (app == null) return roots;

        foreach (Window window in app.Windows)
        {
            roots.Add(window);
        }

        return roots;
    }

    private static string EscapeXml(string? text)
    {
        if (text == null) return string.Empty;
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static string EscapeJson(string? text)
    {
        if (text == null) return string.Empty;
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
