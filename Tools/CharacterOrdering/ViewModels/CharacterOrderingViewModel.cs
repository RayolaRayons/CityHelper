using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CityHelper.Services;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.CharacterOrdering.ViewModels;

public sealed partial class CharacterOrderingViewModel : ViewModelBase
{
    private readonly AppState _state;
    private PlayerSlotFile? _file;
    private string? _filePath;

    public CharacterOrderingViewModel(AppState state)
    {
        _state = state;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.SelectedInstallation) or nameof(AppState.SelectedAccount))
                Reload();
        };
        Reload();
    }

    /// <summary>Servers that appear in playerslot.txt for the selected installation/account.</summary>
    public ObservableCollection<string> Servers { get; } = new();

    public ObservableCollection<SlotViewModel> Slots { get; } = new();

    [ObservableProperty]
    private string? _selectedServer;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    partial void OnSelectedServerChanged(string? value) => BuildSlots();

    /// <summary>Re-reads playerslot.txt. Unsaved changes are discarded.</summary>
    [RelayCommand]
    public void Reload()
    {
        var previousServer = SelectedServer;
        _file = null;
        _filePath = null;
        Servers.Clear();
        IsDirty = false;

        if (_state.SelectedInstallation is not { } inst || _state.SelectedAccount is not { } account)
        {
            StatusText = "Select an installation and account.";
            SelectedServer = null;
            BuildSlots();
            return;
        }

        var path = Path.Combine(inst.Path, "accounts", account.Name, "playerslot.txt");
        if (!File.Exists(path))
        {
            StatusText = $"No playerslot.txt found for {account.Name}.";
            SelectedServer = null;
            BuildSlots();
            return;
        }

        try
        {
            _file = PlayerSlotFile.Load(path);
            _filePath = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not read playerslot.txt: " + ex.Message;
            SelectedServer = null;
            BuildSlots();
            return;
        }

        foreach (var server in _file.Entries.Select(e => e.Server).Distinct(StringComparer.Ordinal))
            Servers.Add(server);

        StatusText = Servers.Count == 0 ? "playerslot.txt has no characters." : "";
        var next = previousServer != null && Servers.Contains(previousServer) ? previousServer : Servers.FirstOrDefault();
        if (next == SelectedServer)
            BuildSlots();      // property unchanged, so the change handler won't run
        else
            SelectedServer = next;
    }

    private void BuildSlots()
    {
        Slots.Clear();
        if (_file is null || SelectedServer is null)
            return;

        // Entries of this server in position order (ties keep file order)
        var entries = _file.Entries.Where(e => e.Server == SelectedServer)
            .Select((e, i) => (e, i)).OrderBy(x => x.e.Position).ThenBy(x => x.i).Select(x => x.e).ToList();

        // Duplicate positions in the file: push the later one to the next free slot (and mark as changed)
        var taken = new HashSet<int>();
        foreach (var e in entries)
        {
            int pos = e.Position;
            while (!taken.Add(pos)) pos++;
            if (pos != e.Position)
            {
                e.Position = pos;
                IsDirty = true;
            }
        }

        int max = entries.Count == 0 ? -1 : entries.Max(e => e.Position);
        int slotCount = (Math.Max(max, 0) / SlotViewModel.SectionSize + 1) * SlotViewModel.SectionSize;
        for (int i = 0; i < slotCount; i++)
            Slots.Add(new SlotViewModel(i));
        foreach (var e in entries)
            Slots[e.Position].Entry = e;
        EnsureSpareSection();
    }

    /// <summary>Always keep one empty section at the end so a character can be moved past the last used slot.</summary>
    private void EnsureSpareSection()
    {
        if (Slots.Count == 0)
            return;
        int lastStart = Slots.Count - SlotViewModel.SectionSize;
        if (Slots.Skip(lastStart).Any(s => s.HasCharacter))
            for (int i = 0; i < SlotViewModel.SectionSize; i++)
                Slots.Add(new SlotViewModel(Slots.Count));
    }

    /// <summary>Moves a character to <paramref name="target"/>; swaps with whoever is there.</summary>
    public void Move(SlotViewModel source, SlotViewModel target)
    {
        if (source == target || source.Entry is null)
            return;

        var moving = source.Entry;
        var displaced = target.Entry;
        target.Entry = moving;
        source.Entry = displaced;
        moving.Position = target.Position;
        if (displaced != null)
            displaced.Position = source.Position;

        IsDirty = true;
        EnsureSpareSection();
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Save()
    {
        if (_file is null || _filePath is null)
            return;
        try
        {
            _file.Save(_filePath);
            IsDirty = false;
            StatusText = "Saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not save playerslot.txt: " + ex.Message;
        }
    }
}
