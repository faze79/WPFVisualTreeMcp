namespace WpfVisualTreeMcp.Shared.Models;

/// <summary>Exactly one index, exact text, or property/value key. Indices follow the current items view.</summary>
public sealed class ItemLocator
{
    public int? Index { get; set; }
    public string? Text { get; set; }
    public string? KeyProperty { get; set; }
    public string? KeyValue { get; set; }

    public static ItemLocator At(int index) => new() { Index = index };
    public static ItemLocator ByText(string text) => new() { Text = text };
    public static ItemLocator ByKey(string property, string? value) => new() { KeyProperty = property, KeyValue = value };
    public void Validate()
    {
        if ((Index.HasValue ? 1 : 0) + (Text != null ? 1 : 0) + (KeyProperty != null ? 1 : 0) != 1 || Index < 0)
            throw new ArgumentException("An item requires exactly one nonnegative index, exact text or property key.");
        if (KeyProperty != null && (string.IsNullOrWhiteSpace(KeyProperty) || KeyProperty.Length > 256))
            throw new ArgumentException("A property key must contain 1-256 nonblank characters.");
        if (Text?.Length > 2048 || KeyValue?.Length > 2048 || KeyValue != null && KeyProperty == null)
            throw new ArgumentException("Invalid or oversized item locator.");
    }
}

public sealed class ColumnLocator
{
    /// <summary>Index in DataGrid.Columns, independent of display reordering.</summary>
    public int? Index { get; set; }
    public string? Header { get; set; }
    public string? BindingPath { get; set; }
    public static ColumnLocator At(int index) => new() { Index = index };
    public static ColumnLocator ByHeader(string header) => new() { Header = header };
    public static ColumnLocator ByBinding(string path) => new() { BindingPath = path };
    public void Validate()
    {
        if ((Index.HasValue ? 1 : 0) + (Header != null ? 1 : 0) + (BindingPath != null ? 1 : 0) != 1 || Index < 0
            || Header?.Length > 2048 || BindingPath?.Length > 256 || BindingPath != null && string.IsNullOrWhiteSpace(BindingPath))
            throw new ArgumentException("A column requires exactly one nonnegative index, exact header or binding path.");
    }
}

public sealed class ControlResult
{
    public bool Changed { get; set; }
    public string? Value { get; set; }
    public string? ElementHandle { get; set; }
    public int? RowIndex { get; set; }
    public int? ColumnIndex { get; set; }
    public int RowCount { get; set; }
    public List<GridColumnInfo> Columns { get; set; } = new();
    public List<int> SelectedIndices { get; set; } = new();
}

public sealed class GridColumnInfo
{
    public int Index { get; set; }
    public int DisplayIndex { get; set; }
    public string? Header { get; set; }
    public string? BindingPath { get; set; }
    public bool IsReadOnly { get; set; }
}

public sealed class WpfWindowInfo
{
    public string Handle { get; set; } = string.Empty;
    public string? AutomationId { get; set; }
    public string? Name { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? OwnerHandle { get; set; }
    public bool IsMain { get; set; }
    public bool IsActive { get; set; }
    public bool IsVisible { get; set; }
    public bool IsEnabled { get; set; }
    public string State { get; set; } = string.Empty;
}
