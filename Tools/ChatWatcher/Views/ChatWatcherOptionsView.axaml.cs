using Avalonia.Controls;
using CityHelper.Tools.ChatWatcher.ViewModels;

namespace CityHelper.Tools.ChatWatcher.Views;

public partial class ChatWatcherOptionsView : UserControl
{
    public ChatWatcherOptionsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ChatWatcherOptionsViewModel vm)
                vm.EditorDialog = editor =>
                    new FilterEditorWindow(editor).ShowDialog<bool>((Window)TopLevel.GetTopLevel(this)!);
        };
    }
}
