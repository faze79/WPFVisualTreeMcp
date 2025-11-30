using System.Diagnostics;
using System.Windows;
using WpfVisualTreeMcp.Inspector;

namespace SampleWpfApp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize the WPF Visual Tree Inspector
        // This enables the MCP server to inspect this application
        InspectorService.Initialize(Process.GetCurrentProcess().Id);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Clean up the inspector service
        InspectorService.Instance?.Dispose();

        base.OnExit(e);
    }
}
