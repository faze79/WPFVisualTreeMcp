using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Performs interaction (clicks) on WPF elements.
///
/// UI Automation patterns are the default mechanism — they invoke the control's
/// action directly, raise proper events, need no window focus, and do not move the
/// mouse. An optional physical mode drives the real OS mouse for elements that have
/// no automation pattern but must still be clicked at their on-screen position.
///
/// All methods must be called on the UI dispatcher thread.
/// </summary>
internal sealed class ControlInteractor
{
    /// <summary>Describes how a click was carried out.</summary>
    public readonly struct ClickOutcome
    {
        public ClickOutcome(string method, string? detail)
        {
            Method = method;
            Detail = detail;
        }

        /// <summary>The mechanism used (Invoke, Toggle, Physical, ...).</summary>
        public string Method { get; }

        /// <summary>Optional extra detail (resulting toggle state, click coordinates, ...).</summary>
        public string? Detail { get; }
    }

    /// <summary>
    /// Clicks the given element. When <paramref name="physical"/> is false (default)
    /// the control's action is invoked via UI Automation; when true, a real OS mouse
    /// click is performed at the element's on-screen centre.
    /// </summary>
    public ClickOutcome Click(UIElement element, bool physical)
    {
        if (!element.IsEnabled)
            throw new InvalidOperationException("Element is disabled and cannot be clicked.");

        if (!element.IsVisible)
            throw new InvalidOperationException("Element is not visible and cannot be clicked.");

        return physical ? PhysicalClick(element) : AutomationClick(element);
    }

    /// <summary>
    /// Invokes the control via the first matching UI Automation pattern. Falls back to
    /// synthetic routed mouse events when the element exposes no pattern.
    /// </summary>
    private static ClickOutcome AutomationClick(UIElement element)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer != null)
        {
            // Buttons, menu items, hyperlinks, repeat buttons, ...
            if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
            {
                invoke.Invoke();
                return new ClickOutcome("Invoke", null);
            }

            // Check boxes, toggle buttons, radio buttons.
            if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
            {
                toggle.Toggle();
                return new ClickOutcome("Toggle", $"new toggle state: {toggle.ToggleState}");
            }

            // List items, combo box items, tab items, tree view items.
            if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selectionItem)
            {
                selectionItem.Select();
                return new ClickOutcome("SelectionItem.Select", null);
            }

            // Expanders, tree view items.
            if (peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expandCollapse)
            {
                if (expandCollapse.ExpandCollapseState == ExpandCollapseState.Collapsed)
                {
                    expandCollapse.Expand();
                    return new ClickOutcome("ExpandCollapse.Expand", null);
                }

                expandCollapse.Collapse();
                return new ClickOutcome("ExpandCollapse.Collapse", null);
            }
        }

        // No automation pattern (e.g. a Border/Grid/TextBlock with a custom MouseDown
        // handler): raise the routed mouse events directly on the element.
        return SyntheticMouseClick(element);
    }

    /// <summary>
    /// Best-effort click for elements with no UI Automation pattern: raises the
    /// left-button down/up routed event pair on the element itself.
    /// </summary>
    private static ClickOutcome SyntheticMouseClick(UIElement element)
    {
        var device = Mouse.PrimaryDevice;
        var timestamp = Environment.TickCount;

        void Raise(RoutedEvent routedEvent)
        {
            element.RaiseEvent(new MouseButtonEventArgs(device, timestamp, MouseButton.Left)
            {
                RoutedEvent = routedEvent,
                Source = element,
            });
        }

        Raise(UIElement.PreviewMouseLeftButtonDownEvent);
        Raise(UIElement.MouseLeftButtonDownEvent);
        Raise(UIElement.PreviewMouseLeftButtonUpEvent);
        Raise(UIElement.MouseLeftButtonUpEvent);

        return new ClickOutcome(
            "SyntheticMouse",
            "element exposes no UI Automation pattern; raised routed mouse events (best-effort)");
    }

    /// <summary>
    /// Performs a real OS mouse click at the element's on-screen centre. Brings the
    /// host window forward first so the click lands on the intended element.
    /// </summary>
    private static ClickOutcome PhysicalClick(UIElement element)
    {
        var size = element.RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
            throw new InvalidOperationException("Element has zero size and cannot be physically clicked.");

        Window.GetWindow(element)?.Activate();

        var centre = element.PointToScreen(new Point(size.Width / 2.0, size.Height / 2.0));
        var x = (int)Math.Round(centre.X);
        var y = (int)Math.Round(centre.Y);

        if (!NativeMethods.SetCursorPos(x, y))
            throw new InvalidOperationException($"SetCursorPos({x},{y}) failed — the point may be off-screen.");

        NativeMethods.MouseLeftClick();

        return new ClickOutcome("Physical", $"OS mouse click at screen ({x},{y})");
    }

    private static class NativeMethods
    {
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);

        public static void MouseLeftClick()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
        }
    }
}
