using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using CityHelper.ViewModels;

namespace CityHelper.Views;

public partial class InstallationsView : UserControl
{
    public InstallationsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is InstallationsViewModel vm)
                vm.EditDialog = EditAsync;
        };
    }

    private Task<InstallationEdit?> EditAsync(InstallationEdit? existing, IEnumerable<string> otherNames) =>
        new InstallationEditWindow(existing, otherNames).ShowDialog<InstallationEdit?>((Window)TopLevel.GetTopLevel(this)!);

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is InstallationsViewModel { SelectedItem: not null } vm && vm.EditCommand.CanExecute(null))
            vm.EditCommand.Execute(null);
    }
}
