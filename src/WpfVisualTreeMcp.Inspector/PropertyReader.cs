using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Reads dependency properties from WPF elements.
/// </summary>
public class PropertyReader
{
    /// <summary>
    /// Gets all dependency properties of an element.
    /// </summary>
    public string GetProperties(DependencyObject element)
    {
        var sb = new StringBuilder();
        sb.Append("{\"element\":");
        sb.Append(GetElementInfo(element));
        sb.Append(",\"properties\":[");

        var properties = GetDependencyProperties(element);
        var first = true;

        foreach (var dp in properties.OrderBy(p => p.Name))
        {
            if (!first) sb.Append(",");
            first = false;

            var value = element.GetValue(dp);
            var source = DependencyPropertyHelper.GetValueSource(element, dp);

            sb.Append("{");
            sb.Append($"\"name\":\"{EscapeJson(dp.Name)}\"");
            sb.Append($",\"typeName\":\"{EscapeJson(dp.PropertyType.FullName ?? dp.PropertyType.Name)}\"");
            sb.Append($",\"value\":{FormatValue(value)}");
            sb.Append($",\"source\":\"{GetSourceName(source.BaseValueSource)}\"");
            sb.Append($",\"isBinding\":{(source.IsExpression ? "true" : "false")}");
            sb.Append("}");
        }

        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>
    /// Gets a specific property value from an element.
    /// </summary>
    public object? GetPropertyValue(DependencyObject element, string propertyName)
    {
        var dp = GetDependencyProperty(element, propertyName);
        return dp != null ? element.GetValue(dp) : null;
    }

    /// <summary>
    /// Gets layout-specific properties.
    /// </summary>
    public string GetLayoutInfo(DependencyObject element)
    {
        if (element is not UIElement uiElement)
        {
            return "{\"error\": \"Element is not a UIElement\"}";
        }

        var sb = new StringBuilder();
        sb.Append("{\"element\":");
        sb.Append(GetElementInfo(element));
        sb.Append(",\"layout\":{");

        if (element is FrameworkElement fe)
        {
            sb.Append($"\"actualWidth\":{fe.ActualWidth}");
            sb.Append($",\"actualHeight\":{fe.ActualHeight}");
            sb.Append($",\"desiredSize\":{{\"width\":{fe.DesiredSize.Width},\"height\":{fe.DesiredSize.Height}}}");
            sb.Append($",\"renderSize\":{{\"width\":{fe.RenderSize.Width},\"height\":{fe.RenderSize.Height}}}");
            sb.Append($",\"margin\":{{\"left\":{fe.Margin.Left},\"top\":{fe.Margin.Top},\"right\":{fe.Margin.Right},\"bottom\":{fe.Margin.Bottom}}}");
            sb.Append($",\"horizontalAlignment\":\"{fe.HorizontalAlignment}\"");
            sb.Append($",\"verticalAlignment\":\"{fe.VerticalAlignment}\"");
            sb.Append($",\"visibility\":\"{fe.Visibility}\"");

            if (fe is System.Windows.Controls.Control control)
            {
                sb.Append($",\"padding\":{{\"left\":{control.Padding.Left},\"top\":{control.Padding.Top},\"right\":{control.Padding.Right},\"bottom\":{control.Padding.Bottom}}}");
            }
        }

        sb.Append("}}");
        return sb.ToString();
    }

    private string GetElementInfo(DependencyObject element)
    {
        var typeName = element.GetType().FullName ?? element.GetType().Name;
        string? name = null;

        if (element is FrameworkElement fe)
        {
            name = string.IsNullOrEmpty(fe.Name) ? null : fe.Name;
        }

        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"typeName\":\"{EscapeJson(typeName)}\"");
        if (name != null)
        {
            sb.Append($",\"name\":\"{EscapeJson(name)}\"");
        }
        sb.Append("}");
        return sb.ToString();
    }

    private IEnumerable<DependencyProperty> GetDependencyProperties(DependencyObject element)
    {
        var properties = new List<DependencyProperty>();
        var type = element.GetType();

        foreach (PropertyDescriptor pd in TypeDescriptor.GetProperties(element))
        {
            var dpd = DependencyPropertyDescriptor.FromProperty(pd);
            if (dpd?.DependencyProperty != null)
            {
                properties.Add(dpd.DependencyProperty);
            }
        }

        return properties.Distinct();
    }

    private DependencyProperty? GetDependencyProperty(DependencyObject element, string propertyName)
    {
        foreach (PropertyDescriptor pd in TypeDescriptor.GetProperties(element))
        {
            if (pd.Name == propertyName)
            {
                var dpd = DependencyPropertyDescriptor.FromProperty(pd);
                return dpd?.DependencyProperty;
            }
        }
        return null;
    }

    private string FormatValue(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        var type = value.GetType();

        // Handle primitives
        if (type == typeof(string))
        {
            return $"\"{EscapeJson((string)value)}\"";
        }
        if (type == typeof(bool))
        {
            return ((bool)value) ? "true" : "false";
        }
        if (type.IsPrimitive || type == typeof(decimal))
        {
            return value.ToString() ?? "null";
        }

        // Handle common WPF types
        if (value is Thickness thickness)
        {
            return $"\"({thickness.Left},{thickness.Top},{thickness.Right},{thickness.Bottom})\"";
        }
        if (value is System.Windows.Media.Color color)
        {
            return $"\"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}\"";
        }
        if (value is System.Windows.Media.Brush brush)
        {
            return $"\"{EscapeJson(brush.ToString())}\"";
        }

        // Default: use ToString
        var str = value.ToString();
        if (string.IsNullOrEmpty(str))
        {
            return "\"\"";
        }

        // Truncate very long values
        if (str.Length > 200)
        {
            str = str.Substring(0, 200) + "...";
        }

        return $"\"{EscapeJson(str)}\"";
    }

    private string GetSourceName(BaseValueSource source)
    {
        return source switch
        {
            BaseValueSource.Default => "Default",
            BaseValueSource.Inherited => "Inherited",
            BaseValueSource.DefaultStyle => "DefaultStyle",
            BaseValueSource.DefaultStyleTrigger => "DefaultStyleTrigger",
            BaseValueSource.Style => "Style",
            BaseValueSource.TemplateTrigger => "TemplateTrigger",
            BaseValueSource.StyleTrigger => "StyleTrigger",
            BaseValueSource.ImplicitStyleReference => "ImplicitStyle",
            BaseValueSource.ParentTemplate => "ParentTemplate",
            BaseValueSource.ParentTemplateTrigger => "ParentTemplateTrigger",
            BaseValueSource.Local => "Local",
            _ => "Unknown"
        };
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
