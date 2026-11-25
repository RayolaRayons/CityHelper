using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CityHelper.Models;
using CityHelper.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.ViewModels;

public sealed record InstallationEdit(string Name, string Path);

/// <summary>Working copy of the installation list. Nothing is persisted until Save.</summary>
public sealed partial class InstallationsViewModel : OptionsSectionViewModel
{
    private readonly AppState _state;

    public override string Title => "Installations";

    /// <summary>Shows the add/edit dialog; returns null if cancelled. Provided by the view.</summary>
    public Func<InstallationEdit?, IEnumerable<string>, Task<InstallationEdit?>>? EditDialog { get; set; }

    public InstallationsViewModel(AppState state)
    {
        _state = state;
        foreach (var i in state.Installations)
            Items.Add(new InstallationRow { Name = i.Name, Path = i.Path, IsDefault = i.IsDefault });
        NormalizeDefault();
        SelectedItem = Items.FirstOrDefault();
    }

    public ObservableCollection<InstallationRow> Items { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(RemoveCommand), nameof(MakeDefaultCommand))]
    private InstallationRow? _selectedItem;

    private bool HasSelection => SelectedItem != null;
    private bool CanMakeDefault => SelectedItem is { IsDefault: false };

    private void NormalizeDefault()
    {
        // Exactly one default: first flagged, otherwise the top item.
        var def = Items.FirstOrDefault(i => i.IsDefault) ?? Items.FirstOrDefault();
        foreach (var i in Items)
            i.IsDefault = i == def;
        MakeDefaultCommand.NotifyCanExecuteChanged();
    }

    private IEnumerable<string> NamesExcept(InstallationRow? row) =>
        Items.Where(i => i != row).Select(i => i.Name).ToList();

    [RelayCommand]
    private async Task Add()
    {
        if (EditDialog is null) return;
        var result = await EditDialog(null, NamesExcept(null));
        if (result is null) return;
        var row = new InstallationRow { Name = result.Name, Path = result.Path };
        Items.Add(row);
        NormalizeDefault();   // the first item ever added becomes the default
        SelectedItem = row;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Edit()
    {
        if (EditDialog is null || SelectedItem is not { } row) return;
        var result = await EditDialog(new InstallationEdit(row.Name, row.Path), NamesExcept(row));
        if (result is null) return;
        row.Name = result.Name;
        row.Path = result.Path;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        if (SelectedItem is not { } row) return;
        int idx = Items.IndexOf(row);
        Items.Remove(row);
        NormalizeDefault();   // if the default was removed, the top item takes over
        SelectedItem = Items.Count == 0 ? null : Items[Math.Min(idx, Items.Count - 1)];
    }

    [RelayCommand(CanExecute = nameof(CanMakeDefault))]
    private void MakeDefault()
    {
        if (SelectedItem is not { } row) return;
        foreach (var i in Items)
            i.IsDefault = i == row;
        MakeDefaultCommand.NotifyCanExecuteChanged();
    }

    public override string? Save()
    {
        var list = Items.Select(i => new Installation(i.Name, i.Path, i.IsDefault)).ToList();
        try
        {
            InstallationStore.Save(list);
        }
        catch (Exception ex)
        {
            return "Could not write CityHelper.ini:\n" + ex.Message;
        }
        _state.ApplyInstallations(list);
        return null;
    }
}
