using System.Diagnostics;
using System.Windows;
#if INSPECTOR_SELF_HOST
using WpfVisualTreeMcp.Inspector;
#endif

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
#if INSPECTOR_SELF_HOST
        InspectorService.Initialize(Process.GetCurrentProcess().Id);
#endif
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Clean up the inspector service
#if INSPECTOR_SELF_HOST
        InspectorService.Instance?.Dispose();
#endif

        base.OnExit(e);
    }
}
