using System;
using System.Windows;

namespace SampleWpfApp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
#if SELF_HOSTED_INSPECTOR
    private bool _inspectorStarted;
#endif

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if SELF_HOSTED_INSPECTOR
        _inspectorStarted = !string.Equals(
            Environment.GetEnvironmentVariable("WPF_VISUAL_TREE_MCP_SELF_HOSTED"),
            "false",
            StringComparison.OrdinalIgnoreCase);

        if (_inspectorStarted)
        {
            // Initialize the WPF Visual Tree Inspector
            // This enables the MCP server to inspect this application
            SelfHostedInspector.Start();
        }
#endif
    }

    protected override void OnExit(ExitEventArgs e)
    {
#if SELF_HOSTED_INSPECTOR
        if (_inspectorStarted)
        {
            // Clean up the inspector service
            SelfHostedInspector.Stop();
        }
#endif

        base.OnExit(e);
    }
}
