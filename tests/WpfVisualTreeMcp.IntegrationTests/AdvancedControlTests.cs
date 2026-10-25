using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfVisualTreeMcp.Inspector;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class AdvancedControlTests
{
    [Fact]
    public Task Rejected_selection_state_is_reported_instead_of_succeeding() => DiagnosticReporterTests.OnSta(() =>
    {
        var list = new ListBox(); list.Items.Add("A"); list.Items.Add("B");
        list.SelectionChanged += (_, _) => { if (list.SelectedIndex == 1) list.SelectedIndex = 0; };
        var window = new Window { Content = list, Width = 200, Height = 150 }; window.Show(); window.UpdateLayout();
        try
        {
            var select = () => new ControlOperations(new TreeWalker()).Execute(list,
                new() { Operation = "select", Items = new() { ItemLocator.ByText("B") } });
            select.Should().Throw<InvalidOperationException>().WithMessage("*did not accept*");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Desired_states_are_idempotent_and_selection_is_exact_and_atomic_on_ambiguity() => DiagnosticReporterTests.OnSta(() =>
    {
        var box = new CheckBox { IsThreeState = true };
        var list = new ListBox { SelectionMode = SelectionMode.Multiple, Height = 100 };
        foreach (var item in new[] { "A", "AB", "duplicate", "duplicate" }) list.Items.Add(new ListBoxItem { Content = item });
        var expander = new Expander { Header = "Header", Content = "body" };
        var panel = new StackPanel(); panel.Children.Add(box); panel.Children.Add(list); panel.Children.Add(expander);
        var window = new Window { Content = panel, Width = 400, Height = 300 };
        window.Show(); window.UpdateLayout();
        try
        {
            var controls = new ControlOperations(new TreeWalker());
            var events = 0; box.Checked += (_, _) => events++;
            controls.Execute(box, new() { Operation = "check", Checked = true }).Result.Changed.Should().BeTrue();
            controls.Execute(box, new() { Operation = "check", Checked = true }).Result.Changed.Should().BeFalse();
            events.Should().Be(1);
            controls.Execute(box, new() { Operation = "check", Checked = null }); box.IsChecked.Should().BeNull();
            controls.Execute(box, new() { Operation = "check", Checked = false }); box.IsChecked.Should().BeFalse();
            controls.Execute(expander, new() { Operation = "expand" }); expander.IsExpanded.Should().BeTrue();
            controls.Execute(expander, new() { Operation = "collapse" }); expander.IsExpanded.Should().BeFalse();
            controls.Execute(list, new() { Operation = "select", Items = new() { ItemLocator.ByText("A"), ItemLocator.ByText("AB") } })
                .Result.SelectedIndices.Should().Equal(0, 1);
            var ambiguous = () => controls.Execute(list, new() { Operation = "select", Items = new() { ItemLocator.ByText("duplicate") } });
            ambiguous.Should().Throw<ArgumentException>().WithMessage("*Ambiguous*");
            list.SelectedItems.Count.Should().Be(2);
            controls.Execute(list, new() { Operation = "select", SelectionMode = "remove", Items = new() { ItemLocator.ByText("A") } })
                .Result.SelectedIndices.Should().Equal(1);
            controls.Execute(list, new() { Operation = "select" }).Result.SelectedIndices.Should().BeEmpty();
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Grid_realizes_offscreen_rows_respects_column_reordering_and_commits_bound_values() => DiagnosticReporterTests.OnSta(() =>
    {
        var data = Enumerable.Range(0, 500).Select(i => new Row { Id = i, Quantity = i.ToString() }).ToArray();
        var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = data, EnableRowVirtualization = true, EnableColumnVirtualization = true, CanUserAddRows = false };
        grid.Columns.Add(new DataGridTextColumn { Header = "Id", Binding = new Binding("Id"), IsReadOnly = true });
        grid.Columns.Add(new DataGridTextColumn { Header = "Quantity", Binding = new Binding("Quantity") { Mode = BindingMode.TwoWay } });
        grid.Columns[1].DisplayIndex = 0;
        var window = new Window { Content = grid, Width = 400, Height = 200 }; window.Show(); window.UpdateLayout();
        try
        {
            grid.ItemContainerGenerator.ContainerFromIndex(490).Should().BeNull("the row starts virtualized");
            var controls = new ControlOperations(new TreeWalker());
            var request = new ControlOperationRequest { Operation = "grid_edit", Row = ItemLocator.ByKey("Id", "490"), Column = ColumnLocator.ByHeader("Quantity"), Value = "loaded" };
            var result = controls.Execute(grid, request);
            result.NotReady.Should().BeFalse(result.Reason);
            result.Result.ColumnIndex.Should().Be(1); result.Result.RowIndex.Should().Be(490);
            data[490].Quantity.Should().Be("loaded");
            result.Result.Value.Should().Be("loaded");
            request.Operation = "grid_read";
            controls.Execute(grid, request).Result.Value.Should().Be("loaded");
            request.Column = ColumnLocator.ByBinding("Id"); request.Operation = "grid_edit";
            var readOnly = () => controls.Execute(grid, request);
            readOnly.Should().Throw<InvalidOperationException>().WithMessage("*read-only*");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Tree_path_expands_ancestors_and_selects_the_exact_child() => DiagnosticReporterTests.OnSta(() =>
    {
        var tree = new TreeView(); var parent = new TreeViewItem { Header = "parent" };
        var child = new TreeViewItem { Header = "child" }; parent.Items.Add(child); tree.Items.Add(parent);
        var window = new Window { Content = tree, Width = 300, Height = 200 }; window.Show(); window.UpdateLayout();
        try
        {
            var controls = new ControlOperations(new TreeWalker());
            var request = new ControlOperationRequest { Operation = "tree_select", Path = new() { ItemLocator.ByText("parent"), ItemLocator.ByText("child") } };
            controls.Execute(tree, request).NotReady.Should().BeFalse();
            parent.IsExpanded.Should().BeTrue(); child.IsSelected.Should().BeTrue();
            controls.Execute(tree, request).Result.Changed.Should().BeFalse();
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Property_watch_reports_previous_value_and_unsubscribe_removes_the_handler() => DiagnosticReporterTests.OnSta(() =>
    {
        var box = new TextBox { Text = "initial" }; using var watcher = new PropertyWatcher();
        var events = new List<PropertyChangedNotification>(); watcher.PropertyChanged += events.Add;
        var (id, initial) = watcher.Watch(box, "System.Windows.Controls.TextBox.Text"); initial.Should().Be("initial");
        box.Text = "one"; box.Text = "two";
        events.Select(e => (e.OldValue, e.NewValue)).Should().Equal(("initial", "one"), ("one", "two"));
        watcher.Unwatch(id); box.Text = "three"; events.Should().HaveCount(2);
        watcher.GetActiveWatches().Should().Be("[]");
    });

    public sealed class Row { public int Id { get; set; } public string Quantity { get; set; } = ""; }
}
