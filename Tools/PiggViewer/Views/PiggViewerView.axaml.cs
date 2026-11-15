using System.Linq;
using Avalonia.Controls;
using CityHelper.Tools.PiggViewer.ViewModels;

namespace CityHelper.Tools.PiggViewer.Views;

public partial class PiggViewerView : UserControl
{
    public PiggViewerView()
    {
        InitializeComponent();
        FileList.SelectionChanged += (_, _) =>
        {
            if (DataContext is PiggViewerViewModel vm)
                vm.SetSelectedFiles(FileList.SelectedItems?.OfType<FileRowViewModel>().ToList() ?? new());
        };
    }
}
