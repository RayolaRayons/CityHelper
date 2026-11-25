using System;
using System.Windows.Input;
using CityHelper.Services;
using CityHelper.Tools.BadgeLookup.ViewModels;
using CityHelper.ViewModels;

namespace CityHelper.Tools.BadgeLookup;

/// <summary>Looks up badges (badges-all.json): names for heroes and villains, descriptions, category, icons and requirements.</summary>
public sealed class BadgeLookupTool : ITool
{
    private readonly Lazy<BadgeLookupViewModel> _vm = new(() => new BadgeLookupViewModel());

    public string Name => "Badge Lookup";

    public ViewModelBase CreateViewModel(AppState state) => _vm.Value;

    public ICommand? RefreshCommand => _vm.Value.RefreshCommand;
}
