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

    /// <summary>Creating a file belongs with the other file commands (like New / Open in PopMenu Hero).</summary>
    public IReadOnlyList<RibbonGroup> FileGroups => new[]
    {
        new RibbonGroup("Binds", new[]
        {
            new RibbonCommand("New Binds File", _vm.Value.NewFileCommand,
                "Create a new file in the Binds folder (available when the Binds folder section is selected)"),
        }),
    };

    public IReadOnlyList<RibbonGroup> RibbonGroups => new[]
    {
        new RibbonGroup("Bind", new[]
        {
            new RibbonCommand("Add Bind", _vm.Value.AddRowCommand, "Add a new line to the list"),
            new RibbonCommand("Reload from disk", _vm.Value.ReloadCommand, "Discard unsaved changes and read the file again"),
        }),
    };
}
