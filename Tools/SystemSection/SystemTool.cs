using System;
using System.Collections.Generic;
using System.Windows.Input;
using CityHelper.Services;
using CityHelper.Tools.SystemSection.ViewModels;
using CityHelper.ViewModels;

namespace CityHelper.Tools.SystemSection;

/// <summary>
/// The Options section at the bottom of the tool list: a tree on the left, and one page on the right for
/// Information, About, Links, the installations and each tool's options.
/// </summary>
public sealed class SystemTool : ITool
{
    private readonly Lazy<SystemViewModel> _vm;

    public SystemTool(AppState state, IReadOnlyList<ITool> tools) => _vm = new(() => new SystemViewModel(state, tools));

    public string Name => "Options";

    public ViewModelBase CreateViewModel(AppState state) => _vm.Value;

    /// <summary>Ctrl+S saves the options while Options is open.</summary>
    public ICommand? SaveCommand => _vm.Value.SaveCommand;
}
