using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CityHelper.Views;

/// <summary>Minimal modal Yes / No box; closes with true for Yes.</summary>
public sealed class ConfirmBox : Window
{
    public ConfirmBox(string title, string message)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 560;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var yes = new Button { Content = "Yes", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        var no = new Button { Content = "No", MinWidth = 80, IsDefault = true, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        yes.Click += (_, _) => Close(true);
        no.Click += (_, _) => Close(false);
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { yes, no },
                },
            },
        };
    }
}
