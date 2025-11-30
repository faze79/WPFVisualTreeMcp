using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Watches dependency properties for changes and sends notifications.
/// </summary>
public class PropertyWatcher : IDisposable
{
    private readonly Dictionary<string, WatchEntry> _watches = new();
    private bool _disposed;

    /// <summary>
    /// Event raised when a watched property changes.
    /// </summary>
    public event Action<PropertyChangedNotification>? PropertyChanged;

    /// <summary>
    /// Starts watching a property on an element.
    /// </summary>
    /// <param name="element">The element to watch.</param>
    /// <param name="propertyName">The property to watch.</param>
    /// <returns>A watch ID and the initial value.</returns>
    public (string watchId, string? initialValue) Watch(DependencyObject element, string propertyName)
    {
        var watchId = Guid.NewGuid().ToString("N");

        // Find the dependency property
        var dpd = FindDependencyProperty(element, propertyName);
        if (dpd == null)
        {
            throw new ArgumentException($"Property '{propertyName}' not found on element type '{element.GetType().Name}'");
        }

        var initialValue = element.GetValue(dpd.DependencyProperty);
        var initialValueStr = FormatValue(initialValue);

        // Add value changed handler
        EventHandler handler = (sender, args) =>
        {
            var newValue = element.GetValue(dpd.DependencyProperty);
            OnPropertyChanged(watchId, propertyName, initialValueStr, FormatValue(newValue));
        };

        dpd.AddValueChanged(element, handler);

        var entry = new WatchEntry
        {
            WatchId = watchId,
            Element = element,
            PropertyDescriptor = dpd,
            Handler = handler,
            LastValue = initialValueStr
        };

        _watches[watchId] = entry;

        return (watchId, initialValueStr);
    }

    /// <summary>
    /// Stops watching a property.
    /// </summary>
    /// <param name="watchId">The watch ID returned from Watch.</param>
    public void Unwatch(string watchId)
    {
        if (!_watches.TryGetValue(watchId, out var entry))
        {
            return;
        }

        entry.PropertyDescriptor.RemoveValueChanged(entry.Element, entry.Handler);
        _watches.Remove(watchId);
    }

    /// <summary>
    /// Stops watching all properties.
    /// </summary>
    public void UnwatchAll()
    {
        foreach (var entry in _watches.Values)
        {
            entry.PropertyDescriptor.RemoveValueChanged(entry.Element, entry.Handler);
        }
        _watches.Clear();
    }

    /// <summary>
    /// Gets all active watches.
    /// </summary>
    public string GetActiveWatches()
    {
        var watches = new List<object>();
        foreach (var kvp in _watches)
        {
            var entry = kvp.Value;
            var typeName = entry.Element.GetType().Name;
            string? elementName = null;
            if (entry.Element is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
            {
                elementName = fe.Name;
            }

            watches.Add(new
            {
                watchId = entry.WatchId,
                propertyName = entry.PropertyDescriptor.Name,
                elementType = typeName,
                elementName = elementName,
                currentValue = entry.LastValue
            });
        }

        return JsonSerializer.Serialize(watches);
    }

    private void OnPropertyChanged(string watchId, string propertyName, string? oldValue, string? newValue)
    {
        if (!_watches.TryGetValue(watchId, out var entry))
        {
            return;
        }

        entry.LastValue = newValue;

        var notification = new PropertyChangedNotification
        {
            WatchId = watchId,
            PropertyName = propertyName,
            OldValue = oldValue,
            NewValue = newValue,
            Timestamp = DateTime.UtcNow
        };

        PropertyChanged?.Invoke(notification);
    }

    private static DependencyPropertyDescriptor? FindDependencyProperty(DependencyObject element, string propertyName)
    {
        foreach (PropertyDescriptor pd in TypeDescriptor.GetProperties(element))
        {
            if (pd.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return DependencyPropertyDescriptor.FromProperty(pd);
            }
        }

        return null;
    }

    private static string? FormatValue(object? value)
    {
        if (value == null) return null;

        var type = value.GetType();

        if (type == typeof(string)) return (string)value;
        if (type == typeof(bool)) return ((bool)value).ToString().ToLower();
        if (type.IsPrimitive || type == typeof(decimal)) return value.ToString();

        // For complex types, return type name and a truncated ToString
        var str = value.ToString() ?? "";
        if (str.Length > 200)
        {
            str = str.Substring(0, 200) + "...";
        }

        // If ToString just returns type name, skip it
        if (str == type.FullName || str == type.Name)
        {
            return $"[{type.Name}]";
        }

        return str;
    }

    public void Dispose()
    {
        if (_disposed) return;
        UnwatchAll();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private class WatchEntry
    {
        public string WatchId { get; set; } = string.Empty;
        public DependencyObject Element { get; set; } = null!;
        public DependencyPropertyDescriptor PropertyDescriptor { get; set; } = null!;
        public EventHandler Handler { get; set; } = null!;
        public string? LastValue { get; set; }
    }
}
