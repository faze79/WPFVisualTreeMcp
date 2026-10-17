using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SampleWpfApp;

public partial class TouchDemoWindow : Window
{
    private readonly HashSet<int> _active = new();
    private readonly Stopwatch _hold = new();
    private int _downs, _ups, _moves, _maximum;
    private double _x, _y, _scale = 1, _rotation;

    public TouchDemoWindow()
    {
        InitializeComponent();
        TouchSurface.AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(Down), true);
        TouchSurface.AddHandler(UIElement.PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>(Move), true);
        TouchSurface.AddHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(Up), true);
        Refresh();
    }

    private void Down(object? sender, TouchEventArgs e)
    {
        if (_active.Count == 0) _hold.Restart();
        _active.Add(e.TouchDevice.Id);
        _downs++;
        _maximum = Math.Max(_maximum, _active.Count);
        Refresh();
    }
    private void Move(object? sender, TouchEventArgs e) { _moves++; Refresh(); }
    private void Up(object? sender, TouchEventArgs e)
    {
        _active.Remove(e.TouchDevice.Id);
        _ups++;
        if (_active.Count == 0) _hold.Stop();
        Refresh();
    }
    private void Starting(object sender, ManipulationStartingEventArgs e)
    {
        e.ManipulationContainer = TouchSurface;
        e.Mode = ManipulationModes.All;
    }
    private void Delta(object sender, ManipulationDeltaEventArgs e)
    {
        if (e.IsInertial) { e.Complete(); return; }
        _x += e.DeltaManipulation.Translation.X;
        _y += e.DeltaManipulation.Translation.Y;
        _scale *= e.DeltaManipulation.Scale.X;
        _rotation += e.DeltaManipulation.Rotation;
        var transforms = new TransformGroup();
        transforms.Children.Add(new ScaleTransform(_scale, _scale));
        transforms.Children.Add(new RotateTransform(_rotation));
        transforms.Children.Add(new TranslateTransform(_x, _y));
        TouchCard.RenderTransformOrigin = new Point(0.5, 0.5);
        TouchCard.RenderTransform = transforms;
        Refresh();
        e.Handled = true;
    }
    private void Reset(object sender, RoutedEventArgs e)
    {
        _active.Clear(); _hold.Reset();
        _downs = _ups = _moves = _maximum = 0;
        _x = _y = _rotation = 0; _scale = 1;
        TouchCard.RenderTransform = Transform.Identity;
        Refresh();
    }
    private void Refresh()
    {
        TouchTelemetry.Text = FormattableString.Invariant(
            $"{{\"downs\":{_downs},\"ups\":{_ups},\"moves\":{_moves},\"active\":{_active.Count},\"max_contacts\":{_maximum},\"hold_ms\":{_hold.ElapsedMilliseconds}}}");
        TouchManipulation.Text = FormattableString.Invariant(
            $"{{\"x\":{_x:F2},\"y\":{_y:F2},\"scale\":{_scale:F3},\"rotation\":{_rotation:F2}") + "}";
    }
}
