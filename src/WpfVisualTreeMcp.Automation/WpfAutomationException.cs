namespace WpfVisualTreeMcp.Automation;

/// <summary>The target application could not complete an inspection or action.</summary>
public class WpfAutomationException : Exception
{
    public WpfAutomationException(string message) : base(message) { }
    public WpfAutomationException(string message, Exception innerException) : base(message, innerException) { }
}
