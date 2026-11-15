using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CityHelper.Views;

/// <summary>Minimal modal OK box.</summary>
public sealed class MessageBox : Window
{
    public MessageBox(string title, string message)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 520;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var ok = new Button { Content = "OK", MinWidth = 80, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, ok },
        };
    }
}
