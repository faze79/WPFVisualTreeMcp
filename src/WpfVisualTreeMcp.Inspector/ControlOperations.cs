using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>Runs exclusively on the WPF Dispatcher. Never retries an edit or delivered input.</summary>
internal sealed class ControlOperations
{
    private readonly TreeWalker _walker;
    internal ControlOperations(TreeWalker walker) => _walker = walker;

    internal ControlOperationResponse Execute(DependencyObject element, ControlOperationRequest request)
    {
        request.Validate();
        try
        {
            if (element is not UIElement ui) throw new ArgumentException("The target must be a WPF UIElement.");
            if (!ui.IsVisible || !ui.IsEnabled) throw new ControlNotReadyException("Control is not visible and enabled.");
            var result = request.Operation switch
            {
                "check" => Check(ui, request.Checked),
                "expand" => Expand(ui, true),
                "collapse" => Expand(ui, false),
                "select" => Select(ui, request),
                "grid_info" => GridInfo(Require<DataGrid>(ui)),
                "grid_read" or "grid_edit" or "grid_cell" => Cell(Require<DataGrid>(ui), request),
                _ => Tree(Require<TreeView>(ui), request)
            };
            return new ControlOperationResponse { RequestId = request.RequestId, Result = result };
        }
        catch (ControlNotReadyException ex)
        {
            return new ControlOperationResponse { RequestId = request.RequestId, NotReady = true, Reason = ex.Message };
        }
    }

    private static T Require<T>(object value) where T : class => value as T
        ?? throw new ArgumentException($"Expected {typeof(T).Name}, found {value.GetType().Name}.");

    private static ControlResult Check(UIElement element, bool? value)
    {
        var toggle = Require<ToggleButton>(element);
        if (value == null && !toggle.IsThreeState) throw new ArgumentException("Indeterminate requires a three-state toggle.");
        var changed = toggle.IsChecked != value;
        if (changed)
        {
            var provider = UIElementAutomationPeer.CreatePeerForElement(toggle)?.GetPattern(PatternInterface.Toggle) as IToggleProvider
                ?? throw new ArgumentException("Control does not expose the Toggle automation pattern.");
            // A three-state checkbox can need two transitions. No further action once the state matches.
            for (var i = 0; i < 3 && toggle.IsChecked != value; i++) provider.Toggle();
            if (toggle.IsChecked != value) throw new InvalidOperationException("The control did not accept the requested checked state.");
        }
        return new ControlResult { Changed = changed, Value = toggle.IsChecked?.ToString().ToLowerInvariant() };
    }

    private static ControlResult Expand(UIElement element, bool expand)
    {
        var provider = UIElementAutomationPeer.CreatePeerForElement(element)?.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider
            ?? throw new ArgumentException("Control does not expose the ExpandCollapse automation pattern.");
        var desired = expand ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        if (provider.ExpandCollapseState == ExpandCollapseState.LeafNode)
            throw new ArgumentException("A leaf node cannot be expanded or collapsed.");
        var changed = provider.ExpandCollapseState != desired;
        if (changed) { if (expand) provider.Expand(); else provider.Collapse(); }
        if (provider.ExpandCollapseState != desired) throw new InvalidOperationException("Control did not accept the requested expansion state.");
        return new ControlResult { Changed = changed, Value = expand ? "expanded" : "collapsed" };
    }

    private static ControlResult Select(UIElement element, ControlOperationRequest request)
    {
        var selector = Require<Selector>(element);
        // Resolve the entire request before changing any selection; ambiguity never selects the first item.
        var indices = request.Items.Select(x => ResolveItem(selector, x)).ToArray();
        if (indices.Distinct().Count() != indices.Length) throw new ArgumentException("Selection contains duplicate items.");
        IList? selected = selector switch
        {
            ListBox list when list.SelectionMode != SelectionMode.Single => list.SelectedItems,
            DataGrid grid when grid.SelectionMode == DataGridSelectionMode.Extended && grid.SelectionUnit != DataGridSelectionUnit.Cell => grid.SelectedItems,
            _ => null
        };
        if (selector is DataGrid { SelectionUnit: DataGridSelectionUnit.Cell })
            throw new ArgumentException("Row selection requires FullRow or CellOrRowHeader selection units.");
        if (selected == null && (indices.Length > 1 || request.SelectionMode != "replace"))
            throw new ArgumentException("The control is not configured for multiple selection.");
        var before = SelectedIndices(selector);
        if (selected == null) selector.SetCurrentValue(Selector.SelectedIndexProperty, indices.Length == 0 ? -1 : indices[0]);
        else
        {
            var wanted = new HashSet<object>(indices.Select(i => selector.Items[i]));
            if (request.SelectionMode == "replace")
                foreach (var item in selected.Cast<object>().Where(x => !wanted.Contains(x)).ToArray()) selected.Remove(item);
            foreach (var index in indices)
            {
                var item = selector.Items[index];
                if (request.SelectionMode == "remove") selected.Remove(item);
                else if (!selected.Contains(item)) selected.Add(item);
            }
        }
        var after = SelectedIndices(selector);
        var expected = request.SelectionMode == "replace" ? indices.OrderBy(i => i) : request.SelectionMode == "add"
            ? before.Union(indices).OrderBy(i => i) : before.Except(indices).OrderBy(i => i);
        if (!after.SequenceEqual(expected)) throw new InvalidOperationException("The control did not accept the requested selection state.");
        return new ControlResult { Changed = !before.SequenceEqual(after), SelectedIndices = after };
    }

    private static List<int> SelectedIndices(Selector selector)
    {
        IEnumerable<object> items = selector switch
        {
            ListBox l => l.SelectedItems.Cast<object>(),
            DataGrid g => g.SelectedItems.Cast<object>(),
            _ => selector.SelectedIndex < 0 ? Array.Empty<object>() : new[] { selector.SelectedItem }
        };
        return items.Select(x => selector.Items.IndexOf(x)).OrderBy(x => x).ToList();
    }

    private static ControlResult GridInfo(DataGrid grid) => new()
    {
        RowCount = grid.Items.Cast<object>().Count(x => x != CollectionView.NewItemPlaceholder),
        SelectedIndices = SelectedIndices(grid),
        Columns = grid.Columns.Select((column, index) => new GridColumnInfo
        {
            Index = index, DisplayIndex = column.DisplayIndex, Header = Display(column.Header),
            BindingPath = (column as DataGridBoundColumn)?.Binding is Binding binding ? binding.Path?.Path : null,
            IsReadOnly = grid.IsReadOnly || column.IsReadOnly
        }).ToList()
    };

    private ControlResult Cell(DataGrid grid, ControlOperationRequest request)
    {
        var rowIndex = ResolveItem(grid, request.Row!);
        var column = ResolveColumn(grid, request.Column!);
        if (column.Visibility != Visibility.Visible) throw new ArgumentException("The selected column is hidden.");
        if (request.Operation == "grid_edit" && (grid.IsReadOnly || column.IsReadOnly))
            throw new InvalidOperationException("The grid or column is read-only.");
        if (request.Operation == "grid_edit" && column is not (DataGridTextColumn or DataGridTemplateColumn))
            throw new ArgumentException("Text editing requires a DataGridTextColumn or a template with one TextBox editor.");
        var item = grid.Items[rowIndex];
        grid.ScrollIntoView(item, column);
        grid.UpdateLayout();
        var row = grid.ItemContainerGenerator.ContainerFromIndex(rowIndex) as DataGridRow;
        if (row == null) throw new ControlNotReadyException("Virtualized grid row has not been realized yet.");
        row.ApplyTemplate();
        var presenter = Descendants(row).OfType<DataGridCellsPresenter>().FirstOrDefault();
        presenter?.ApplyTemplate();
        grid.UpdateLayout();
        var cell = Descendants(row).OfType<DataGridCell>().FirstOrDefault(c => ReferenceEquals(c.Column, column));
        if (cell == null) throw new ControlNotReadyException("Virtualized grid cell has not been realized yet.");
        if (!cell.IsVisible || !cell.IsEnabled) throw new ControlNotReadyException("Grid cell is not visible and enabled.");
        // All retryable preparation ends here. Editing below is performed at most once.
        var before = CellText(cell);
        if (request.Operation == "grid_edit")
        {
            grid.CurrentCell = new DataGridCellInfo(item, column);
            if (!grid.BeginEdit()) throw new InvalidOperationException("WPF rejected BeginEdit (read-only state, validation or an application handler).");
            cell.UpdateLayout();
            var editors = Descendants(cell).OfType<TextBox>().Where(x => x.IsVisible).ToArray();
            if (editors.Length != 1)
            {
                grid.CancelEdit(DataGridEditingUnit.Cell);
                throw new InvalidOperationException("The cell must expose exactly one TextBox editor.");
            }
            var editor = editors[0];
            if (editor.IsReadOnly) { grid.CancelEdit(DataGridEditingUnit.Cell); throw new InvalidOperationException("The cell editor is read-only."); }
            editor.SetCurrentValue(TextBox.TextProperty, request.Value);
            editor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (Validation.GetHasError(editor) || !grid.CommitEdit(DataGridEditingUnit.Cell, true)
                || !grid.CommitEdit(DataGridEditingUnit.Row, true))
                throw new InvalidOperationException("WPF rejected the cell edit: " + string.Join("; ", Validation.GetErrors(editor).Select(x => x.ErrorContent)));
            grid.UpdateLayout();
        }
        return new ControlResult
        {
            RowIndex = rowIndex, ColumnIndex = grid.Columns.IndexOf(column),
            ElementHandle = _walker.GetOrCreateHandle(cell), Value = CellText(cell),
            Changed = request.Operation == "grid_edit" && before != CellText(cell)
        };
    }

    private static string? CellText(DataGridCell cell)
    {
        // Preserve whitespace/empty strings for ordinary text columns; searchable text is intentionally normalized.
        var content = cell.Content;
        if (content is TextBlock block) return block.Text;
        if (content is TextBox box) return box.Text;
        return TreeWalker.GetSearchableText(cell);
    }

    private ControlResult Tree(TreeView tree, ControlOperationRequest request)
    {
        ItemsControl parent = tree;
        TreeViewItem? node = null;
        for (var depth = 0; depth < request.Path.Count; depth++)
        {
            var index = ResolveItem(parent, request.Path[depth]);
            parent.ApplyTemplate(); parent.UpdateLayout();
            node = parent.ItemContainerGenerator.ContainerFromIndex(index) as TreeViewItem;
            if (node == null)
            {
                // Only use this parent's items host, never a nested child node's panel.
                var panel = Descendants(parent).OfType<VirtualizingPanel>()
                    .FirstOrDefault(p => ReferenceEquals(ItemsControl.GetItemsOwner(p), parent));
                panel?.BringIndexIntoViewPublic(index);
                parent.UpdateLayout();
                node = parent.ItemContainerGenerator.ContainerFromIndex(index) as TreeViewItem;
            }
            if (node == null) throw new ControlNotReadyException("Virtualized tree node has not been realized yet.");
            if (!node.IsEnabled) throw new ControlNotReadyException("Tree node or ancestor is disabled.");
            if (depth < request.Path.Count - 1)
            {
                node.SetCurrentValue(TreeViewItem.IsExpandedProperty, true);
                node.UpdateLayout();
            }
            parent = node;
        }
        node!.BringIntoView(); node.UpdateLayout();
        if (!node.IsVisible) throw new ControlNotReadyException("Tree node is not visible.");
        var changed = false;
        switch (request.Operation)
        {
            case "tree_expand": changed = !node.IsExpanded; node.SetCurrentValue(TreeViewItem.IsExpandedProperty, true); break;
            case "tree_collapse": changed = node.IsExpanded; node.SetCurrentValue(TreeViewItem.IsExpandedProperty, false); break;
            case "tree_select": changed = !node.IsSelected; node.SetCurrentValue(TreeViewItem.IsSelectedProperty, true); break;
            case "tree_node": break;
            default: throw new ArgumentException("Unsupported tree operation.");
        }
        if (request.Operation == "tree_select" && !node.IsSelected || request.Operation == "tree_expand" && !node.IsExpanded
            || request.Operation == "tree_collapse" && node.IsExpanded)
            throw new InvalidOperationException("The tree node did not accept the requested state.");
        return new ControlResult { Changed = changed, ElementHandle = _walker.GetOrCreateHandle(node), Value = Display(node.Header) };
    }

    internal static int ResolveItem(ItemsControl parent, ItemLocator locator)
    {
        locator.Validate();
        if (locator.Index is int index)
        {
            if (index >= parent.Items.Count || parent.Items[index] == CollectionView.NewItemPlaceholder)
                throw new ArgumentException($"Item index {index} is outside the current items view.");
            return index;
        }
        var match = -1;
        for (var i = 0; i < parent.Items.Count; i++)
        {
            var item = parent.Items[i];
            if (item == CollectionView.NewItemPlaceholder) continue;
            var value = locator.KeyProperty != null ? Format(ReadPath(item, locator.KeyProperty)) : ItemText(parent, item, i);
            if (!string.Equals(value, locator.KeyProperty != null ? locator.KeyValue : locator.Text, StringComparison.Ordinal)) continue;
            if (match >= 0) throw new ArgumentException("Ambiguous item locator: more than one item matches exactly. Use a unique property key.");
            match = i;
        }
        if (match < 0) throw new ControlNotReadyException("No item matches the exact text or property key.");
        return match;
    }

    private static string? ItemText(ItemsControl parent, object item, int index)
    {
        if (item is HeaderedItemsControl h) return Display(h.Header);
        if (item is ContentControl c) return Display(c.Content);
        if (!string.IsNullOrEmpty(parent.DisplayMemberPath)) return Format(ReadPath(item, parent.DisplayMemberPath));
        if (item is string s) return s;
        var formatted = Format(item);
        if (!string.IsNullOrEmpty(formatted) && formatted != item.GetType().Name && formatted != item.GetType().FullName) return formatted;
        // Prefer stable data over a realized/recycled template and never truncate an exact selector.
        foreach (var name in new[] { "Header", "Name", "Title", "Text", "DisplayName" })
            if (TypeDescriptor.GetProperties(item)[name] is PropertyDescriptor property) return Format(property.GetValue(item));
        if (parent.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container)
            return TreeWalker.GetSearchableText(container) ?? formatted;
        return formatted;
    }

    private static object? ReadPath(object? item, string path)
    {
        foreach (var part in path.Split('.'))
        {
            if (item == null) return null;
            var property = TypeDescriptor.GetProperties(item)[part]
                ?? throw new ArgumentException($"Property '{part}' does not exist on {item.GetType().Name}.");
            item = property.GetValue(item);
        }
        return item;
    }
    private static string? Format(object? value) => value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    private static string? Display(object? value) => value is DependencyObject d ? TreeWalker.GetSearchableText(d) : Format(value);

    private static DataGridColumn ResolveColumn(DataGrid grid, ColumnLocator locator)
    {
        locator.Validate();
        if (locator.Index is int index)
        {
            if (index >= grid.Columns.Count) throw new ArgumentException("Column index is outside DataGrid.Columns.");
            return grid.Columns[index];
        }
        var matches = grid.Columns.Where(c => locator.Header != null
            ? string.Equals(Display(c.Header), locator.Header, StringComparison.Ordinal)
            : c is DataGridBoundColumn { Binding: Binding b } && b.Path?.Path == locator.BindingPath).ToArray();
        if (matches.Length != 1) throw new ArgumentException($"Column locator matches {matches.Length} columns; exactly one is required.");
        return matches[0];
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>(); stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            if (current is not Visual) continue;
            for (var i = VisualTreeHelper.GetChildrenCount(current) - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(current, i));
        }
    }
}

internal sealed class ControlNotReadyException : Exception
{
    internal ControlNotReadyException(string message) : base(message) { }
}
