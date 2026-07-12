using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Writes dependency property values at runtime and can undo those writes.
///
/// Each set snapshots enough of the prior state (a local binding, a local value, or
/// "no local value") to restore it exactly. Reverts are kept on a per-session stack so a
/// whole tweak experiment can be rolled back. All methods must run on the UI Dispatcher.
/// </summary>
public class PropertyWriter
{
    /// <summary>One undoable change to a single property on a single element.</summary>
    private sealed class Change
    {
        public DependencyObject Element { get; set; } = null!;
        public DependencyProperty Property { get; set; } = null!;
        public string PropertyName { get; set; } = string.Empty;
        public string ElementHandle { get; set; } = string.Empty;

        /// <summary>The binding that was on the property before we overwrote it, if any.</summary>
        public BindingBase? PreviousBinding { get; set; }

        /// <summary>The local value before the write (DependencyProperty.UnsetValue if none). Ignored when PreviousBinding is set.</summary>
        public object? PreviousLocalValue { get; set; }
    }

    private readonly List<Change> _undoStack = new();

    public readonly struct SetOutcome
    {
        public SetOutcome(string appliedValue, string valueType, string previousSource)
        {
            AppliedValue = appliedValue;
            ValueType = valueType;
            PreviousSource = previousSource;
        }

        /// <summary>The value read back after the write (the coerced result).</summary>
        public string AppliedValue { get; }
        public string ValueType { get; }

        /// <summary>What held the property before: "Binding", "Local", or "Unset".</summary>
        public string PreviousSource { get; }
    }

    /// <summary>
    /// Sets <paramref name="propertyName"/> on <paramref name="element"/> to the value parsed
    /// from <paramref name="valueString"/>, recording an undo entry. Throws with an actionable
    /// message when the property is missing, read-only, or the value can't be converted.
    /// </summary>
    public SetOutcome SetProperty(DependencyObject element, string elementHandle, string propertyName, string valueString)
    {
        var dp = ResolveProperty(element, propertyName)
            ?? throw new ArgumentException(
                $"'{propertyName}' is not a dependency property on {element.GetType().Name}. " +
                "Only dependency properties can be set at runtime.");

        if (dp.ReadOnly)
        {
            throw new InvalidOperationException(
                $"'{propertyName}' is a read-only dependency property and cannot be set.");
        }

        var converted = ConvertValue(dp, valueString);

        // Snapshot prior state for revert: a local binding takes precedence over a plain value.
        var previousBinding = BindingOperations.GetBindingBase(element, dp);
        var previousLocal = element.ReadLocalValue(dp);
        var previousSource = previousBinding != null
            ? "Binding"
            : (previousLocal == DependencyProperty.UnsetValue ? "Unset" : "Local");

        element.SetValue(dp, converted);

        _undoStack.Add(new Change
        {
            Element = element,
            Property = dp,
            PropertyName = propertyName,
            ElementHandle = elementHandle,
            PreviousBinding = previousBinding,
            PreviousLocalValue = previousLocal
        });

        var applied = element.GetValue(dp);
        return new SetOutcome(
            FormatValue(applied),
            dp.PropertyType.FullName ?? dp.PropertyType.Name,
            previousSource);
    }

    /// <summary>
    /// Reverts the most recent still-pending change, optionally filtered to a specific
    /// handle and/or property. Returns the reverted (handle, property) or null if nothing matched.
    /// </summary>
    public (string Handle, string Property)? RevertLast(string? elementHandle = null, string? propertyName = null)
    {
        for (int i = _undoStack.Count - 1; i >= 0; i--)
        {
            var c = _undoStack[i];
            if (elementHandle != null && c.ElementHandle != elementHandle) continue;
            if (propertyName != null && c.PropertyName != propertyName) continue;

            ApplyRevert(c);
            _undoStack.RemoveAt(i);
            return (c.ElementHandle, c.PropertyName);
        }
        return null;
    }

    /// <summary>Reverts all pending changes, newest first. Returns how many were reverted.</summary>
    public int RevertAll()
    {
        var count = _undoStack.Count;
        for (int i = _undoStack.Count - 1; i >= 0; i--)
        {
            ApplyRevert(_undoStack[i]);
        }
        _undoStack.Clear();
        return count;
    }

    public int PendingCount => _undoStack.Count;

    private static void ApplyRevert(Change c)
    {
        if (c.PreviousBinding != null)
        {
            // Restore the binding we overwrote.
            BindingOperations.SetBinding(c.Element, c.Property, c.PreviousBinding);
        }
        else if (c.PreviousLocalValue == DependencyProperty.UnsetValue)
        {
            // There was no local value: clearing falls back to style/inherited/default.
            c.Element.ClearValue(c.Property);
        }
        else
        {
            c.Element.SetValue(c.Property, c.PreviousLocalValue);
        }
    }

    private static DependencyProperty? ResolveProperty(DependencyObject element, string propertyName)
    {
        foreach (PropertyDescriptor pd in TypeDescriptor.GetProperties(element))
        {
            if (pd.Name == propertyName)
            {
                return DependencyPropertyDescriptor.FromProperty(pd)?.DependencyProperty;
            }
        }
        return null;
    }

    /// <summary>
    /// Converts a string to the property's type. Handles null/{null}, the property type's
    /// own TypeConverter (covers Thickness, Brush, Visibility, GridLength, Color, enums, ...),
    /// and a plain IConvertible fallback.
    /// </summary>
    private static object? ConvertValue(DependencyProperty dp, string valueString)
    {
        var targetType = dp.PropertyType;

        if (valueString == null || valueString == "{null}")
        {
            return null;
        }

        if (targetType == typeof(string))
        {
            return valueString;
        }

        try
        {
            var converter = TypeDescriptor.GetConverter(targetType);
            if (converter != null && converter.CanConvertFrom(typeof(string)))
            {
                return converter.ConvertFromString(null, CultureInfo.InvariantCulture, valueString);
            }
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"Could not convert '{valueString}' to {targetType.Name}: {ex.Message}");
        }

        try
        {
            return Convert.ChangeType(valueString, targetType, CultureInfo.InvariantCulture);
        }
        catch
        {
            throw new ArgumentException(
                $"No string conversion available for property type {targetType.Name}. " +
                "Provide a value its TypeConverter accepts (e.g. '10,0,10,0' for Thickness, " +
                "'Red' or '#FF0000' for Brush, 'Collapsed' for Visibility).");
        }
    }

    private static string FormatValue(object? value)
    {
        if (value == null) return "null";
        return value switch
        {
            Thickness t => $"({t.Left},{t.Top},{t.Right},{t.Bottom})",
            System.Windows.Media.Color c => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}",
            _ => value.ToString() ?? "null"
        };
    }
}
