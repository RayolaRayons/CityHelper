using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using CityHelper.Tools.Bind.ViewModels;

namespace CityHelper.Tools.Bind.Views;

public partial class BindView : UserControl
{
    public BindView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is BindViewModel vm)
                vm.PromptFileName = PromptAsync;
        };
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
