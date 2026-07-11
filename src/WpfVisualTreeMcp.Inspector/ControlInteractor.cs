using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WpfVisualTreeMcp.Inspector;

/// <summary>
/// Performs interaction (clicks, text input, keyboard shortcuts) on WPF elements.
///
/// UI Automation patterns are the default mechanism — they invoke the control's
/// action directly, raise proper events, need no window focus, and do not move the
/// mouse. An optional physical mode drives the real OS mouse/keyboard for elements
/// that have no automation pattern but must still be driven at the OS input level.
///
/// All methods must be called on the UI dispatcher thread.
/// </summary>
internal sealed class ControlInteractor
{
    /// <summary>Describes how an interaction was carried out.</summary>
    public readonly struct InteractionOutcome
    {
        public InteractionOutcome(string method, string? detail)
        {
            Method = method;
            Detail = detail;
        }

        /// <summary>The mechanism used (Invoke, Toggle, Physical, ValueProvider.SetValue, ...).</summary>
        public string Method { get; }

        /// <summary>Optional extra detail (resulting toggle state, click coordinates, key combo, ...).</summary>
        public string? Detail { get; }
    }

    // ---------------------------------------------------------------------
    // Click
    // ---------------------------------------------------------------------

    /// <summary>
    /// Clicks the given element. When <paramref name="physical"/> is false (default)
    /// the control's action is invoked via UI Automation; when true, a real OS mouse
    /// click is performed at the element's on-screen centre.
    /// <paramref name="clickType"/> selects single (default), "double" or "right" —
    /// double and right clicks are OS-level notions, so they always use the physical path.
    /// </summary>
    public InteractionOutcome Click(UIElement element, bool physical, string? clickType = null)
    {
        EnsureInteractable(element, "clicked");

        var type = string.IsNullOrEmpty(clickType) ? "single" : clickType!.ToLowerInvariant();
        if (type != "single" && type != "double" && type != "right")
            throw new ArgumentException($"Unknown click_type '{clickType}'. Expected: single, double, or right.");

        if (type != "single")
            return PhysicalClick(element, type);

        return physical ? PhysicalClick(element, type) : AutomationClick(element);
    }

    private static InteractionOutcome AutomationClick(UIElement element)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer != null)
        {
            if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
            {
                invoke.Invoke();
                return new InteractionOutcome("Invoke", null);
            }

            if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
            {
                toggle.Toggle();
                return new InteractionOutcome("Toggle", $"new toggle state: {toggle.ToggleState}");
            }

            if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selectionItem)
            {
                selectionItem.Select();
                return new InteractionOutcome("SelectionItem.Select", null);
            }

            if (peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expandCollapse)
            {
                if (expandCollapse.ExpandCollapseState == ExpandCollapseState.Collapsed)
                {
                    expandCollapse.Expand();
                    return new InteractionOutcome("ExpandCollapse.Expand", null);
                }

                expandCollapse.Collapse();
                return new InteractionOutcome("ExpandCollapse.Collapse", null);
            }
        }

        return SyntheticMouseClick(element);
    }

    private static InteractionOutcome SyntheticMouseClick(UIElement element)
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

        return new InteractionOutcome(
            "SyntheticMouse",
            "element exposes no UI Automation pattern; raised routed mouse events (best-effort)");
    }

    private static InteractionOutcome PhysicalClick(UIElement element, string clickType = "single")
    {
        var size = element.RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
            throw new InvalidOperationException("Element has zero size and cannot be physically clicked.");

        Window.GetWindow(element)?.Activate();
        ScrollIntoView(element);

        var centre = element.PointToScreen(new Point(size.Width / 2.0, size.Height / 2.0));
        var x = (int)Math.Round(centre.X);
        var y = (int)Math.Round(centre.Y);

        if (!NativeMethods.SetCursorPos(x, y))
            throw new InvalidOperationException($"SetCursorPos({x},{y}) failed — the point may be off-screen.");

        switch (clickType)
        {
            case "double":
                NativeMethods.MouseLeftClick();
                NativeMethods.MouseLeftClick();
                return new InteractionOutcome("Physical.DoubleClick", $"OS mouse double-click at screen ({x},{y})");
            case "right":
                NativeMethods.MouseRightClick();
                return new InteractionOutcome("Physical.RightClick", $"OS mouse right-click at screen ({x},{y})");
            default:
                NativeMethods.MouseLeftClick();
                return new InteractionOutcome("Physical", $"OS mouse click at screen ({x},{y})");
        }
    }

    /// <summary>
    /// Scrolls the element into its ScrollViewer's viewport (if any) and forces a
    /// layout pass, so PointToScreen returns on-screen coordinates.
    /// </summary>
    private static void ScrollIntoView(UIElement element)
    {
        if (element is FrameworkElement fe)
        {
            fe.BringIntoView();
            fe.UpdateLayout();
        }
    }

    // ---------------------------------------------------------------------
    // Set text / fill value
    // ---------------------------------------------------------------------

    /// <summary>
    /// Replaces the text/value of <paramref name="element"/> with <paramref name="text"/>.
    /// Default: <see cref="IValueProvider.SetValue"/> via UI Automation, with a
    /// <c>TextBox.Text</c> / <c>PasswordBox.Password</c> / reflected <c>Text</c>
    /// fallback. With <paramref name="physical"/>=true, focuses the element and
    /// types the text via OS keyboard input (selecting and deleting any prior
    /// value first).
    /// </summary>
    public InteractionOutcome SetText(UIElement element, string text, bool physical)
    {
        EnsureInteractable(element, "given text");
        text ??= string.Empty;
        return physical ? PhysicalSetText(element, text) : AutomationSetText(element, text);
    }

    private static InteractionOutcome AutomationSetText(UIElement element, string text)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer?.GetPattern(PatternInterface.Value) is IValueProvider value)
        {
            if (value.IsReadOnly)
                throw new InvalidOperationException("Element value is read-only and cannot be set.");
            value.SetValue(text);
            return new InteractionOutcome("ValueProvider.SetValue", ReadBackDetail(value.Value, text));
        }

        // Direct-property fallbacks for the common cases that lack a pattern.
        if (element is TextBox tb)
        {
            tb.Text = text;
            return new InteractionOutcome("DirectProperty.Text", ReadBackDetail(tb.Text, text));
        }
        if (element is PasswordBox pb)
        {
            pb.Password = text;
            // Never echo passwords back.
            return new InteractionOutcome("DirectProperty.Password", $"length now: {pb.Password.Length}");
        }

        // Last resort: reflect for a settable string `Text` property (covers many
        // third-party controls that expose Text but no automation peer).
        var prop = element.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
        if (prop != null && prop.CanWrite && prop.PropertyType == typeof(string))
        {
            prop.SetValue(element, text);
            return new InteractionOutcome("Reflected.Text", ReadBackDetail(prop.GetValue(element) as string, text));
        }

        throw new InvalidOperationException(
            $"Element type '{element.GetType().Name}' has no IValueProvider and no settable string 'Text' property. " +
            "Try physical=true to type via OS keyboard input.");
    }

    /// <summary>
    /// Builds a detail string reporting the element's value after the write, so the
    /// caller can verify the outcome without an extra round-trip. Flags a mismatch
    /// (e.g. input validation or coercion changed the value).
    /// </summary>
    private static string ReadBackDetail(string? actual, string requested)
    {
        actual ??= string.Empty;
        var shown = actual.Length > 80 ? actual.Substring(0, 80) + "…" : actual;
        return actual == requested
            ? $"value now: '{shown}'"
            : $"value now: '{shown}' (differs from requested text — the control may coerce or validate input)";
    }

    private static InteractionOutcome PhysicalSetText(UIElement element, string text)
    {
        if (!FocusForKeyboardInput(element))
            throw new InvalidOperationException(
                "Element is not focusable; physical typing has nowhere to land. " +
                "Use the default UI Automation mode or set Focusable=true.");

        // Select-all + delete the existing content first.
        NativeMethods.KeyDown(NativeMethods.VK_CONTROL);
        NativeMethods.KeyDown((byte)'A');
        NativeMethods.KeyUp((byte)'A');
        NativeMethods.KeyUp(NativeMethods.VK_CONTROL);
        NativeMethods.KeyDown(NativeMethods.VK_DELETE);
        NativeMethods.KeyUp(NativeMethods.VK_DELETE);

        foreach (var c in text)
        {
            NativeMethods.SendUnicodeChar(c);
        }

        return new InteractionOutcome("Physical", $"typed {text.Length} char(s) after Ctrl+A/Delete");
    }

    // ---------------------------------------------------------------------
    // Select item (ComboBox / ListBox / TabControl / any Selector)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Selects an item in a Selector-derived control (ComboBox, ListBox, ListView,
    /// TabControl, ...) by visible text or by index. Works on virtualized items too,
    /// because it drives the Items collection rather than item containers (which do
    /// not exist in the visual tree until realized).
    /// </summary>
    public InteractionOutcome SelectItem(UIElement element, string? itemText, int? index)
    {
        EnsureInteractable(element, "used for selection");

        if (element is not Selector selector)
            throw new InvalidOperationException(
                $"Element type '{element.GetType().Name}' is not a Selector (ComboBox, ListBox, ListView, TabControl, ...). " +
                "Use wpf_click_element for buttons/checkboxes or wpf_set_text for editable text.");

        if (index is null && string.IsNullOrEmpty(itemText))
            throw new ArgumentException("Provide item_text or index to choose the item to select.");

        var count = selector.Items.Count;
        if (count == 0)
            throw new InvalidOperationException("The control has no items to select.");

        if (index is int i)
        {
            if (i < 0 || i >= count)
                throw new ArgumentOutOfRangeException(nameof(index), $"index {i} is out of range; the control has {count} item(s).");
            selector.SelectedIndex = i;
            return new InteractionOutcome(
                "Selector.SelectedIndex",
                $"selected index {i}: '{DescribeItem(selector, i)}'");
        }

        for (int j = 0; j < count; j++)
        {
            var text = DescribeItem(selector, j);
            if (text.IndexOf(itemText, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                selector.SelectedIndex = j;
                return new InteractionOutcome(
                    "Selector.SelectedIndex",
                    $"selected index {j}: '{text}' (matched '{itemText}')");
            }
        }

        var preview = string.Join(", ", Enumerable.Range(0, Math.Min(count, 10))
            .Select(j => $"'{DescribeItem(selector, j)}'"));
        throw new InvalidOperationException(
            $"No item matching '{itemText}' among {count} item(s). " +
            $"First items: [{preview}{(count > 10 ? ", …" : "")}]");
    }

    /// <summary>
    /// Human-readable text for the item at <paramref name="index"/> — what the user
    /// sees in the dropdown/list. Tries, in order: container Content, DisplayMemberPath,
    /// an overridden ToString(), the realized item container's visible text (covers
    /// ItemTemplate over data objects), and common display properties.
    /// </summary>
    private static string DescribeItem(Selector selector, int index)
    {
        var item = selector.Items[index];
        if (item == null) return "(null)";

        if (item is ContentControl cc && cc.Content != null)
            return cc.Content.ToString() ?? cc.GetType().Name;

        if (!string.IsNullOrEmpty(selector.DisplayMemberPath))
        {
            var prop = item.GetType().GetProperty(selector.DisplayMemberPath, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
                return prop.GetValue(item)?.ToString() ?? "(null)";
        }

        // An overridden ToString() is authoritative; the default one (type name) is not.
        var str = item.ToString();
        var isDefaultToString = str == item.GetType().FullName || str == item.GetType().Name;
        if (!string.IsNullOrEmpty(str) && !isDefaultToString)
            return str!;

        // ItemTemplate case: read the visible text from the realized container.
        if (selector.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container)
        {
            var text = TreeWalker.GetSearchableText(container);
            if (!string.IsNullOrWhiteSpace(text))
                return text!.Length > 100 ? text.Substring(0, 100) + "…" : text;
        }

        // Virtualized (no container yet): fall back to common display properties.
        foreach (var name in new[] { "Name", "Title", "Text", "DisplayName", "Header", "Description" })
        {
            var prop = item.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.PropertyType == typeof(string))
            {
                var value = prop.GetValue(item) as string;
                if (!string.IsNullOrWhiteSpace(value))
                    return value!;
            }
        }

        return str ?? item.GetType().Name;
    }

    // ---------------------------------------------------------------------
    // Send keys (shortcut)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Sends a single key combination (e.g. <c>Ctrl+S</c>, <c>Enter</c>, <c>F5</c>,
    /// <c>Alt+F4</c>) to the given element, or to whatever currently has focus when
    /// <paramref name="element"/> is null. Uses OS keyboard input.
    /// </summary>
    public InteractionOutcome SendKeys(UIElement? element, string keys)
    {
        if (string.IsNullOrWhiteSpace(keys))
            throw new ArgumentException("Key combo is required (e.g. \"Ctrl+S\", \"Enter\", \"F5\").");

        if (element != null)
            EnsureInteractable(element, "given keys");

        var (modifiers, key) = KeyComboParser.Parse(keys);

        if (element != null)
            FocusForKeyboardInput(element);

        foreach (var modifier in modifiers)
            NativeMethods.KeyDown(modifier);

        NativeMethods.KeyDown(key);
        NativeMethods.KeyUp(key);

        // Release modifiers in reverse order, matching how a human would let go.
        foreach (var modifier in modifiers.Reverse())
            NativeMethods.KeyUp(modifier);

        return new InteractionOutcome("Physical", $"sent '{keys}'");
    }

    // ---------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------

    private static void EnsureInteractable(UIElement element, string action)
    {
        if (!element.IsEnabled)
            throw new InvalidOperationException($"Element is disabled and cannot be {action}.");
        if (!element.IsVisible)
            throw new InvalidOperationException($"Element is not visible and cannot be {action}.");
    }

    /// <summary>
    /// Brings the host window forward and tries to give keyboard focus to the element.
    /// Returns true when the element actually accepted focus.
    /// </summary>
    private static bool FocusForKeyboardInput(UIElement element)
    {
        Window.GetWindow(element)?.Activate();
        ScrollIntoView(element);
        var focused = element.Focus();
        Keyboard.Focus(element);
        return focused;
    }

    // ---------------------------------------------------------------------
    // Native interop (mouse + keyboard input)
    // ---------------------------------------------------------------------

    private static class NativeMethods
    {
        // -- Mouse -------------------------------------------------------
        private const uint MOUSEEVENTF_LEFTDOWN  = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP    = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP   = 0x0010;

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

        public static void MouseRightClick()
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, IntPtr.Zero);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero);
        }

        // -- Keyboard ----------------------------------------------------
        public const byte VK_BACK    = 0x08;
        public const byte VK_TAB     = 0x09;
        public const byte VK_RETURN  = 0x0D;
        public const byte VK_SHIFT   = 0x10;
        public const byte VK_CONTROL = 0x11;
        public const byte VK_MENU    = 0x12;  // Alt
        public const byte VK_ESCAPE  = 0x1B;
        public const byte VK_SPACE   = 0x20;
        public const byte VK_PRIOR   = 0x21;  // PageUp
        public const byte VK_NEXT    = 0x22;  // PageDown
        public const byte VK_END     = 0x23;
        public const byte VK_HOME    = 0x24;
        public const byte VK_LEFT    = 0x25;
        public const byte VK_UP      = 0x26;
        public const byte VK_RIGHT   = 0x27;
        public const byte VK_DOWN    = 0x28;
        public const byte VK_INSERT  = 0x2D;
        public const byte VK_DELETE  = 0x2E;
        public const byte VK_LWIN    = 0x5B;

        private const uint INPUT_KEYBOARD     = 1;
        private const uint KEYEVENTF_KEYUP    = 0x0002;
        private const uint KEYEVENTF_UNICODE  = 0x0004;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        public static void KeyDown(byte vk) => keybd_event(vk, 0, 0, IntPtr.Zero);
        public static void KeyUp(byte vk)   => keybd_event(vk, 0, KEYEVENTF_KEYUP, IntPtr.Zero);

        /// <summary>
        /// Types a single Unicode character via SendInput with KEYEVENTF_UNICODE.
        /// Unlike keybd_event (whose scan code is a byte), SendInput's wScan is a
        /// ushort, so this handles the full BMP.
        /// </summary>
        public static void SendUnicodeChar(char c)
        {
            var inputs = new INPUT[2];

            inputs[0].type        = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk    = 0;
            inputs[0].u.ki.wScan  = c;
            inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;

            inputs[1].type        = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk    = 0;
            inputs[1].u.ki.wScan  = c;
            inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }
    }

    // ---------------------------------------------------------------------
    // Key-combo parser
    // ---------------------------------------------------------------------

    private static class KeyComboParser
    {
        /// <summary>
        /// Parses a key combo like <c>Ctrl+S</c>, <c>Ctrl+Shift+F5</c>, <c>Alt+F4</c>,
        /// <c>Enter</c>, <c>F1</c>, <c>Win+R</c>. The last segment is the key; any
        /// preceding segments are modifiers.
        /// </summary>
        public static (byte[] Modifiers, byte Key) Parse(string keys)
        {
            var parts = keys.Split('+');
            if (parts.Length == 0)
                throw new ArgumentException("Empty key combo.");

            var modifiers = new List<byte>();
            byte? key = null;

            for (int i = 0; i < parts.Length; i++)
            {
                var token = parts[i].Trim();
                if (token.Length == 0)
                    throw new ArgumentException($"Empty token in key combo '{keys}'.");

                if (i < parts.Length - 1)
                {
                    var mod = MapModifier(token)
                        ?? throw new ArgumentException(
                            $"Unknown modifier '{token}' in '{keys}'. Expected: Ctrl, Shift, Alt, or Win.");
                    modifiers.Add(mod);
                }
                else
                {
                    key = MapKey(token)
                        ?? throw new ArgumentException(
                            $"Unknown key '{token}' in '{keys}'. " +
                            "Supported: A-Z, 0-9, F1-F12, Enter, Esc, Tab, Space, " +
                            "Backspace, Delete, Insert, Home, End, PageUp, PageDown, " +
                            "Left, Right, Up, Down.");
                }
            }

            return (modifiers.ToArray(), key!.Value);
        }

        private static byte? MapModifier(string token)
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":  return NativeMethods.VK_CONTROL;
                case "shift":    return NativeMethods.VK_SHIFT;
                case "alt":
                case "menu":     return NativeMethods.VK_MENU;
                case "win":
                case "windows":
                case "meta":
                case "lwin":     return NativeMethods.VK_LWIN;
                default:         return null;
            }
        }

        private static byte? MapKey(string token)
        {
            // Single character: letter or digit.
            if (token.Length == 1)
            {
                var c = char.ToUpperInvariant(token[0]);
                if (c >= 'A' && c <= 'Z') return (byte)c;
                if (c >= '0' && c <= '9') return (byte)c;
            }

            var lower = token.ToLowerInvariant();

            // F1..F12
            if (lower.Length >= 2 && lower[0] == 'f'
                && int.TryParse(lower.Substring(1), out var n)
                && n >= 1 && n <= 12)
            {
                return (byte)(0x6F + n);  // VK_F1 = 0x70
            }

            switch (lower)
            {
                case "enter":
                case "return":    return NativeMethods.VK_RETURN;
                case "esc":
                case "escape":    return NativeMethods.VK_ESCAPE;
                case "tab":       return NativeMethods.VK_TAB;
                case "space":     return NativeMethods.VK_SPACE;
                case "back":
                case "backspace": return NativeMethods.VK_BACK;
                case "del":
                case "delete":    return NativeMethods.VK_DELETE;
                case "ins":
                case "insert":    return NativeMethods.VK_INSERT;
                case "home":      return NativeMethods.VK_HOME;
                case "end":       return NativeMethods.VK_END;
                case "pgup":
                case "pageup":    return NativeMethods.VK_PRIOR;
                case "pgdn":
                case "pagedown":  return NativeMethods.VK_NEXT;
                case "left":      return NativeMethods.VK_LEFT;
                case "right":     return NativeMethods.VK_RIGHT;
                case "up":        return NativeMethods.VK_UP;
                case "down":      return NativeMethods.VK_DOWN;
                default:          return null;
            }
        }
    }
}
