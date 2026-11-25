using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CityHelper.Tools.Bind.ViewModels;

namespace CityHelper.Tools.Bind.Views;

public partial class BindView : UserControl
{
    public BindView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
                _vm.RowAdded -= OnRowAdded;
            _vm = DataContext as BindViewModel;
            if (_vm != null)
            {
                _vm.PromptFileName = PromptAsync;
                _vm.RowAdded += OnRowAdded;
            }
        };
    }

    private BindViewModel? _vm;

    /// <summary>Add Bind: jump to the new blank line at the bottom and put the cursor in its Key box.</summary>
    private void OnRowAdded(BindRowViewModel row)
    {
        // After layout, so the new line has been created
        Dispatcher.UIThread.Post(() =>
        {
            RowScroller.ScrollToEnd();
            if (RowList.ContainerFromIndex(RowList.ItemCount - 1) is { } container)
            {
                container.BringIntoView();
                container.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    private async Task<string?> PromptAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return null;

        var box = new TextBox { MinWidth = 280, PlaceholderText = "File name (.txt is added if missing)" };
        var ok = new Button { Content = "Create", MinWidth = 80, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        var dialog = new Window
        {
            Title = "New Binds File",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "Name of the new file in the Binds folder:" },
                    box,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { ok, cancel },
                    },
                },
            },
        };
        ok.Click += (_, _) => dialog.Close(box.Text);
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => box.Focus();
        return await dialog.ShowDialog<string?>(owner);
    }
}
