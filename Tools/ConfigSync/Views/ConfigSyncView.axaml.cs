using Avalonia.Controls;
using CityHelper.Tools.ConfigSync.ViewModels;

namespace CityHelper.Tools.ConfigSync.Views;

public partial class ConfigSyncView : UserControl
{
    public ConfigSyncView()
    {
        InitializeComponent();

        // Look for changed files (the game keeps writing its logs) only while this pane is on screen
        AttachedToVisualTree += (_, _) => (DataContext as ConfigSyncViewModel)?.StartWatching();
        DetachedFromVisualTree += (_, _) => (DataContext as ConfigSyncViewModel)?.StopWatching();
    }
}
