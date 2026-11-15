using System;
using System.Windows.Input;
using CityHelper.Services;
using CityHelper.Tools.LogManager.ViewModels;
using CityHelper.ViewModels;

namespace CityHelper.Tools.LogManager;

/// <summary>Merges and trims the chat logs of the selected installation and account.</summary>
public sealed class LogManagerTool : ITool
{
    private readonly Lazy<LogManagerViewModel> _vm;

    public LogManagerTool(AppState state)
    {
        _vm = new(() => new LogManagerViewModel(state));
    }

    public string Name => "Log Manager";

    public ViewModelBase CreateViewModel(AppState state) => _vm.Value;

    // Merge and Cleanup Logfile live in the view only; the ribbon carries the standard Refresh
    public ICommand? RefreshCommand => _vm.Value.RefreshCommand;

    // Settings are read from the file each time they are used, so there is nothing to reload
    public OptionsSectionViewModel? CreateOptionsSection() => new LogOptionsViewModel(LogManagerSettings.Load());
}
