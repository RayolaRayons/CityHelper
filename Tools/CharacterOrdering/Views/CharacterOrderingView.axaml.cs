using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using CityHelper.Tools.CharacterOrdering.ViewModels;

namespace CityHelper.Tools.CharacterOrdering.Views;

public partial class CharacterOrderingView : UserControl
{
    private const double DragThreshold = 6;
    private const double AutoScrollEdge = 30;

    private SlotViewModel? _source;
    private Border? _sourceVisual;
    private Point _pressPoint;
    private bool _dragging;
    private SlotViewModel? _target;

    public CharacterOrderingView()
    {
        InitializeComponent();
        // Tunnel + handledEventsToo so we see presses regardless of what children do
        SlotList.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        SlotList.AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        SlotList.AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        SlotList.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(null, commit: false),
            RoutingStrategies.Direct | RoutingStrategies.Bubble);
    }

    private CharacterOrderingViewModel? Vm => DataContext as CharacterOrderingViewModel;

    private static SlotViewModel? SlotOf(Visual? v)
    {
        for (; v != null; v = v.GetVisualParent())
            if (v is Control { DataContext: SlotViewModel slot })
                return slot;
        return null;
    }

    /// <summary>
    /// Slot whose row contains the pointer's Y position. Geometry-based rather than hit-testing,
    /// because empty areas of a row (no background) aren't hit-testable and made targeting flaky.
    /// Pointers above/below the list clamp to the first/last slot.
    /// </summary>
    private SlotViewModel? SlotAt(Point p)
    {
        if (SlotList.ItemsPanelRoot is not { } panel || panel.Children.Count == 0)
            return null;

        SlotViewModel? last = null;
        foreach (var child in panel.Children)
        {
            if (child.DataContext is not SlotViewModel slot)
                continue;
            var top = child.TranslatePoint(default, SlotList)?.Y ?? 0;
            if (last is null && p.Y < top)
                return slot;                       // above the first row
            if (p.Y >= top && p.Y < top + child.Bounds.Height)
                return slot;
            last = slot;
        }
        return last;                               // below the last row
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _source = null;
        _sourceVisual = null;
        if (!e.GetCurrentPoint(SlotList).Properties.IsLeftButtonPressed || e.Source is not Visual src)
            return;

        // Only the character button starts a drag
        for (Visual? v = src; v != null && v != SlotList; v = v.GetVisualParent())
        {
            if (v is Border b && b.Classes.Contains("charbtn"))
            {
                _sourceVisual = b;
                _source = SlotOf(b);
                _pressPoint = e.GetPosition(SlotList);
                return;
            }
        }
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_source is null || _sourceVisual is null)
            return;
        if (!e.GetCurrentPoint(SlotList).Properties.IsLeftButtonPressed)
        {
            EndDrag(e.Pointer, commit: false);
            return;
        }

        var p = e.GetPosition(SlotList);
        var delta = p - _pressPoint;
        if (!_dragging)
        {
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
                return;
            _dragging = true;
            _source.IsDragging = true;
            e.Pointer.Capture(SlotList);
            // Draw the dragged button above its neighbours
            for (Visual? v = _sourceVisual; v != null && v != SlotList; v = v.GetVisualParent())
                v.ZIndex = 100;
        }

        _sourceVisual.RenderTransform = new TranslateTransform(delta.X, delta.Y);
        AutoScroll(e.GetPosition(Scroller));
        SetTarget(SlotAt(p));
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndDrag(e.Pointer, commit: true);
    }

    private void SetTarget(SlotViewModel? target)
    {
        if (target == _target)
            return;
        if (_target != null) _target.IsDropTarget = false;
        _target = target;
        if (_target != null && _target != _source) _target.IsDropTarget = true;
    }

    private void EndDrag(IPointer? pointer, bool commit)
    {
        bool wasDragging = _dragging;
        var source = _source;
        var target = _target;
        var visual = _sourceVisual;

        if (_target != null) _target.IsDropTarget = false;
        _target = null;
        _dragging = false;
        _source = null;
        _sourceVisual = null;

        if (visual != null)
        {
            visual.RenderTransform = null;
            for (Visual? v = visual; v != null && v != SlotList; v = v.GetVisualParent())
                v.ZIndex = 0;
        }
        if (source != null) source.IsDragging = false;
        if (wasDragging && pointer?.Captured == SlotList)
            pointer.Capture(null);

        if (commit && wasDragging && source != null && target != null)
            Vm?.Move(source, target);
    }

    private void AutoScroll(Point p)
    {
        if (p.Y < AutoScrollEdge)
            Scroller.Offset = Scroller.Offset.WithY(Math.Max(0, Scroller.Offset.Y - 14));
        else if (p.Y > Scroller.Bounds.Height - AutoScrollEdge)
            Scroller.Offset = Scroller.Offset.WithY(Scroller.Offset.Y + 14);
    }
}
