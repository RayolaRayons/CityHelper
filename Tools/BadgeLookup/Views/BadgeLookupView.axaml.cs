using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CityHelper.Tools.BadgeLookup.ViewModels;

namespace CityHelper.Tools.BadgeLookup.Views;

public partial class BadgeLookupView : UserControl
{
    public BadgeLookupView() => InitializeComponent();

    private async void OnCopyName(object? sender, RoutedEventArgs e)
    {
        if ((DataContext as BadgeLookupViewModel)?.Selected is { } row && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(row.HeroName);
    }
}
