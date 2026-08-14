using System.Diagnostics;
using System.Runtime.CompilerServices;
using WpfVisualTreeMcp.Inspector;

namespace SampleWpfApp;

internal static class SelfHostedInspector
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Start()
    {
        InspectorService.Initialize(Process.GetCurrentProcess().Id);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Stop()
    {
        InspectorService.Instance?.Dispose();
    }
}
