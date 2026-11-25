using System;
using System.Windows.Input;
using CityHelper.Services;
using CityHelper.Tools.IconLookup.ViewModels;
using CityHelper.ViewModels;

namespace CityHelper.Tools.IconLookup;

/// <summary>Browses the macro image (icon) cache: pictures with their texture names, with a section filter and search.</summary>
public sealed class IconLookupTool : ITool
{
    private readonly Lazy<IconLookupViewModel> _vm = new(() => new IconLookupViewModel());

    public string Name => "Icon Lookup";

    public ViewModelBase CreateViewModel(AppState state) => _vm.Value;

    public ICommand? RefreshCommand => _vm.Value.RefreshCommand;
}
