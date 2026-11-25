using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CityHelper.Tools.IconLookup.ViewModels;

namespace CityHelper.Tools.IconLookup.Views;

public partial class IconLookupView : UserControl
{
    public IconLookupView() => InitializeComponent();

    private async void OnCopyName(object? sender, RoutedEventArgs e)
    {
        if ((DataContext as IconLookupViewModel)?.SelectedName is { Length: > 0 } name && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(name);
    }
}
