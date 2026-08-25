using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

internal sealed class IntegrationTheoryAttribute : TheoryAttribute
{
    public IntegrationTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WPF_VISUAL_TREE_MCP_INTEGRATION_SERVER")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WPF_VISUAL_TREE_MCP_INTEGRATION_SAMPLES")))
        {
            Skip = "Run tests/run-integration-tests.ps1 to prepare the native payload and sample matrix.";
        }
    }
}
