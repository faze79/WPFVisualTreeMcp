using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Walks the visual tree and logical tree of WPF elements.
/// </summary>
public class TreeWalker
{
    private readonly Dictionary<DependencyObject, string> _handleCache = new();
    private int _handleCounter;

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

        // Get children
        sb.Append(",\"children\":[");

        if (depth < maxDepth)
        {
            var childCount = VisualTreeHelper.GetChildrenCount(element);
            var first = true;

            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                if (child == null) continue;

                if (!first) sb.Append(",");
                first = false;

                WalkVisualTreeRecursive(child, sb, depth + 1, maxDepth, ref elementCount, ref maxDepthReached);
            }
        }
        else if (VisualTreeHelper.GetChildrenCount(element) > 0)
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
    /// </summary>
    public DependencyObject? ResolveHandle(string handle)
    {
        foreach (var kvp in _handleCache)
        {
            if (kvp.Value == handle)
            {
                return kvp.Key;
            }
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
        _handleCache[element] = handle;
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
    /// <param name="typeName">Optional type name to match.</param>
    /// <param name="elementName">Optional element name to match.</param>
    /// <param name="maxResults">Maximum number of results to return (default: 50, max: 10000).</param>
    /// <returns>JSON array of matching elements.</returns>
    public string FindElements(DependencyObject root, string? typeName, string? elementName, int maxResults = 50)
    {
        // Clamp maxResults to reasonable limit to prevent memory issues
        if (maxResults > 10000) maxResults = 10000;
        if (maxResults < 1) maxResults = 1;

        var results = new List<string>();
        FindElementsRecursive(root, typeName, elementName, results, maxResults);

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
    /// <param name="typeName">Optional type name to match.</param>
    /// <param name="elementName">Optional element name to match.</param>
    /// <returns>JSON array of matching elements.</returns>
    public string FindElementsDeep(DependencyObject root, string? typeName, string? elementName)
    {
        var results = new List<string>();
        FindElementsDeepRecursive(root, typeName, elementName, results);

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

    private void FindElementsDeepRecursive(DependencyObject element, string? typeName, string? elementName, List<string> results)
    {
        var fullTypeName = element.GetType().FullName ?? element.GetType().Name;
        var shortTypeName = element.GetType().Name;
        var name = GetElementName(element);

        bool matches = true;

        if (!string.IsNullOrEmpty(typeName))
        {
            matches = fullTypeName.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                      shortTypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase);
        }

        if (matches && !string.IsNullOrEmpty(elementName))
        {
            matches = name != null && name.IndexOf(elementName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        if (matches)
        {
            var handle = GetOrCreateHandle(element);
            var path = GetElementPath(element);

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"handle\":\"{handle}\"");
            sb.Append($",\"typeName\":\"{EscapeJson(fullTypeName)}\"");
            if (!string.IsNullOrEmpty(name))
            {
                sb.Append($",\"name\":\"{EscapeJson(name)}\"");
            }
            sb.Append($",\"path\":\"{EscapeJson(path)}\"");
            sb.Append("}");
            results.Add(sb.ToString());
        }

        // Continue traversing all children without limit
        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child != null)
            {
                FindElementsDeepRecursive(child, typeName, elementName, results);
            }
        }
    }

    private void FindElementsRecursive(DependencyObject element, string? typeName, string? elementName, List<string> results, int maxResults)
    {
        // Stop if we've reached the maximum number of results
        if (results.Count >= maxResults)
        {
            return;
        }

        var fullTypeName = element.GetType().FullName ?? element.GetType().Name;
        var shortTypeName = element.GetType().Name;
        var name = GetElementName(element);

        bool matches = true;

        if (!string.IsNullOrEmpty(typeName))
        {
            // Use IndexOf for .NET Framework 4.8 compatibility (no Contains with StringComparison)
            matches = fullTypeName.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                      shortTypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase);
        }

        if (matches && !string.IsNullOrEmpty(elementName))
        {
            matches = name != null && name.IndexOf(elementName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        if (matches)
        {
            var handle = GetOrCreateHandle(element);
            var path = GetElementPath(element);

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"handle\":\"{handle}\"");
            sb.Append($",\"typeName\":\"{EscapeJson(fullTypeName)}\"");
            if (!string.IsNullOrEmpty(name))
            {
                sb.Append($",\"name\":\"{EscapeJson(name)}\"");
            }
            sb.Append($",\"path\":\"{EscapeJson(path)}\"");
            sb.Append("}");
            results.Add(sb.ToString());

            // Stop if we've reached the maximum after adding this result
            if (results.Count >= maxResults)
            {
                return;
            }
        }

        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child != null)
            {
                FindElementsRecursive(child, typeName, elementName, results, maxResults);

                // Stop if we've reached the maximum during child traversal
                if (results.Count >= maxResults)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Exports the visual tree to XAML-like format.
    /// </summary>
    /// <param name="root">The root element to export from.</param>
    /// <returns>XAML representation of the visual tree.</returns>
    public string ExportToXaml(DependencyObject root)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<? xml version=\"1.0\" encoding=\"utf-8\" ?>");
        sb.AppendLine("<!-- Visual Tree Export -->");
        ExportToXamlRecursive(root, sb, 0);
        return sb.ToString();
    }

    private void ExportToXamlRecursive(DependencyObject element, StringBuilder sb, int indent)
    {
        var indentStr = new string(' ', indent * 2);
        var typeName = element.GetType().Name;
        var name = GetElementName(element);

        var childCount = VisualTreeHelper.GetChildrenCount(element);

        if (childCount == 0)
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

            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                if (child != null)
                {
                    ExportToXamlRecursive(child, sb, indent + 1);
                }
            }

            sb.AppendLine($"{indentStr}</{typeName}>");
        }
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
