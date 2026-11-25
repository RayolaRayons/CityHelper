using Avalonia.Controls;
using Avalonia.Interactivity;
using CityHelper.ViewModels;

namespace CityHelper.Views;

/// <summary>Pick a macro image; closes with its texture name, or null if dismissed.</summary>
public partial class MacroImageLookupWindow : Window
{
    public MacroImageLookupWindow()
    {
        InitializeComponent();
        DataContext = new MacroImageLookupViewModel();
    }

    private void OnInsert(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is MacroImageRow { Entry: { } entry })
            Close(entry.Name);
    }
}
