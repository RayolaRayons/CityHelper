using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CityHelper.Services.MacroImages;
using CityHelper.Tools.PopMenuHero.Services;
using CityHelper.Tools.PopMenuHero.ViewModels;

namespace CityHelper.Tools.PopMenuHero.Views;

public partial class PopMenuHeroView : UserControl
{
    private const double DragThreshold = 6;
    private const double AutoScrollEdge = 24;

        private NodeViewModel? _dragSource;
    private Point _pressPoint;
    private bool _dragging;
    private NodeViewModel? _dropTarget;
    private DropPosition _dropPosition;
    private bool _dropValid;

    private bool _closeConfirmed;
    private Window? _window;

    public PopMenuHeroView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;

        // handledEventsToo: the TreeView marks presses handled when it selects
        Tree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Tree.AddHandler(PointerMovedEvent, OnTreePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        Tree.AddHandler(PointerReleasedEvent, OnTreePointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        Tree.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(null), RoutingStrategies.Direct | RoutingStrategies.Bubble);
    }

    private PopMenuHeroViewModel? Vm => DataContext as PopMenuHeroViewModel;

    /// <summary>Icon field: pick a macro image and put its texture name in the field.</summary>
    private async void OnLookupIcon(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner || (sender as Control)?.DataContext is not LockedOptionNodeViewModel node)
            return;
        if (!await MacroImageUi.EnsureAsync(owner))
            return;
        var name = await new CityHelper.Views.MacroImageLookupWindow().ShowDialog<string?>(owner);
        if (!string.IsNullOrEmpty(name))
            node.Icon = name;
    }

    private async void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || Vm is null)
            return;
        if (Vm.Dialogs is DialogService dialogs)
            dialogs.Owner = top;

        // Subscribe once and keep it for the app's lifetime, so unsaved changes still
        // prompt on exit while another tool is showing.
        if (_window is null && top is Window window)
        {
            _window = window;
            window.Closing += OnWindowClosing;
        }
        await Vm.InitializeAsync([]);
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || Vm is null || !Vm.IsModified)
            return;
        e.Cancel = true;
        if (await Vm.ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            _window?.Close();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_dragging && e.Key == Key.Escape)
        {
            EndDrag(null);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    // --- Drag and drop ---

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragSource = null;
        if (!e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed || e.Source is not Visual source)
            return;
        if (source.FindAncestorOfType<ToggleButton>(includeSelf: true) != null)
            return;  // expander arrow
        _dragSource = source.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as NodeViewModel;
        _pressPoint = e.GetPosition(Tree);
    }

    private void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSource is null || Vm is null)
            return;
        if (!e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed)
        {
            EndDrag(e.Pointer);
            return;
        }

        var p = e.GetPosition(Tree);
        if (!_dragging)
        {
            var d = p - _pressPoint;
            if (Math.Abs(d.X) < DragThreshold && Math.Abs(d.Y) < DragThreshold)
                return;
            _dragging = true;
            e.Pointer.Capture(Tree);
        }

        AutoScroll(p);
        UpdateDropTarget(p);
        e.Handled = true;
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging && _dropValid && _dragSource != null && Vm != null)
        {
            Vm.MoveNode(_dragSource, _dropTarget, _dropPosition);
            e.Handled = true;
        }
        EndDrag(e.Pointer);
    }

    private void EndDrag(IPointer? pointer)
    {
        bool wasDragging = _dragging;
        _dragging = false;
        _dragSource = null;
        _dropValid = false;
        DropIndicator.IsVisible = false;
        if (wasDragging && pointer?.Captured == Tree)
            pointer.Capture(null);
    }

    private void UpdateDropTarget(Point p)
    {
        var item = (Tree.InputHitTest(p) as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        var presenter = item is null ? null : HeaderPresenter(item);
        if (item?.DataContext is not NodeViewModel target || presenter is null)
        {
            // Empty space below the items: end of the root level
            ShowDrop(null, DropPosition.After);
            return;
        }

        var local = Tree.TranslatePoint(p, presenter) ?? default;
        double h = presenter.Bounds.Height;
        var pos = target is MenuNodeViewModel
            ? local.Y < h * 0.25 ? DropPosition.Before : local.Y > h * 0.75 ? DropPosition.After : DropPosition.Inside
            : local.Y < h / 2 ? DropPosition.Before : DropPosition.After;

        // "Below" an expanded menu's header means "before its first child", which is where the line is drawn
        if (pos == DropPosition.After && target is MenuNodeViewModel { IsExpanded: true, Children.Count: > 0 } menu)
        {
            target = menu.Children[0];
            pos = DropPosition.Before;
        }

        ShowDrop(target, pos);
    }

    private void ShowDrop(NodeViewModel? target, DropPosition pos)
    {
        _dropTarget = target;
        _dropPosition = pos;
        _dropValid = Vm!.CanDrop(_dragSource!, target, pos);

        Rect? rect = null;
        if (target is null)
        {
            // Line under the last root item (including its expanded children)
            if (Vm.RootItems.Count > 0 && Tree.ContainerFromItem(Vm.RootItems[^1]) is Control last)
            {
                var origin = last.TranslatePoint(default, DropOverlay) ?? default;
                rect = new Rect(origin, last.Bounds.Size);
            }
        }
        else if (Tree.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault(i => i.DataContext == target)
                 is { } container && HeaderPresenter(container) is { } presenter)
        {
            var origin = presenter.TranslatePoint(default, DropOverlay) ?? default;
            rect = new Rect(origin, presenter.Bounds.Size);
        }

        if (!_dropValid || rect is not { } r)
        {
            DropIndicator.IsVisible = false;
            return;
        }

        if (pos == DropPosition.Inside)
        {
            DropIndicator.BorderThickness = new Thickness(2);
            DropIndicator.Width = r.Width;
            DropIndicator.Height = r.Height;
            Canvas.SetTop(DropIndicator, r.Top);
        }
        else
        {
            DropIndicator.BorderThickness = new Thickness(0, 2, 0, 0);
            DropIndicator.Width = r.Width;
            DropIndicator.Height = 2;
            Canvas.SetTop(DropIndicator, (pos == DropPosition.Before ? r.Top : r.Bottom) - 1);
        }
        Canvas.SetLeft(DropIndicator, r.Left);
        DropIndicator.IsVisible = true;
    }

    private void AutoScroll(Point p)
    {
        if (Tree.FindDescendantOfType<ScrollViewer>() is not { } sv)
            return;
        if (p.Y < AutoScrollEdge)
            sv.Offset = sv.Offset.WithY(Math.Max(0, sv.Offset.Y - 12));
        else if (p.Y > Tree.Bounds.Height - AutoScrollEdge)
            sv.Offset = sv.Offset.WithY(sv.Offset.Y + 12);
    }

    /// <summary>The row content of a TreeViewItem (excluding its children), from our own template.</summary>
    private static ContentPresenter? HeaderPresenter(TreeViewItem item) =>
        item.GetVisualChildren().FirstOrDefault()?
            .GetVisualChildren().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Header")?
            .GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(c => c.Name == "PART_HeaderPresenter");
}
