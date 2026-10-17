using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SampleWpfApp;

public partial class DragDemoWindow : Window
{
    private Point? _sourceStart;
    private Point _boxOffset;
    private MouseButton _button;
    private int _moves;
    private int _drops;

    public DragDemoWindow() => InitializeComponent();

    private void SourceDown(object sender, MouseButtonEventArgs e) => _sourceStart = e.GetPosition(DragSource);

    private void SourceMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _sourceStart = null; return; }
        if (!_sourceStart.HasValue) return;
        var delta = e.GetPosition(DragSource) - _sourceStart.Value;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _sourceStart = null;
        // This enters WPF/OLE's nested event loop: the Inspector must keep
        // submitting movement and button-up from outside the UI Dispatcher.
        DragDrop.DoDragDrop(DragSource, "sample-drag-item", DragDropEffects.Copy);
    }

    private void TargetDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void TargetDrop(object sender, DragEventArgs e)
    {
        if ((string?)e.Data.GetData(DataFormats.StringFormat) == "sample-drag-item")
            DropStatus.Text = $"Dropped {++_drops}";
        e.Handled = true;
    }

    private void BoxDown(object sender, MouseButtonEventArgs e)
    {
        _button = e.ChangedButton;
        _boxOffset = e.GetPosition(DragBox);
        _moves = 0;
        DragBox.CaptureMouse();
        DragStatus.Text = $"Pressed {_button}";
        e.Handled = true;
    }

    private void BoxMove(object sender, MouseEventArgs e)
    {
        if (!DragBox.IsMouseCaptured) return;
        var point = e.GetPosition(DragCanvas);
        Canvas.SetLeft(DragBox, Math.Max(0, Math.Min(DragCanvas.ActualWidth - DragBox.ActualWidth, point.X - _boxOffset.X)));
        Canvas.SetTop(DragBox, Math.Max(0, Math.Min(DragCanvas.ActualHeight - DragBox.ActualHeight, point.Y - _boxOffset.Y)));
        DragStatus.Text = $"Moving {_button}: {++_moves}";
    }

    private void BoxUp(object sender, MouseButtonEventArgs e)
    {
        if (!DragBox.IsMouseCaptured || e.ChangedButton != _button) return;
        DragBox.ReleaseMouseCapture();
        DragStatus.Text = $"Released {_button}; moves={_moves}";
        e.Handled = true;
    }
}
