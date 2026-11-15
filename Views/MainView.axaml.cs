using Avalonia.Controls;
using Avalonia.Input;

namespace CityHelper.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void OnInstallationPicked(object? sender, TappedEventArgs e) => this.FindControl<Button>("InstallationSegment")?.Flyout?.Hide();
    private void OnAccountPicked(object? sender, TappedEventArgs e) => this.FindControl<Button>("AccountSegment")?.Flyout?.Hide();
}


