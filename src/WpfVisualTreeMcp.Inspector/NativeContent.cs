using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Inspector;

internal static class NativeContent
{
    internal static NativeContentInfo? Describe(DependencyObject element) => element is HwndHost host
        ? new NativeContentInfo { WindowHandle = "0x" + host.Handle.ToInt64().ToString("X") }
        : null;

    internal static bool ContainsVisibleHost(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        var visited = new HashSet<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current)) continue;
            if (current is UIElement ui && !ui.IsVisible) continue;
            if (current is HwndHost) return true;
            foreach (var child in TreeWalker.GetAllVisualChildren(current)) pending.Push(child);
        }
        return false;
    }
}
