using Avalonia.Controls;
using CityHelper.Tools.ChatWatcher.ViewModels;

namespace CityHelper.Tools.ChatWatcher.Views;

public partial class FilterEditorWindow : Window
{
    public FilterEditorWindow()
    {
        InitializeComponent();
    }

    public FilterEditorWindow(FilterEditorViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += saved => Close(saved);
    }
}
