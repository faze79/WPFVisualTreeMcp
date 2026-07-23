using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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

    /// <summary>
    /// Explains an element's triggers (from its Style and ControlTemplate), each evaluated
    /// against the current state — the condition, the property's current value, whether the
    /// trigger is active, and the setters it applies. When <paramref name="propertyName"/> is
    /// given, also attributes that property's value: which style setter or active trigger set it.
    /// </summary>
    public string ExplainTriggers(FrameworkElement element, string? propertyName)
    {
        var sb = new StringBuilder();
        sb.Append("{\"element\":{");
        sb.Append($"\"typeName\":\"{EscapeJson(element.GetType().Name)}\"");
        if (!string.IsNullOrEmpty(element.Name)) sb.Append($",\"name\":\"{EscapeJson(element.Name)}\"");
        sb.Append("}");

        // Style triggers (walk the BasedOn chain).
        sb.Append(",\"styleTriggers\":[");
        var first = true;
        for (var style = element.Style; style != null; style = style.BasedOn)
        {
            var styleLabel = style.TargetType?.Name ?? "Style";
            foreach (var trg in style.Triggers)
            {
                if (!first) sb.Append(",");
                first = false;
                AppendEvaluatedTrigger(sb, trg, element, $"Style({styleLabel})");
            }
        }
        sb.Append("]");

        // ControlTemplate triggers — where most "why doesn't it react?" triggers live.
        sb.Append(",\"templateTriggers\":[");
        first = true;
        if (element is Control control && control.Template != null)
        {
            foreach (var trg in control.Template.Triggers)
            {
                if (!first) sb.Append(",");
                first = false;
                AppendEvaluatedTrigger(sb, trg, element, "ControlTemplate");
            }
        }
        sb.Append("]");

        // Optional: attribute one property's current value to its source.
        if (!string.IsNullOrEmpty(propertyName))
        {
            sb.Append(",\"attribution\":");
            AppendAttribution(sb, element, propertyName!);
        }
        else
        {
            sb.Append(",\"attribution\":null");
        }

        sb.Append("}");
        return sb.ToString();
    }

    private void AppendEvaluatedTrigger(StringBuilder sb, TriggerBase trigger, FrameworkElement element, string origin)
    {
        sb.Append("{");
        sb.Append($"\"origin\":\"{EscapeJson(origin)}\"");

        bool? active = null;

        switch (trigger)
        {
            case Trigger t:
            {
                sb.Append(",\"type\":\"Trigger\",\"conditions\":[");
                var current = t.Property != null ? element.GetValue(t.Property) : null;
                var isActive = t.Property != null && AreValuesEqual(current, t.Value);
                active = isActive;
                sb.Append("{");
                sb.Append($"\"property\":\"{EscapeJson(t.Property?.Name ?? "?")}\"");
                sb.Append($",\"expected\":{FormatValue(t.Value)}");
                sb.Append($",\"current\":{FormatValue(current)}");
                sb.Append($",\"matches\":{isActive.ToString().ToLowerInvariant()}");
                sb.Append("}]");
                AppendTriggerSetters(sb, t.Setters);
                break;
            }
            case MultiTrigger mt:
            {
                sb.Append(",\"type\":\"MultiTrigger\",\"conditions\":[");
                var allMatch = true;
                var cf = true;
                foreach (Condition c in mt.Conditions)
                {
                    if (!cf) sb.Append(",");
                    cf = false;
                    var current = c.Property != null ? element.GetValue(c.Property) : null;
                    var m = c.Property != null && AreValuesEqual(current, c.Value);
                    if (!m) allMatch = false;
                    sb.Append("{");
                    sb.Append($"\"property\":\"{EscapeJson(c.Property?.Name ?? "?")}\"");
                    sb.Append($",\"expected\":{FormatValue(c.Value)}");
                    sb.Append($",\"current\":{FormatValue(current)}");
                    sb.Append($",\"matches\":{m.ToString().ToLowerInvariant()}");
                    sb.Append("}");
                }
                sb.Append("]");
                active = allMatch;
                AppendTriggerSetters(sb, mt.Setters);
                break;
            }
            case DataTrigger dt:
            {
                sb.Append(",\"type\":\"DataTrigger\",\"conditions\":[{");
                var current = EvalBinding(dt.Binding, element);
                var m = AreValuesEqual(current, dt.Value);
                active = m;
                sb.Append($"\"binding\":\"{EscapeJson(dt.Binding?.ToString() ?? "?")}\"");
                sb.Append($",\"expected\":{FormatValue(dt.Value)}");
                sb.Append($",\"current\":{FormatValue(current)}");
                sb.Append($",\"matches\":{m.ToString().ToLowerInvariant()}");
                sb.Append("}]");
                AppendTriggerSetters(sb, dt.Setters);
                break;
            }
            case EventTrigger et:
                sb.Append($",\"type\":\"EventTrigger\",\"routedEvent\":\"{EscapeJson(et.RoutedEvent?.Name ?? "?")}\",\"note\":\"Event triggers run animations on an event; they have no active/inactive state.\"");
                break;
            default:
                sb.Append($",\"type\":\"{EscapeJson(trigger.GetType().Name)}\"");
                break;
        }

        if (active.HasValue)
            sb.Append($",\"active\":{active.Value.ToString().ToLowerInvariant()}");
        sb.Append("}");
    }

    private void AppendTriggerSetters(StringBuilder sb, SetterBaseCollection setters)
    {
        sb.Append(",\"setters\":[");
        var first = true;
        foreach (var sbase in setters)
        {
            if (sbase is Setter s)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append("{");
                sb.Append($"\"property\":\"{EscapeJson(s.Property?.Name ?? "?")}\"");
                sb.Append($",\"value\":{FormatValue(s.Value)}");
                sb.Append("}");
            }
        }
        sb.Append("]");
    }

    /// <summary>Attributes a property's current value to its source: a style setter, an active trigger, local, or default.</summary>
    private void AppendAttribution(StringBuilder sb, FrameworkElement element, string propertyName)
    {
        var dp = ResolveDependencyProperty(element, propertyName);
        if (dp == null)
        {
            sb.Append($"{{\"error\":\"'{EscapeJson(propertyName)}' is not a dependency property on {EscapeJson(element.GetType().Name)}\"}}");
            return;
        }

        var vs = DependencyPropertyHelper.GetValueSource(element, dp);
        var current = element.GetValue(dp);

        sb.Append("{");
        sb.Append($"\"property\":\"{EscapeJson(propertyName)}\"");
        sb.Append($",\"valueSource\":\"{vs.BaseValueSource}\"");
        sb.Append($",\"effectiveValue\":{FormatValue(current)}");
        sb.Append(",\"setBy\":");

        switch (vs.BaseValueSource)
        {
            case BaseValueSource.Style:
            case BaseValueSource.DefaultStyle:
                AppendStyleSetterSource(sb, element, dp);
                break;
            case BaseValueSource.StyleTrigger:
            case BaseValueSource.DefaultStyleTrigger:
            case BaseValueSource.TemplateTrigger:
            case BaseValueSource.ParentTemplateTrigger:
                AppendActiveTriggerSource(sb, element, dp, vs.BaseValueSource);
                break;
            case BaseValueSource.Local:
                sb.Append("{\"kind\":\"Local\",\"detail\":\"Set directly on the element (local value), overriding any style/trigger.\"}");
                break;
            case BaseValueSource.ParentTemplate:
                sb.Append("{\"kind\":\"ParentTemplate\",\"detail\":\"Set by the ControlTemplate that contains this element.\"}");
                break;
            case BaseValueSource.Inherited:
                sb.Append("{\"kind\":\"Inherited\",\"detail\":\"Inherited from an ancestor element.\"}");
                break;
            default:
                sb.Append($"{{\"kind\":\"{vs.BaseValueSource}\"}}");
                break;
        }

        sb.Append("}");
    }

    private void AppendStyleSetterSource(StringBuilder sb, FrameworkElement element, DependencyProperty dp)
    {
        for (var style = element.Style; style != null; style = style.BasedOn)
        {
            foreach (var sbase in style.Setters)
            {
                if (sbase is Setter s && s.Property == dp)
                {
                    sb.Append("{");
                    sb.Append($"\"kind\":\"StyleSetter\",\"style\":\"{EscapeJson(style.TargetType?.Name ?? "Style")}\"");
                    sb.Append($",\"setter\":\"{EscapeJson(dp.Name)}\",\"value\":{FormatValue(s.Value)}");
                    sb.Append("}");
                    return;
                }
            }
        }
        sb.Append("{\"kind\":\"StyleSetter\",\"detail\":\"Set by a style setter (exact setter not located; may come from an implicit or default style).\"}");
    }

    private void AppendActiveTriggerSource(StringBuilder sb, FrameworkElement element, DependencyProperty dp, BaseValueSource source)
    {
        // Search style triggers (+ BasedOn) and template triggers for an ACTIVE trigger with a setter for dp.
        var triggerLists = new List<(IEnumerable<TriggerBase> triggers, string origin)>();
        for (var style = element.Style; style != null; style = style.BasedOn)
            triggerLists.Add((System.Linq.Enumerable.Cast<TriggerBase>(style.Triggers), $"Style({style.TargetType?.Name})"));
        if (element is Control control && control.Template != null)
            triggerLists.Add((System.Linq.Enumerable.Cast<TriggerBase>(control.Template.Triggers), "ControlTemplate"));

        foreach (var (triggers, origin) in triggerLists)
        {
            foreach (var trg in triggers)
            {
                if (!TriggerIsActive(trg, element)) continue;
                var setters = GetTriggerSetters(trg);
                if (setters == null) continue;
                foreach (var sbase in setters)
                {
                    if (sbase is Setter s && s.Property == dp)
                    {
                        sb.Append("{");
                        sb.Append($"\"kind\":\"ActiveTrigger\",\"origin\":\"{EscapeJson(origin)}\"");
                        sb.Append($",\"condition\":\"{EscapeJson(DescribeTriggerCondition(trg))}\"");
                        sb.Append($",\"setter\":\"{EscapeJson(dp.Name)}\",\"value\":{FormatValue(s.Value)}");
                        sb.Append("}");
                        return;
                    }
                }
            }
        }
        sb.Append($"{{\"kind\":\"Trigger\",\"detail\":\"Value comes from {source} — see the active trigger in styleTriggers/templateTriggers above.\"}}");
    }

    private bool TriggerIsActive(TriggerBase trigger, FrameworkElement element)
    {
        switch (trigger)
        {
            case Trigger t:
                return t.Property != null && AreValuesEqual(element.GetValue(t.Property), t.Value);
            case MultiTrigger mt:
                foreach (Condition c in mt.Conditions)
                    if (c.Property == null || !AreValuesEqual(element.GetValue(c.Property), c.Value)) return false;
                return true;
            case DataTrigger dt:
                return AreValuesEqual(EvalBinding(dt.Binding, element), dt.Value);
            default:
                return false;
        }
    }

    private static SetterBaseCollection? GetTriggerSetters(TriggerBase trigger) => trigger switch
    {
        Trigger t => t.Setters,
        MultiTrigger mt => mt.Setters,
        DataTrigger dt => dt.Setters,
        _ => null
    };

    private string DescribeTriggerCondition(TriggerBase trigger) => trigger switch
    {
        Trigger t => $"{t.Property?.Name}={t.Value}",
        MultiTrigger mt => string.Join(" AND ", System.Linq.Enumerable.Select(
            System.Linq.Enumerable.Cast<Condition>(mt.Conditions), c => $"{c.Property?.Name}={c.Value}")),
        DataTrigger dt => $"{dt.Binding}={dt.Value}",
        _ => trigger.GetType().Name
    };

    /// <summary>Best-effort evaluation of a DataTrigger binding path against the element's DataContext.</summary>
    private static object? EvalBinding(BindingBase? bindingBase, FrameworkElement element)
    {
        if (bindingBase is not Binding b) return null;
        object? cur = b.Source ?? element.DataContext;
        var path = b.Path?.Path;
        if (string.IsNullOrWhiteSpace(path)) return cur;
        foreach (var seg in path.Split('.'))
        {
            if (cur == null) return null;
            var pi = cur.GetType().GetProperty(seg);
            if (pi == null) return null;
            cur = pi.GetValue(cur);
        }
        return cur;
    }

    private static bool AreValuesEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (Equals(a, b)) return true;
        // XAML-parsed trigger values are usually the right type; fall back to a string compare.
        return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }

    private static DependencyProperty? ResolveDependencyProperty(DependencyObject element, string propertyName)
    {
        foreach (PropertyDescriptor pd in TypeDescriptor.GetProperties(element))
        {
            if (pd.Name == propertyName)
                return DependencyPropertyDescriptor.FromProperty(pd)?.DependencyProperty;
        }
        return null;
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
