using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
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

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.DirectoryRequested -= OnDirectoryRequested;
                _vm.FileRequested -= OnFileRequested;
            }
            _vm = DataContext as PiggViewerViewModel;
            if (_vm != null)
            {
                _vm.DirectoryRequested += OnDirectoryRequested;
                _vm.FileRequested += OnFileRequested;
            }
        };
    }

    private PiggViewerViewModel? _vm;

    /// <summary>Search: bring the result's folder into view in the tree (after the tree has expanded to it).</summary>
    private void OnDirectoryRequested(DirectoryNodeViewModel directory) =>
        Dispatcher.UIThread.Post(() => DirTree.TreeContainerFromItem(directory)?.BringIntoView(), DispatcherPriority.Loaded);

    /// <summary>Search: select the result's file in the list and scroll to it.</summary>
    private void OnFileRequested(FileRowViewModel file) =>
        Dispatcher.UIThread.Post(() =>
        {
            FileList.SelectedItems?.Clear();
            FileList.SelectedItem = file;
            FileList.ScrollIntoView(file);
        }, DispatcherPriority.Loaded);
}
