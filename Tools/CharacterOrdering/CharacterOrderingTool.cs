using System;
using System.Collections.Generic;
using System.Windows.Input;
using CityHelper.Ribbon;
using CityHelper.Services;
using CityHelper.Tools.CharacterOrdering.ViewModels;
using CityHelper.ViewModels;

namespace CityHelper.Tools.CharacterOrdering;

/// <summary>Reorders characters per server by editing the positions in accounts\&lt;account&gt;\playerslot.txt.</summary>
public sealed class CharacterOrderingTool : ITool
{
    private readonly Lazy<CharacterOrderingViewModel> _vm;

    public CharacterOrderingTool(AppState state) => _vm = new(() => new CharacterOrderingViewModel(state));

    public string Name => "Character Ordering";

    public ViewModelBase CreateViewModel(AppState state) => _vm.Value;

    public ICommand? SaveCommand => _vm.Value.SaveCommand;

    public IReadOnlyList<RibbonGroup> RibbonGroups => new[]
    {
        new RibbonGroup("Character Order", new[]
        {
            new RibbonCommand("Reload from disk", _vm.Value.ReloadCommand, "Discard unsaved changes and read the file again"),
        }),
    };
}
