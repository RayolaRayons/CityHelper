using Avalonia.Controls;
using CityHelper.ViewModels;

namespace CityHelper.Views;

public partial class OptionsWindow : Window
{
    public OptionsWindow()
    {
        InitializeComponent();
    }

    public OptionsWindow(OptionsViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += saved => Close(saved);
        vm.SaveFailed += async msg => await new MessageBox("Options", msg).ShowDialog(this);
    }
}
