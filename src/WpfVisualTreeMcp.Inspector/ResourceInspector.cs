using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Inspects WPF resources and styles.
/// </summary>
public class ResourceInspector
{
    /// <summary>
    /// Gets resources based on the specified scope.
    /// </summary>
    /// <param name="scope">The scope: "application", "window", or "element".</param>
    /// <param name="element">The element for element-scoped resources.</param>
    /// <returns>JSON representation of resources.</returns>
    public string GetResources(string scope, FrameworkElement? element)
    {
        var sb = new StringBuilder();
        sb.Append("[");

        var resources = new List<string>();

        switch (scope?.ToLower())
        {
            case "application":
                CollectResources(Application.Current.Resources, "Application", resources);
                break;

            case "window":
                var window = element != null ? Window.GetWindow(element) : Application.Current.MainWindow;
                if (window != null)
                {
                    CollectResources(window.Resources, "Window", resources);
                }
                break;

            case "element":
                if (element != null)
                {
                    CollectElementResources(element, resources);
                }
                break;

            default:
                // Collect all resources
                CollectResources(Application.Current.Resources, "Application", resources);
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    CollectResources(mainWindow.Resources, "Window", resources);
                }
                break;
        }

        for (int i = 0; i < resources.Count; i++)
        {
            if (i > 0) sb.Append(",");
            sb.Append(resources[i]);
        }

        sb.Append("]");
        return sb.ToString();
    }

    /// <summary>
    /// Gets the style applied to an element.
    /// </summary>
    /// <param name="element">The element to inspect.</param>
    /// <returns>JSON representation of the style.</returns>
    public string GetStyle(FrameworkElement element)
    {
        var sb = new StringBuilder();
        sb.Append("{");

        var style = element.Style;
        if (style == null)
        {
            sb.Append("\"hasStyle\":false");
        }
        else
        {
            sb.Append("\"hasStyle\":true");
            sb.Append(",\"style\":{");

            // Target type
            if (style.TargetType != null)
            {
                sb.Append($"\"targetType\":\"{EscapeJson(style.TargetType.Name)}\"");
            }

            // Based on
            if (style.BasedOn != null)
            {
                sb.Append($",\"basedOn\":\"{EscapeJson(style.BasedOn.TargetType?.Name ?? "Unknown")}\"");
            }

            // Setters
            sb.Append(",\"setters\":[");
            var first = true;
            foreach (var setter in style.Setters)
            {
                if (setter is Setter s)
                {
                    if (!first) sb.Append(",");
                    first = false;

                    sb.Append("{");
                    sb.Append($"\"property\":\"{EscapeJson(s.Property?.Name ?? "Unknown")}\"");
                    sb.Append($",\"value\":{FormatValue(s.Value)}");
                    sb.Append("}");
                }
            }
            sb.Append("]");

            // Triggers
            sb.Append(",\"triggers\":[");
            first = true;
            foreach (var trigger in style.Triggers)
            {
                if (!first) sb.Append(",");
                first = false;

                sb.Append("{");
                if (trigger is Trigger t)
                {
                    sb.Append($"\"type\":\"Trigger\"");
                    sb.Append($",\"property\":\"{EscapeJson(t.Property?.Name ?? "Unknown")}\"");
                    sb.Append($",\"value\":{FormatValue(t.Value)}");
                }
                else if (trigger is DataTrigger dt)
                {
                    sb.Append($"\"type\":\"DataTrigger\"");
                    sb.Append($",\"binding\":\"{EscapeJson(dt.Binding?.ToString() ?? "Unknown")}\"");
                    sb.Append($",\"value\":{FormatValue(dt.Value)}");
                }
                else if (trigger is MultiTrigger)
                {
                    sb.Append($"\"type\":\"MultiTrigger\"");
                }
                else if (trigger is EventTrigger et)
                {
                    sb.Append($"\"type\":\"EventTrigger\"");
                    sb.Append($",\"routedEvent\":\"{EscapeJson(et.RoutedEvent?.Name ?? "Unknown")}\"");
                }
                else
                {
                    sb.Append($"\"type\":\"{EscapeJson(trigger.GetType().Name)}\"");
                }
                sb.Append("}");
            }
            sb.Append("]");

            sb.Append("}");
        }

        // Also get implicit style if different
        var implicitStyle = element.TryFindResource(element.GetType()) as Style;
        if (implicitStyle != null && implicitStyle != style)
        {
            sb.Append(",\"implicitStyleAvailable\":true");
        }

        sb.Append("}");
        return sb.ToString();
    }

    private void CollectResources(ResourceDictionary dictionary, string source, List<string> resources)
    {
        if (dictionary == null) return;

        foreach (var key in dictionary.Keys)
        {
            try
            {
                var value = dictionary[key];
                var keyStr = key?.ToString() ?? "(null)";
                var typeName = value?.GetType().Name ?? "null";

                var sb = new StringBuilder();
                sb.Append("{");
                sb.Append($"\"key\":\"{EscapeJson(keyStr)}\"");
                sb.Append($",\"typeName\":\"{EscapeJson(typeName)}\"");
                sb.Append($",\"value\":{FormatValue(value)}");
                sb.Append($",\"source\":\"{EscapeJson(source)}\"");
                sb.Append("}");
                resources.Add(sb.ToString());
            }
            catch
            {
                // Skip resources that can't be read
            }
        }

        // Include merged dictionaries
        foreach (var merged in dictionary.MergedDictionaries)
        {
            var mergedSource = merged.Source?.ToString() ?? $"{source}/Merged";
            CollectResources(merged, mergedSource, resources);
        }
    }

    private void CollectElementResources(FrameworkElement element, List<string> resources)
    {
        var current = element as FrameworkElement;
        var depth = 0;

        while (current != null && depth < 50)
        {
            var source = $"{current.GetType().Name}";
            if (!string.IsNullOrEmpty(current.Name))
            {
                source += $"[{current.Name}]";
            }

            CollectResources(current.Resources, source, resources);

            current = current.Parent as FrameworkElement ??
                      System.Windows.Media.VisualTreeHelper.GetParent(current) as FrameworkElement;
            depth++;
        }

        // Also include application resources
        CollectResources(Application.Current.Resources, "Application", resources);
    }

    private string FormatValue(object? value)
    {
        if (value == null) return "null";

        var type = value.GetType();

        if (type == typeof(string))
            return $"\"{EscapeJson((string)value)}\"";
        if (type == typeof(bool))
            return ((bool)value) ? "true" : "false";
        if (type.IsPrimitive || type == typeof(decimal))
            return value.ToString() ?? "null";

        // For complex types, just return a brief description
        var str = value.ToString() ?? "";
        if (str.Length > 100) str = str.Substring(0, 100) + "...";

        // If ToString returns type name, indicate it's a complex object
        if (str == type.FullName || str == type.Name)
        {
            return $"\"[{type.Name}]\"";
        }

        return $"\"{EscapeJson(str)}\"";
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
