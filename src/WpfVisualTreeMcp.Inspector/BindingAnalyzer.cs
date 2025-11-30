using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Analyzes data bindings on WPF elements.
/// </summary>
public class BindingAnalyzer
{
    private readonly List<BindingErrorInfo> _capturedErrors = new();
    private TraceListener? _errorListener;

    /// <summary>
    /// Gets all bindings for an element.
    /// </summary>
    public string GetBindings(DependencyObject element)
    {
        var sb = new StringBuilder();
        sb.Append("{\"element\":");
        sb.Append(GetElementInfo(element));
        sb.Append(",\"bindings\":[");

        var bindings = GetAllBindings(element);
        var first = true;

        foreach (var binding in bindings)
        {
            if (!first) sb.Append(",");
            first = false;

            sb.Append("{");
            sb.Append($"\"property\":\"{EscapeJson(binding.Property.Name)}\"");
            sb.Append($",\"path\":\"{EscapeJson(binding.Binding.Path?.Path ?? "(none)")}\"");

            if (binding.Binding.Source != null)
            {
                sb.Append($",\"source\":\"{EscapeJson(binding.Binding.Source.GetType().Name)}\"");
            }
            else if (binding.Binding.RelativeSource != null)
            {
                sb.Append($",\"source\":\"RelativeSource({binding.Binding.RelativeSource.Mode})\"");
            }
            else if (binding.Binding.ElementName != null)
            {
                sb.Append($",\"source\":\"ElementName({EscapeJson(binding.Binding.ElementName)})\"");
            }
            else
            {
                sb.Append(",\"source\":\"DataContext\"");
            }

            sb.Append($",\"mode\":\"{binding.Binding.Mode}\"");

            if (binding.Binding.UpdateSourceTrigger != UpdateSourceTrigger.Default)
            {
                sb.Append($",\"updateTrigger\":\"{binding.Binding.UpdateSourceTrigger}\"");
            }

            var status = GetBindingStatus(binding.Expression);
            sb.Append($",\"status\":\"{status}\"");

            var currentValue = element.GetValue(binding.Property);
            sb.Append($",\"currentValue\":{FormatValue(currentValue)}");

            sb.Append("}");
        }

        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>
    /// Gets all binding errors captured from the application.
    /// </summary>
    public string GetBindingErrors()
    {
        var sb = new StringBuilder();
        sb.Append("{\"errors\":[");

        var first = true;
        foreach (var error in _capturedErrors)
        {
            if (!first) sb.Append(",");
            first = false;

            sb.Append("{");
            sb.Append($"\"elementType\":\"{EscapeJson(error.ElementType)}\"");
            if (error.ElementName != null)
            {
                sb.Append($",\"elementName\":\"{EscapeJson(error.ElementName)}\"");
            }
            sb.Append($",\"property\":\"{EscapeJson(error.Property)}\"");
            sb.Append($",\"bindingPath\":\"{EscapeJson(error.BindingPath)}\"");
            sb.Append($",\"errorType\":\"{EscapeJson(error.ErrorType)}\"");
            sb.Append($",\"message\":\"{EscapeJson(error.Message)}\"");
            sb.Append("}");
        }

        sb.Append($"],\"count\":{_capturedErrors.Count}}}");
        return sb.ToString();
    }

    /// <summary>
    /// Starts capturing binding errors from the trace output.
    /// </summary>
    public void StartCapturingErrors()
    {
        if (_errorListener != null) return;

        _errorListener = new BindingErrorTraceListener(_capturedErrors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(_errorListener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
    }

    /// <summary>
    /// Stops capturing binding errors.
    /// </summary>
    public void StopCapturingErrors()
    {
        if (_errorListener == null) return;

        PresentationTraceSources.DataBindingSource.Listeners.Remove(_errorListener);
        _errorListener.Dispose();
        _errorListener = null;
    }

    private IEnumerable<BindingData> GetAllBindings(DependencyObject element)
    {
        var bindings = new List<BindingData>();

        foreach (PropertyDescriptor pd in TypeDescriptor.GetProperties(element))
        {
            var dpd = DependencyPropertyDescriptor.FromProperty(pd);
            if (dpd?.DependencyProperty == null) continue;

            var binding = BindingOperations.GetBindingExpression(element, dpd.DependencyProperty);
            if (binding?.ParentBinding != null)
            {
                bindings.Add(new BindingData
                {
                    Property = dpd.DependencyProperty,
                    Binding = binding.ParentBinding,
                    Expression = binding
                });
            }
        }

        return bindings;
    }

    private string GetBindingStatus(BindingExpression expression)
    {
        if (expression.HasError)
        {
            return "Error";
        }

        return expression.Status switch
        {
            BindingStatus.Active => "Active",
            BindingStatus.Inactive => "Inactive",
            BindingStatus.Detached => "Detached",
            BindingStatus.PathError => "PathError",
            BindingStatus.UpdateTargetError => "UpdateTargetError",
            BindingStatus.UpdateSourceError => "UpdateSourceError",
            BindingStatus.AsyncRequestPending => "AsyncPending",
            BindingStatus.Unattached => "Unattached",
            _ => "Unknown"
        };
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

    private string FormatValue(object? value)
    {
        if (value == null) return "null";

        var type = value.GetType();
        if (type == typeof(string)) return $"\"{EscapeJson((string)value)}\"";
        if (type == typeof(bool)) return ((bool)value) ? "true" : "false";
        if (type.IsPrimitive || type == typeof(decimal)) return value.ToString() ?? "null";

        var str = value.ToString() ?? "";
        if (str.Length > 100) str = str.Substring(0, 100) + "...";
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

    private class BindingData
    {
        public DependencyProperty Property { get; set; } = null!;
        public Binding Binding { get; set; } = null!;
        public BindingExpression Expression { get; set; } = null!;
    }

    private class BindingErrorInfo
    {
        public string ElementType { get; set; } = string.Empty;
        public string? ElementName { get; set; }
        public string Property { get; set; } = string.Empty;
        public string BindingPath { get; set; } = string.Empty;
        public string ErrorType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    private class BindingErrorTraceListener : TraceListener
    {
        private readonly List<BindingErrorInfo> _errors;
        private readonly StringBuilder _buffer = new();

        public BindingErrorTraceListener(List<BindingErrorInfo> errors)
        {
            _errors = errors;
        }

        public override void Write(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                _buffer.Append(message);
            }
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                _buffer.Append(message);
            }

            if (_buffer.Length == 0) return;

            var fullMessage = _buffer.ToString();
            _buffer.Clear();

            // Parse the binding error message to extract useful information
            var error = ParseBindingError(fullMessage);
            if (error != null)
            {
                _errors.Add(error);
            }
        }

        private BindingErrorInfo? ParseBindingError(string message)
        {
            // Skip informational messages, only capture warnings/errors
            if (message.Contains("Information:")) return null;

            var error = new BindingErrorInfo
            {
                Message = TruncateMessage(message, 500)
            };

            // Determine error type
            if (message.Contains("Cannot find source"))
            {
                error.ErrorType = "SourceNotFound";
            }
            else if (message.Contains("path error") || message.Contains("BindingExpression path error"))
            {
                error.ErrorType = "PathError";
            }
            else if (message.Contains("Cannot convert"))
            {
                error.ErrorType = "ConversionError";
            }
            else if (message.Contains("ValidationError"))
            {
                error.ErrorType = "ValidationError";
            }
            else if (message.Contains("UpdateSourceExceptionFilter"))
            {
                error.ErrorType = "UpdateSourceError";
            }
            else
            {
                error.ErrorType = "Unknown";
            }

            // Try to extract binding path
            // Pattern: Path=PropertyName or Path='PropertyName.SubProperty'
            var pathMatch = System.Text.RegularExpressions.Regex.Match(
                message, @"Path[=:]'?([^';]+)'?");
            if (pathMatch.Success)
            {
                error.BindingPath = pathMatch.Groups[1].Value.Trim();
            }

            // Try to extract target element type
            // Pattern: target element is 'TypeName' (Name='elementName')
            var targetMatch = System.Text.RegularExpressions.Regex.Match(
                message, @"target element is '([^']+)'");
            if (targetMatch.Success)
            {
                error.ElementType = targetMatch.Groups[1].Value;
            }

            // Try to extract element name
            var nameMatch = System.Text.RegularExpressions.Regex.Match(
                message, @"\(Name='([^']+)'\)");
            if (nameMatch.Success)
            {
                error.ElementName = nameMatch.Groups[1].Value;
            }

            // Try to extract property name
            // Pattern: TargetProperty: PropertyName or target property is 'PropertyName'
            var propMatch = System.Text.RegularExpressions.Regex.Match(
                message, @"target property is '([^']+)'");
            if (propMatch.Success)
            {
                error.Property = propMatch.Groups[1].Value;
            }
            else
            {
                var propMatch2 = System.Text.RegularExpressions.Regex.Match(
                    message, @"TargetProperty[=:]'?([^';(]+)'?");
                if (propMatch2.Success)
                {
                    error.Property = propMatch2.Groups[1].Value.Trim();
                }
            }

            return error;
        }

        private static string TruncateMessage(string message, int maxLength)
        {
            if (message.Length <= maxLength) return message;
            return message.Substring(0, maxLength) + "...";
        }
    }
}
