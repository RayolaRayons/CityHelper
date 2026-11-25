using System;
using System.Windows.Input;
using CityHelper.Services;
using CityHelper.Tools.CommandLookup.ViewModels;
using CityHelper.ViewModels;

namespace CityHelper.Tools.CommandLookup;

/// <summary>Looks up slash commands (from the wiki's List of Slash Commands): aliases, default key, syntax and parameters.</summary>
public sealed class CommandLookupTool : ITool
{
    private readonly Lazy<CommandLookupViewModel> _vm = new(() => new CommandLookupViewModel());

    public string Name => "Command Lookup";

    public ViewModelBase CreateViewModel(AppState state) => _vm.Value;

    public ICommand? RefreshCommand => _vm.Value.RefreshCommand;
}
