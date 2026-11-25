using System;
using System.Collections.Generic;
using System.Windows.Input;
using CityHelper.Ribbon;
using CityHelper.Services;
using CityHelper.Tools.ConfigSync.ViewModels;
using CityHelper.ViewModels;

namespace CityHelper.Tools.ConfigSync;

/// <summary>Compares local game config files with a sync folder, per environment.</summary>
public sealed class ConfigSyncTool : ITool
{
    private readonly AppState _state;
    private readonly Lazy<ConfigSyncViewModel> _vm;

    public ConfigSyncTool(AppState state)
    {
        _state = state;
        _vm = new(() => new ConfigSyncViewModel(state));
    }

    public string Name => "Config Sync";

    public ViewModelBase CreateViewModel(AppState state) => _vm.Value;

    public ICommand? RefreshCommand => _vm.Value.RefreshCommand;

    public OptionsSectionViewModel? CreateOptionsSection()
    {
        var options = ConfigSyncViewModel.CreateOptions(_state);
        // The pane re-scans after a save, if it has been shown
        options.Saved = () =>
        {
            if (_vm.IsValueCreated)
                _ = _vm.Value.RefreshAsync();
        };
        return options;
    }
}
