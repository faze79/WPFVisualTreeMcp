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

    private static string EscapeJson(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
