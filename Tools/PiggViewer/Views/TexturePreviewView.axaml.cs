using Avalonia.Controls;
using Avalonia.Input;
using CityHelper.Tools.PiggViewer.ViewModels;

namespace CityHelper.Tools.PiggViewer.Views;

public partial class TexturePreviewView : UserControl
{
    public TexturePreviewView()
    {
        InitializeComponent();
        // The wheel zooms (as in Piglet) instead of scrolling
        Scroller.AddHandler(PointerWheelChangedEvent, OnWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not TexturePreviewViewModel vm)
            return;
        if (e.Delta.Y > 0)
            vm.ZoomIn();
        else if (e.Delta.Y < 0)
            vm.ZoomOut();
        e.Handled = true;
    }
}
