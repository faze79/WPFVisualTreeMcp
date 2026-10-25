using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Automation;

/// <summary>Standard WPF DataGrid operations. Row selectors refer to the current sorted/filtered items view.</summary>
public sealed class WpfDataGrid
{
    private readonly WpfElement _element;
    internal WpfDataGrid(WpfElement element) => _element = element;
    public Task<ControlResult> GetInfoAsync(CancellationToken cancellationToken = default) =>
        _element.ControlAsync(new() { Operation = "grid_info" }, cancellationToken);
    public WpfGridRow Row(int index) => new(_element, ItemLocator.At(index));
    public WpfGridRow RowByKey(string property, string? value) => new(_element, ItemLocator.ByKey(property, value));
    public WpfGridRow RowByText(string text) => new(_element, ItemLocator.ByText(text));
}

public sealed class WpfGridRow
{
    private readonly WpfElement _grid;
    private readonly ItemLocator _row;
    internal WpfGridRow(WpfElement grid, ItemLocator row) { row.Validate(); _grid = grid; _row = row; }
    public WpfGridCell Cell(string header) => new(_grid, _row, ColumnLocator.ByHeader(header));
    /// <summary>Index in DataGrid.Columns; remains valid when columns are visually reordered.</summary>
    public WpfGridCell Cell(int columnIndex) => new(_grid, _row, ColumnLocator.At(columnIndex));
    public WpfGridCell CellByBinding(string path) => new(_grid, _row, ColumnLocator.ByBinding(path));
    public Task<ControlResult> SelectAsync(CancellationToken cancellationToken = default) =>
        _grid.SelectItemsAsync(new[] { _row }, cancellationToken: cancellationToken);
}

public sealed class WpfGridCell
{
    private readonly WpfElement _grid;
    private readonly ItemLocator _row;
    private readonly ColumnLocator _column;
    internal WpfGridCell(WpfElement grid, ItemLocator row, ColumnLocator column)
    { column.Validate(); _grid = grid; _row = row; _column = column; }
    private ControlOperationRequest Request(string operation, string? value = null) =>
        new() { Operation = operation, Row = _row, Column = _column, Value = value };
    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default) =>
        (await _grid.ControlAsync(Request("grid_read"), cancellationToken).ConfigureAwait(false)).Value;
    /// <summary>Edits through the WPF TextBox binding and commits cell/row validation; it does not write the data object directly.</summary>
    public Task<ControlResult> SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _grid.ControlAsync(Request("grid_edit", text), cancellationToken);
    }
    /// <summary>A locator that realizes and resolves the cell again for every action, including gestures and waits.</summary>
    public WpfElement AsElement() => WpfElement.FromResolver(_grid, b => _grid.ResolveControlAsync(b, Request("grid_cell")));
}

public sealed class WpfTreeView
{
    private readonly WpfElement _tree;
    internal WpfTreeView(WpfElement tree) => _tree = tree;
    public WpfTreeNode Node(params string[] path) => NodeBy(path.Select(ItemLocator.ByText).ToArray());
    public WpfTreeNode NodeBy(params ItemLocator[] path) => new(_tree, path);
}

public sealed class WpfTreeNode
{
    private readonly WpfElement _tree;
    private readonly List<ItemLocator> _path;
    internal WpfTreeNode(WpfElement tree, IEnumerable<ItemLocator> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _tree = tree;
        _path = path.Select(p => new ItemLocator { Index = p.Index, Text = p.Text, KeyProperty = p.KeyProperty, KeyValue = p.KeyValue }).ToList();
        Request("tree_node").Validate();
    }
    private ControlOperationRequest Request(string operation) => new() { Operation = operation, Path = _path };
    public Task<ControlResult> ExpandAsync(CancellationToken cancellationToken = default) => _tree.ControlAsync(Request("tree_expand"), cancellationToken);
    public Task<ControlResult> CollapseAsync(CancellationToken cancellationToken = default) => _tree.ControlAsync(Request("tree_collapse"), cancellationToken);
    public Task<ControlResult> SelectAsync(CancellationToken cancellationToken = default) => _tree.ControlAsync(Request("tree_select"), cancellationToken);
    public WpfElement AsElement() => WpfElement.FromResolver(_tree, b => _tree.ResolveControlAsync(b, Request("tree_node")));
}
