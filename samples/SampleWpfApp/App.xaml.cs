using System;
using System.Windows;

namespace SampleWpfApp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private bool _inspectorStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_inspectorStarted)
        {
            // Clean up the inspector service
            SelfHostedInspector.Stop();
        }

        base.OnExit(e);
    }
}
