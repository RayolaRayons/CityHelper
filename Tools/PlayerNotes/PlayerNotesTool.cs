using System;
using System.Collections.Generic;
using System.Windows.Input;
using CityHelper.Ribbon;
using CityHelper.Services;
using CityHelper.Tools.PlayerNotes.ViewModels;
using CityHelper.ViewModels;

namespace CityHelper.Tools.PlayerNotes;

/// <summary>Views and edits the player notes (accounts\&lt;account&gt;\playernotes.txt).</summary>
public sealed class PlayerNotesTool : ITool
{
    private readonly Lazy<PlayerNotesViewModel> _vm;

    public PlayerNotesTool(AppState state) => _vm = new(() => new PlayerNotesViewModel(state));

    public string Name => "Player Notes";

    public ViewModelBase CreateViewModel(AppState state) => _vm.Value;

    public ICommand? SaveCommand => _vm.Value.SaveCommand;

}
