using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CityHelper.Models;
using CityHelper.Services;
using CityHelper.Tools.ConfigSync;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CityHelper.ViewModels;

/// <summary>One installation offered by first-run setup.</summary>
public sealed partial class SetupInstallation : ObservableObject
{
    public SetupInstallation(string name, string path, bool isChecked)
    {
        _name = name;
        _path = path;
        _isChecked = isChecked;
    }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _path;
    [ObservableProperty] private bool _isChecked;
}

/// <summary>First-run setup: which City of Heroes installations to use, and optionally Config Sync.</summary>
public sealed partial class SetupViewModel : ViewModelBase
{
    public SetupViewModel()
    {
        foreach (var found in InstallationDetector.Detect())
            Items.Add(new SetupInstallation(UniqueName(found.Name), found.Path, true));

        IntroText = Items.Count == 0
            ? "No City of Heroes installation was found automatically. Add the folder of each installation you want to use."
            : $"Found {Items.Count} installation(s). Untick any you don't want, or add others.";
    }

    public string IntroText { get; }

    public ObservableCollection<SetupInstallation> Items { get; } = new();

    [ObservableProperty]
    private bool _setUpConfigSync;

    [ObservableProperty]
    private string _syncFolder = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _error = "";

    public bool HasError => Error.Length > 0;

    public bool AddFolder(string path)
    {
        path = path.Trim().TrimEnd('\\', '/');
        var existing = Items.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.IsChecked = true;
            return false;
        }
        Items.Add(new SetupInstallation(UniqueName(InstallationDetector.ToInstallation(path).Name), path, true));
        return true;
    }

    private string UniqueName(string name)
    {
        var candidate = name;
        for (int n = 2; Items.Any(i => i.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); n++)
            candidate = $"{name} ({n})";
        return candidate;
    }

    /// <summary>Validates and writes everything; returns false (with <see cref="Error"/> set) if something is wrong.</summary>
    public bool Finish()
    {
        var chosen = Items.Where(i => i.IsChecked).ToList();
        if (chosen.Any(i => string.IsNullOrWhiteSpace(i.Name) || string.IsNullOrWhiteSpace(i.Path)))
        {
            Error = "Every ticked installation needs a name and a folder.";
            return false;
        }
        var dup = chosen.GroupBy(i => i.Name.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup != null)
        {
            Error = $"Two installations are named \"{dup.Key}\". Give each a different name.";
            return false;
        }
        if (SetUpConfigSync && (chosen.Count == 0 || string.IsNullOrWhiteSpace(SyncFolder)))
        {
            Error = chosen.Count == 0
                ? "Config Sync needs at least one installation."
                : "Choose the folder Config Sync should back up to, or untick Config Sync.";
            return false;
        }

        try
        {
            var installations = InstallationStore.Normalize(
                chosen.Select(i => new Installation(i.Name.Trim(), i.Path.Trim())).ToList());
            InstallationStore.Save(installations);

            if (SetUpConfigSync)
            {
                var names = installations.Select(i => i.Name).ToList();
                var settings = ConfigSyncSettings.Load(names, installations.First(i => i.IsDefault).Name);
                if (settings.Find(settings.DefaultEnvironment) is { } env)
                    env.BackupFolder = SyncFolder.Trim();
                settings.Save();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "Could not write CityHelper.ini:\n" + ex.Message;
            return false;
        }

        MarkCompleted();
        return true;
    }

    /// <summary>Records that setup was seen, so it doesn't appear again (used for Finish and Skip).</summary>
    public void MarkCompleted()
    {
        try
        {
            var settings = AppSettings.Load();
            settings.SetupCompleted = true;
            settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Setup will simply be offered again next time
        }
    }
}
