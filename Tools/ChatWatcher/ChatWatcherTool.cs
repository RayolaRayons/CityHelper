using System;
using System.Windows.Input;
using CityHelper.Services;
using CityHelper.Tools.ChatWatcher.ViewModels;
using CityHelper.ViewModels;

namespace CityHelper.Tools.ChatWatcher;

/// <summary>Watches the current chat log for keywords and offers Team / League invites for the people who trigger them.</summary>
public sealed class ChatWatcherTool : ITool
{
    private readonly Lazy<ChatWatcherViewModel> _vm;

    public ChatWatcherTool(AppState state)
    {
        _vm = new(() => new ChatWatcherViewModel(state));
    }

    public string Name => "ChatWatcher";

    public ViewModelBase CreateViewModel(AppState state) => _vm.Value;

    // Start / Stop and Clear live in the view only; the ribbon carries the standard Refresh
    public ICommand? RefreshCommand => _vm.Value.RefreshCommand;

    public OptionsSectionViewModel? CreateOptionsSection()
    {
        // The window edits the file, so write the pane's state first (if the pane exists)
        if (_vm.IsValueCreated)
            _vm.Value.PrepareOptions();

        return new ChatWatcherOptionsViewModel(ChatWatcherSettings.Load())
        {
            Saved = () =>
            {
                if (_vm.IsValueCreated)
                    _vm.Value.ReloadOptions();
            },
        };
    }
}
