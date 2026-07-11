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
    private readonly object _errorLock = new();
    private const int MaxCapturedErrors = 1000;
    private TraceListener? _errorListener;

    /// <summary>
    /// Gets all bindings for an element, including MultiBinding and detailed binding info.
    /// </summary>
    public string GetBindings(DependencyObject element)
    {
        var sb = new StringBuilder();
        sb.Append("{\"element\":");
        sb.Append(GetElementInfo(element));
        sb.Append(",\"bindings\":[");

        var bindings = GetAllBindings(element);
        var first = true;

        foreach (var bd in bindings)
        {
            if (!first) sb.Append(",");
            first = false;

            if (bd.IsMultiBinding)
            {
                AppendMultiBindingJson(sb, bd, element);
            }
            else
            {
                AppendBindingJson(sb, bd, element);
            }
        }

        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>
    /// Gets the DataContext chain for an element, walking up the visual/logical tree.
    /// </summary>
    public string GetDataContext(DependencyObject element)
    {
        var sb = new StringBuilder();
        sb.Append("{\"element\":");
        sb.Append(GetElementInfo(element));

        if (element is FrameworkElement fe)
        {
            var dc = fe.DataContext;
            sb.Append(",\"dataContext\":");
            AppendDataContextInfo(sb, dc);

            // Check if DataContext is inherited or local
            var source = DependencyPropertyHelper.GetValueSource(fe, FrameworkElement.DataContextProperty);
            sb.Append($",\"dataContextSource\":\"{source.BaseValueSource}\"");

            // Walk up the tree to show DataContext inheritance chain
            sb.Append(",\"inheritanceChain\":[");
            var chainFirst = true;
            var current = fe;
            var visited = new HashSet<DependencyObject>();

            while (current != null && visited.Add(current))
            {
                if (!chainFirst) sb.Append(",");
                chainFirst = false;

                var typeName = current.GetType().Name;
                var name = string.IsNullOrEmpty(current.Name) ? null : current.Name;
                var currentDc = current.DataContext;
                var dcSource = DependencyPropertyHelper.GetValueSource(current, FrameworkElement.DataContextProperty);

                sb.Append("{");
                sb.Append($"\"typeName\":\"{EscapeJson(typeName)}\"");
                if (name != null) sb.Append($",\"name\":\"{EscapeJson(name)}\"");
                sb.Append($",\"dataContextType\":\"{EscapeJson(currentDc?.GetType().FullName ?? "(null)")}\"");
                sb.Append($",\"source\":\"{dcSource.BaseValueSource}\"");

                // Check if this element has its own DataContext (Local or set explicitly)
                if (dcSource.BaseValueSource == BaseValueSource.Local ||
                    dcSource.BaseValueSource == BaseValueSource.Style ||
                    dcSource.BaseValueSource == BaseValueSource.StyleTrigger)
                {
                    sb.Append(",\"setsDataContext\":true");
                }

                sb.Append("}");

                // Walk up
                var parent = System.Windows.Media.VisualTreeHelper.GetParent(current);
                current = parent as FrameworkElement;
            }

            sb.Append("]");
        }
        else
        {
            sb.Append(",\"dataContext\":null");
            sb.Append(",\"dataContextSource\":\"N/A\"");
            sb.Append(",\"inheritanceChain\":[]");
        }

        sb.Append("}");
        return sb.ToString();
    }

    /// <summary>
    /// Gets all binding errors captured from the application.
    /// </summary>
    public string GetBindingErrors()
    {
        List<BindingErrorInfo> errorsCopy;
        lock (_errorLock)
        {
            errorsCopy = new List<BindingErrorInfo>(_capturedErrors);
        }

        var sb = new StringBuilder();
        sb.Append("{\"errors\":[");

        var first = true;
        foreach (var error in errorsCopy)
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
            sb.Append($",\"timestamp\":\"{error.Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ}\"");
            sb.Append("}");
        }

        sb.Append($"],\"count\":{errorsCopy.Count}}}");
        return sb.ToString();
    }

    /// <summary>
    /// Clears all captured binding errors.
    /// </summary>
    public void ClearBindingErrors()
    {
        lock (_errorLock)
        {
            _capturedErrors.Clear();
        }
    }

    /// <summary>
    /// Starts capturing binding errors from the trace output.
    /// </summary>
    public void StartCapturingErrors()
    {
        if (_errorListener != null) return;

        // Without Refresh(), WPF ignores runtime listener/switch changes unless
        // tracing was already enabled via app.config or the registry — the
        // listener would attach but never receive a single event.
        PresentationTraceSources.Refresh();

        _errorListener = new BindingErrorTraceListener(_capturedErrors, _errorLock, MaxCapturedErrors);
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

    #region Binding Enumeration

    private IEnumerable<BindingData> GetAllBindings(DependencyObject element)
    {
        var bindings = new List<BindingData>();

        foreach (PropertyDescriptor pd in TypeDescriptor.GetProperties(element))
        {
            var dpd = DependencyPropertyDescriptor.FromProperty(pd);
            if (dpd?.DependencyProperty == null) continue;

            // Check for single binding
            var bindingExpr = BindingOperations.GetBindingExpression(element, dpd.DependencyProperty);
            if (bindingExpr?.ParentBinding != null)
            {
                bindings.Add(new BindingData
                {
                    Property = dpd.DependencyProperty,
                    Binding = bindingExpr.ParentBinding,
                    Expression = bindingExpr,
                    IsMultiBinding = false
                });
                continue;
            }

            // Check for MultiBinding
            var multiExpr = BindingOperations.GetMultiBindingExpression(element, dpd.DependencyProperty);
            if (multiExpr?.ParentMultiBinding != null)
            {
                bindings.Add(new BindingData
                {
                    Property = dpd.DependencyProperty,
                    MultiBinding = multiExpr.ParentMultiBinding,
                    MultiExpression = multiExpr,
                    IsMultiBinding = true
                });
            }
        }

        return bindings;
    }

    #endregion

    #region JSON Serialization

    private void AppendBindingJson(StringBuilder sb, BindingData bd, DependencyObject element)
    {
        var binding = bd.Binding!;
        var expression = bd.Expression!;

        sb.Append("{");
        sb.Append($"\"property\":\"{EscapeJson(bd.Property.Name)}\"");
        sb.Append($",\"path\":\"{EscapeJson(binding.Path?.Path ?? "(none)")}\"");
        sb.Append(",\"type\":\"Binding\"");

        // Source
        AppendBindingSource(sb, binding);

        // Mode & trigger
        sb.Append($",\"mode\":\"{binding.Mode}\"");
        if (binding.UpdateSourceTrigger != UpdateSourceTrigger.Default)
            sb.Append($",\"updateTrigger\":\"{binding.UpdateSourceTrigger}\"");

        // Converter details
        if (binding.Converter != null)
        {
            sb.Append($",\"converter\":\"{EscapeJson(binding.Converter.GetType().Name)}\"");
            if (binding.ConverterParameter != null)
                sb.Append($",\"converterParameter\":{FormatValue(binding.ConverterParameter)}");
        }

        // StringFormat, FallbackValue, TargetNullValue
        if (!string.IsNullOrEmpty(binding.StringFormat))
            sb.Append($",\"stringFormat\":\"{EscapeJson(binding.StringFormat)}\"");
        if (binding.FallbackValue != DependencyProperty.UnsetValue && binding.FallbackValue != null)
            sb.Append($",\"fallbackValue\":{FormatValue(binding.FallbackValue)}");
        if (binding.TargetNullValue != DependencyProperty.UnsetValue && binding.TargetNullValue != null)
            sb.Append($",\"targetNullValue\":{FormatValue(binding.TargetNullValue)}");

        // IsAsync
        if (binding.IsAsync)
            sb.Append(",\"isAsync\":true");

        // Status & value
        var status = GetBindingStatus(expression);
        sb.Append($",\"status\":\"{status}\"");

        if (expression.HasError)
        {
            var validationError = System.Windows.Controls.Validation.GetErrors(element as DependencyObject);
            if (validationError?.Count > 0)
            {
                sb.Append($",\"validationError\":\"{EscapeJson(validationError[0].ErrorContent?.ToString() ?? "")}\"");
            }
        }

        var currentValue = element.GetValue(bd.Property);
        sb.Append($",\"currentValue\":{FormatValue(currentValue)}");

        sb.Append("}");
    }

    private void AppendMultiBindingJson(StringBuilder sb, BindingData bd, DependencyObject element)
    {
        var multi = bd.MultiBinding!;
        var multiExpr = bd.MultiExpression!;

        sb.Append("{");
        sb.Append($"\"property\":\"{EscapeJson(bd.Property.Name)}\"");
        sb.Append(",\"type\":\"MultiBinding\"");
        sb.Append($",\"mode\":\"{multi.Mode}\"");

        if (multi.Converter != null)
            sb.Append($",\"converter\":\"{EscapeJson(multi.Converter.GetType().Name)}\"");
        if (multi.ConverterParameter != null)
            sb.Append($",\"converterParameter\":{FormatValue(multi.ConverterParameter)}");
        if (!string.IsNullOrEmpty(multi.StringFormat))
            sb.Append($",\"stringFormat\":\"{EscapeJson(multi.StringFormat)}\"");

        // Child bindings
        sb.Append(",\"childBindings\":[");
        var first = true;
        for (int i = 0; i < multi.Bindings.Count; i++)
        {
            if (multi.Bindings[i] is Binding childBinding)
            {
                if (!first) sb.Append(",");
                first = false;

                sb.Append("{");
                sb.Append($"\"path\":\"{EscapeJson(childBinding.Path?.Path ?? "(none)")}\"");
                AppendBindingSource(sb, childBinding);
                sb.Append($",\"mode\":\"{childBinding.Mode}\"");

                // Get child binding status
                if (i < multiExpr.BindingExpressions.Count)
                {
                    var childExpr = multiExpr.BindingExpressions[i] as BindingExpression;
                    if (childExpr != null)
                    {
                        sb.Append($",\"status\":\"{GetBindingStatus(childExpr)}\"");
                    }
                }

                sb.Append("}");
            }
        }
        sb.Append("]");

        var currentValue = element.GetValue(bd.Property);
        sb.Append($",\"currentValue\":{FormatValue(currentValue)}");

        sb.Append("}");
    }

    private void AppendBindingSource(StringBuilder sb, Binding binding)
    {
        if (binding.Source != null)
        {
            var sourceType = binding.Source.GetType();
            sb.Append($",\"source\":\"{EscapeJson(sourceType.Name)}\"");
            sb.Append($",\"sourceType\":\"{EscapeJson(sourceType.FullName ?? sourceType.Name)}\"");

            // Check if source implements INotifyPropertyChanged
            if (binding.Source is System.ComponentModel.INotifyPropertyChanged)
                sb.Append(",\"sourceImplementsINPC\":true");
        }
        else if (binding.RelativeSource != null)
        {
            sb.Append($",\"source\":\"RelativeSource({binding.RelativeSource.Mode})\"");
            if (binding.RelativeSource.Mode == RelativeSourceMode.FindAncestor && binding.RelativeSource.AncestorType != null)
            {
                sb.Append($",\"ancestorType\":\"{EscapeJson(binding.RelativeSource.AncestorType.Name)}\"");
                if (binding.RelativeSource.AncestorLevel > 1)
                    sb.Append($",\"ancestorLevel\":{binding.RelativeSource.AncestorLevel}");
            }
        }
        else if (binding.ElementName != null)
        {
            sb.Append($",\"source\":\"ElementName({EscapeJson(binding.ElementName)})\"");
        }
        else
        {
            sb.Append(",\"source\":\"DataContext\"");
        }
    }

    private void AppendDataContextInfo(StringBuilder sb, object? dc)
    {
        if (dc == null)
        {
            sb.Append("null");
            return;
        }

        var type = dc.GetType();
        sb.Append("{");
        sb.Append($"\"type\":\"{EscapeJson(type.FullName ?? type.Name)}\"");
        sb.Append($",\"shortType\":\"{EscapeJson(type.Name)}\"");

        // Check INPC
        if (dc is System.ComponentModel.INotifyPropertyChanged)
            sb.Append(",\"implementsINPC\":true");
        else
            sb.Append(",\"implementsINPC\":false");

        // List public properties (useful for binding path validation)
        sb.Append(",\"properties\":[");
        var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var first = true;
        var count = 0;
        foreach (var prop in props)
        {
            if (count >= 50) // Cap at 50 properties
            {
                if (!first) sb.Append(",");
                sb.Append("{\"name\":\"...\",\"type\":\"(truncated)\"}");
                break;
            }
            if (!first) sb.Append(",");
            first = false;

            sb.Append("{");
            sb.Append($"\"name\":\"{EscapeJson(prop.Name)}\"");
            sb.Append($",\"type\":\"{EscapeJson(prop.PropertyType.Name)}\"");
            sb.Append($",\"canRead\":{(prop.CanRead ? "true" : "false")}");
            sb.Append($",\"canWrite\":{(prop.CanWrite ? "true" : "false")}");
            sb.Append("}");
            count++;
        }
        sb.Append("]");

        sb.Append("}");
    }

    #endregion

    #region Helpers

    private string GetBindingStatus(BindingExpression expression)
    {
        if (expression.HasError)
            return "Error";

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
            name = string.IsNullOrEmpty(fe.Name) ? null : fe.Name;

        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"typeName\":\"{EscapeJson(typeName)}\"");
        if (name != null)
            sb.Append($",\"name\":\"{EscapeJson(name)}\"");
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
        if (str.Length > 200) str = str.Substring(0, 200) + "...";
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

    #endregion

    #region Internal Types

    private class BindingData
    {
        public DependencyProperty Property { get; set; } = null!;
        public Binding? Binding { get; set; }
        public BindingExpression? Expression { get; set; }
        public MultiBinding? MultiBinding { get; set; }
        public MultiBindingExpression? MultiExpression { get; set; }
        public bool IsMultiBinding { get; set; }
    }

    internal class BindingErrorInfo
    {
        public string ElementType { get; set; } = string.Empty;
        public string? ElementName { get; set; }
        public string Property { get; set; } = string.Empty;
        public string BindingPath { get; set; } = string.Empty;
        public string ErrorType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    private class BindingErrorTraceListener : TraceListener
    {
        private readonly List<BindingErrorInfo> _errors;
        private readonly object _lock;
        private readonly int _maxErrors;
        private readonly StringBuilder _buffer = new();

        public BindingErrorTraceListener(List<BindingErrorInfo> errors, object errorLock, int maxErrors)
        {
            _errors = errors;
            _lock = errorLock;
            _maxErrors = maxErrors;
        }

        public override void Write(string? message)
        {
            if (!string.IsNullOrEmpty(message))
                _buffer.Append(message);
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
                _buffer.Append(message);

            if (_buffer.Length == 0) return;

            var fullMessage = _buffer.ToString();
            _buffer.Clear();

            var error = ParseBindingError(fullMessage);
            if (error != null)
            {
                lock (_lock)
                {
                    if (_errors.Count >= _maxErrors)
                        _errors.RemoveAt(0); // Remove oldest
                    _errors.Add(error);
                }
            }
        }

        private BindingErrorInfo? ParseBindingError(string message)
        {
            if (message.Contains("Information:")) return null;

            var error = new BindingErrorInfo
            {
                Message = TruncateMessage(message, 500),
                Timestamp = DateTime.UtcNow
            };

            // Determine error type
            if (message.Contains("Cannot find source"))
                error.ErrorType = "SourceNotFound";
            else if (message.Contains("path error") || message.Contains("BindingExpression path error"))
                error.ErrorType = "PathError";
            else if (message.Contains("Cannot convert"))
                error.ErrorType = "ConversionError";
            else if (message.Contains("ValidationError"))
                error.ErrorType = "ValidationError";
            else if (message.Contains("UpdateSourceExceptionFilter"))
                error.ErrorType = "UpdateSourceError";
            else
                error.ErrorType = "Unknown";

            // Extract binding path
            var pathMatch = System.Text.RegularExpressions.Regex.Match(
                message, @"Path[=:]'?([^';]+)'?");
            if (pathMatch.Success)
                error.BindingPath = pathMatch.Groups[1].Value.Trim();

            // Extract target element type
            var targetMatch = System.Text.RegularExpressions.Regex.Match(
                message, @"target element is '([^']+)'");
            if (targetMatch.Success)
                error.ElementType = targetMatch.Groups[1].Value;

            // Extract element name
            var nameMatch = System.Text.RegularExpressions.Regex.Match(
                message, @"\(Name='([^']+)'\)");
            if (nameMatch.Success)
                error.ElementName = nameMatch.Groups[1].Value;

            // Extract property name
            var propMatch = System.Text.RegularExpressions.Regex.Match(
                message, @"target property is '([^']+)'");
            if (propMatch.Success)
                error.Property = propMatch.Groups[1].Value;
            else
            {
                var propMatch2 = System.Text.RegularExpressions.Regex.Match(
                    message, @"TargetProperty[=:]'?([^';(]+)'?");
                if (propMatch2.Success)
                    error.Property = propMatch2.Groups[1].Value.Trim();
            }

            return error;
        }

        private static string TruncateMessage(string message, int maxLength)
        {
            if (message.Length <= maxLength) return message;
            return message.Substring(0, maxLength) + "...";
        }
    }

    #endregion
}
