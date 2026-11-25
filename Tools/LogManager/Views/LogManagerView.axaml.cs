using System.Threading.Tasks;
using Avalonia.Controls;
using CityHelper.Tools.LogManager.ViewModels;

namespace CityHelper.Tools.LogManager.Views;

public partial class LogManagerView : UserControl
{
    public LogManagerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is LogManagerViewModel vm)
                vm.Confirm = ConfirmAsync;
        };
    }

    private async Task<bool> ConfirmAsync(string message) =>
        TopLevel.GetTopLevel(this) is Window owner && await new CityHelper.Views.ConfirmBox("Cleanup Logfile", message).ShowDialog<bool>(owner);
}
