using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CityHelper.Tools.PopMenuHero.Services;

namespace CityHelper.Tools.PopMenuHero.Views;

/// <summary>Minimal modal message / Yes-No box (Avalonia has no built-in MessageBox).</summary>
internal sealed class MessageDialog : Window
{
    private MessageDialog(string title, string message, MessageKind kind, bool yesNo)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = 320;
        MaxWidth = 560;

        var icon = new TextBlock
        {
            Text = kind switch { MessageKind.Error => "⛔", MessageKind.Warning => "⚠", _ => yesNo ? "❓" : "ℹ" },
            FontSize = 28,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var text = new SelectableTextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        if (yesNo)
        {
            buttons.Children.Add(MakeButton("Yes", true, isDefault: true));
            buttons.Children.Add(MakeButton("No", false, isCancel: true));
        }
        else
        {
            buttons.Children.Add(MakeButton("OK", true, isDefault: true, isCancel: true));
        }

        var body = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        icon.Margin = new Avalonia.Thickness(0, 0, 16, 0);
        body.Children.Add(icon);
        body.Children.Add(text);

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 20,
            Children = { body, buttons },
        };
    }

    private Button MakeButton(string label, bool result, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = label, MinWidth = 80, IsDefault = isDefault, IsCancel = isCancel };
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.Click += (_, _) => Close(result);
        return button;
    }

    public static Task<bool> ShowAsync(Window owner, string title, string message, MessageKind kind, bool yesNo) =>
        new MessageDialog(title, message, kind, yesNo).ShowDialog<bool>(owner);
}
