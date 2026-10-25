using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
#if SELF_HOST_INSPECTOR
using WpfVisualTreeMcp.Inspector;
#endif

namespace WpfVisualTreeMcp.Automation.TestApp;

public static class Program
{
    private static T Id<T>(T element, string id) where T : DependencyObject { AutomationProperties.SetAutomationId(element, id); return element; }
    [STAThread]
    public static int Main(string[] args)
    {
        var marker = Array.IndexOf(args, "--pid-file");
        if (marker >= 0) System.IO.File.WriteAllText(args[marker + 1], Process.GetCurrentProcess().Id.ToString());
        if (args.Contains("--exit")) return 17;
        if (args.Contains("--no-window")) { Thread.Sleep(60000); return 0; }
        var app = new Application();
        var layout = new DockPanel();
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); layout.Children.Add(top);
        var status = Id(new TextBox { Text = "initial" }, "Status"); top.Children.Add(status);
        top.Children.Add(Id(new TextBox { Text = string.Join("|", args) }, "Arguments"));
        top.Children.Add(Id(new TextBox { Text = Environment.CurrentDirectory }, "WorkingDirectory"));
        top.Children.Add(Id(new TextBox { Text = Environment.GetEnvironmentVariable("WPF_TEST_VALUE") ?? "" }, "EnvironmentValue"));
        var counter = Id(new TextBox { Text = "0" }, "CheckCount"); top.Children.Add(counter);
        var check = Id(new CheckBox { Content = "Enabled" }, "Check"); var checks = 0;
        check.Checked += (_, _) => counter.Text = (++checks).ToString(); top.Children.Add(check);
        var delayed = Id(new Button { Content = "Delayed save", IsEnabled = false }, "Delayed");
        delayed.Click += (_, _) => status.Text = "loaded"; top.Children.Add(delayed);
        var multi = Id(new ListBox { SelectionMode = SelectionMode.Multiple, Height = 65 }, "Multi");
        foreach (var text in new[] { "A", "AB", "C" }) multi.Items.Add(text); top.Children.Add(multi);
        var actions = new WrapPanel(); top.Children.Add(actions);
        var main = Id(new Window { Title = "Automation test app", Width = 850, Height = 750, Content = layout,
            Tag = Environment.Version.Major.ToString() }, "Main");
        Button Button(string name, Action action)
        {
            var button = Id(new Button { Content = name, Margin = new Thickness(3) }, name);
            button.Click += (_, _) => action(); actions.Children.Add(button); return button;
        }
        Button("Changes", () => { status.Text = "one"; status.Text = "two"; });
        Button("OpenDialog", () =>
        {
            var panel = new StackPanel(); var close = Id(new Button { Content = "OK" }, "DialogOk"); panel.Children.Add(close);
            panel.Children.Add(Id(new TextBox { Text = "dialog" }, "Status"));
            var dialog = Id(new Window { Title = "Confirmation", Owner = main, Content = panel, Width = 300, Height = 180 }, "Dialog");
            close.Click += (_, _) => dialog.Close(); dialog.ShowDialog(); status.Text = "dialog closed";
        });
        Button("OpenSecondary", () =>
        {
            var close = Id(new CheckBox { Content = "Allow close" }, "AllowClose");
            var window = Id(new Window { Title = "Secondary", Owner = main, Content = close, Width = 300, Height = 150 }, "Secondary");
            window.Closing += (_, e) => e.Cancel = close.IsChecked != true; window.Show();
        });
        var tree = Id(new TreeView { Width = 200 }, "Tree"); DockPanel.SetDock(tree, Dock.Left); layout.Children.Add(tree);
        VirtualizingPanel.SetIsVirtualizing(tree, true); VirtualizingPanel.SetVirtualizationMode(tree, VirtualizationMode.Recycling);
        for (var i = 0; i < 300; i++)
        {
            var node = new TreeViewItem { Header = "Node " + i };
            node.Items.Add(new TreeViewItem { Header = "Child " + i }); tree.Items.Add(node);
        }
        var rows = Enumerable.Range(0, 1000).Select(i => new Row { Id = i, Quantity = "qty-" + i }).ToArray();
        var grid = Id(new DataGrid { AutoGenerateColumns = false, ItemsSource = rows, EnableRowVirtualization = true, EnableColumnVirtualization = true, CanUserAddRows = false }, "Orders");
        grid.Columns.Add(new DataGridTextColumn { Header = "Id", Binding = new Binding("Id"), IsReadOnly = true });
        grid.Columns.Add(new DataGridTextColumn { Header = "Quantity", Binding = new Binding("Quantity") { Mode = BindingMode.TwoWay, ValidatesOnDataErrors = true } });
        grid.Columns[1].DisplayIndex = 0; layout.Children.Add(grid);
#if SELF_HOST_INSPECTOR
        if (!args.Contains("--inject")) InspectorService.Initialize(Process.GetCurrentProcess().Id);
#endif
        main.Loaded += (_, _) =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => { delayed.IsEnabled = true; timer.Stop(); }; timer.Start();
        };
        return app.Run(main);
    }
    public sealed class Row : IDataErrorInfo
    {
        public int Id { get; set; }
        public string Quantity { get; set; } = "";
        public string Error => "";
        public string this[string columnName] => columnName == "Quantity" && Quantity == "invalid" ? "Invalid quantity" : "";
    }
}
