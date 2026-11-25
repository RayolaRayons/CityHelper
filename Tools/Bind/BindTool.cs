using System;
using System.Collections.Generic;
using System.Windows.Input;
using CityHelper.Ribbon;
using CityHelper.Services;
using CityHelper.Tools.Bind.ViewModels;
using CityHelper.ViewModels;

namespace CityHelper.Tools.Bind;

/// <summary>Edits keybinds.txt, the Binds folder and binds.txt of the selected installation.</summary>
public sealed class BindTool : ITool
{
    private readonly Lazy<BindViewModel> _vm;

    public BindTool(AppState state) => _vm = new(() => new BindViewModel(state));

    public string Name => "Bind";

    public ViewModelBase CreateViewModel(AppState state) => _vm.Value;

    public ICommand? SaveCommand => _vm.Value.SaveCommand;

}
