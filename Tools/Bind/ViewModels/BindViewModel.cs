using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CityHelper.Models;
using CityHelper.Services;
using CityHelper.Tools.ConfigSync;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.Bind.ViewModels;

public enum BindSectionKind { Keybinds, BindsFolder, BindsTxt }

public sealed record BindSection(string Label, BindSectionKind Kind)
{
    public override string ToString() => Label;
}

/// <summary>One editable line: key on the left, command on the right.</summary>
public sealed partial class BindRowViewModel : ObservableObject
{
    private readonly Action _changed;
    private readonly Action<BindRowViewModel> _remove;

    public BindRowViewModel(string key, string command, Action changed, Action<BindRowViewModel> remove)
    {
        _key = key;
        _command = command;
        _changed = changed;
        _remove = remove;
    }

    /// <summary>The command was wrapped in double quotes in the file; they are not shown, and are written back on save.</summary>
    public bool Quoted { get; set; }

    [ObservableProperty] private string _key;
    [ObservableProperty] private string _command;

    partial void OnKeyChanged(string value) => _changed();
    partial void OnCommandChanged(string value) => _changed();

    [RelayCommand]
    private void Remove() => _remove(this);
}

/// <summary>Edits settings\&lt;env&gt;\keybinds.txt, the files in \Binds\, or binds.txt of the selected installation.</summary>
public sealed partial class BindViewModel : ViewModelBase
{
    private readonly AppState _state;
    private string? _path;          // the file the rows were loaded from (so pending edits go to the right place)
    private bool _loading;

    // Whether lines added in the current file get the outer quotes (keybinds.txt uses them, the sample binds files don't)
    private bool _quoteNew;

    public BindViewModel(AppState state)
    {
        _state = state;
        Sections = new[]
        {
            new BindSection(@"Keybinds  (settings\<env>\keybinds.txt)", BindSectionKind.Keybinds),
            new BindSection(@"Binds folder  (\Binds\)", BindSectionKind.BindsFolder),
            new BindSection(@"binds.txt", BindSectionKind.BindsTxt),
        };
        _selectedSection = Sections[0];
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.SelectedInstallation))
                LoadSection();
        };
        LoadSection();
    }

    public BindSection[] Sections { get; }
    public ObservableCollection<string> Files { get; } = new();
    public ObservableCollection<BindRowViewModel> Rows { get; } = new();

    /// <summary>Asks for the name of a new binds file; null if cancelled. Provided by the view.</summary>
    public Func<Task<string?>>? PromptFileName { get; set; }

    [ObservableProperty]
    private BindSection _selectedSection;

    [ObservableProperty]
    private string? _selectedFile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewFileCommand))]
    private bool _isFolderSection;

    /// <summary>True when there is a file to edit (a section selected on an installation, and a file chosen for \Binds\).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRowCommand))]
    private bool _canEdit;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    partial void OnSelectedSectionChanged(BindSection value)
    {
        SaveIfDirty();
        LoadSection();
    }

    partial void OnSelectedFileChanged(string? value)
    {
        if (_loading)
            return;
        SaveIfDirty();
        _path = FolderFilePath(value);
        LoadRows();
    }

    private string? InstallPath => _state.SelectedInstallation?.Path;

    private string? FolderFilePath(string? file) =>
        InstallPath is { } p && file != null ? Path.Combine(p, "Binds", file) : null;

    private string GameEnvironment(Installation inst)
    {
        var settings = ConfigSyncSettings.Load(
            _state.Installations.Select(i => i.Name).ToList(),
            _state.Installations.FirstOrDefault(i => i.IsDefault)?.Name);
        return settings.Find(inst.Name)?.GameEnvironment ?? SyncItems.DefaultGameEnvironment;
    }

    /// <summary>Re-reads the current file. Unsaved changes are discarded.</summary>
    [RelayCommand]
    public void Reload()
    {
        IsDirty = false;
        LoadSection(SelectedFile);
    }

    private void LoadSection(string? preferFile = null)
    {
        SaveIfDirty();
        _loading = true;
        var previousFile = preferFile ?? SelectedFile;
        Files.Clear();
        IsFolderSection = SelectedSection.Kind == BindSectionKind.BindsFolder;
        _path = null;

        if (_state.SelectedInstallation is { } inst)
        {
            switch (SelectedSection.Kind)
            {
                case BindSectionKind.Keybinds:
                    _path = Path.Combine(inst.Path, "settings", GameEnvironment(inst), "keybinds.txt");
                    break;
                case BindSectionKind.BindsTxt:
                    _path = Path.Combine(inst.Path, "binds.txt");
                    break;
                case BindSectionKind.BindsFolder:
                    foreach (var f in BindFile.ListFiles(Path.Combine(inst.Path, "Binds")))
                        Files.Add(f);
                    SelectedFile = Files.FirstOrDefault(f => f.Equals(previousFile, StringComparison.OrdinalIgnoreCase)) ?? Files.FirstOrDefault();
                    _path = FolderFilePath(SelectedFile);
                    break;
            }
        }
        if (SelectedSection.Kind != BindSectionKind.BindsFolder)
            SelectedFile = null;

        _loading = false;
        LoadRows();
    }

    private void LoadRows()
    {
        Rows.Clear();
        IsDirty = false;
        CanEdit = _path != null;
        _quoteNew = SelectedSection.Kind == BindSectionKind.Keybinds;     // until the file shows its own style

        if (_state.SelectedInstallation is null)
        {
            StatusText = "Select an installation.";
            return;
        }
        if (_path is null)
        {
            StatusText = IsFolderSection
                ? "No binds files in the Binds folder. Use New Binds File to add one."
                : "";
            return;
        }
        if (!File.Exists(_path))
        {
            StatusText = $"{Path.GetFileName(_path)} does not exist yet; it is created when you save.";
            return;
        }

        try
        {
            foreach (var e in BindFile.Load(_path))
                Rows.Add(NewRow(e.Key, e.Command));
            if (Rows.Count > 0)
                _quoteNew = Rows.Count(r => r.Quoted) * 2 > Rows.Count;      // new lines follow the style of the file
            StatusText = _path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not read " + Path.GetFileName(_path) + ": " + ex.Message;
            CanEdit = false;
        }
        IsDirty = false;
    }

    private BindRowViewModel NewRow(string key, string rawCommand)
    {
        var command = Unquote(rawCommand, out var quoted);
        var row = new BindRowViewModel(key, command, () => IsDirty = true, r =>
        {
            Rows.Remove(r);
            IsDirty = true;
        }) { Quoted = quoted };
        return row;
    }

    private BindRowViewModel NewBlankRow() => new("", "", () => IsDirty = true, r =>
    {
        Rows.Remove(r);
        IsDirty = true;
    }) { Quoted = _quoteNew };

    private static string Unquote(string text, out bool quoted)
    {
        quoted = text.Length >= 2 && text[0] == '"' && text[^1] == '"';
        return quoted ? text[1..^1] : text;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddRow()
    {
        Rows.Add(NewBlankRow());
        IsDirty = true;
    }

    [RelayCommand(CanExecute = nameof(IsFolderSection))]
    private async Task NewFile()
    {
        if (PromptFileName is null || InstallPath is not { } installPath)
            return;
        SaveIfDirty();

        var input = (await PromptFileName())?.Trim();
        if (string.IsNullOrEmpty(input))
            return;

        var name = Path.HasExtension(input) ? input : input + ".txt";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            StatusText = "That file name contains characters that are not allowed.";
            return;
        }
        if (Files.Any(f => f.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = $"{name} already exists.";
            return;
        }

        try
        {
            var dir = Path.Combine(installPath, "Binds");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name), "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not create " + name + ": " + ex.Message;
            return;
        }
        LoadSection(name);
        StatusText = $"Created {name}.";
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Save() => TrySave();

    private void SaveIfDirty()
    {
        if (IsDirty && _path != null)
            TrySave();
    }

    private bool TrySave()
    {
        if (_path is null)
            return false;

        var entries = new System.Collections.Generic.List<BindEntry>();
        foreach (var row in Rows)
        {
            var key = string.Concat(row.Key.Where(c => !char.IsWhiteSpace(c)));
            var command = row.Command.Trim();
            if (row.Quoted && command.Length > 0 && !(command.Length >= 2 && command[0] == '"' && command[^1] == '"'))
                command = "\"" + command + "\"";       // the quotes the game expects around this command
            if (key.Length == 0 && command.Length == 0)
                continue;
            if (key.Length == 0)
            {
                StatusText = "A bind has a command but no key; enter a key or remove the line.";
                return false;
            }
            entries.Add(new BindEntry(key, command));
        }

        try
        {
            BindFile.Save(_path, entries);
            IsDirty = false;
            StatusText = "Saved " + Path.GetFileName(_path) + " (original kept in the backup folder).";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Could not save " + Path.GetFileName(_path) + ": " + ex.Message;
            return false;
        }
    }
}
