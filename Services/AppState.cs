using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CityHelper.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CityHelper.Services;

/// <summary>
/// Shared application state. Tools read this and observe changes via INotifyPropertyChanged
/// (bind to it, or subscribe to PropertyChanged).
/// </summary>
public sealed partial class AppState : ObservableObject
{
    public ObservableCollection<Installation> Installations { get; } = new();

    /// <summary>Accounts of the selected installation: each subfolder of its "accounts" folder.</summary>
    public ObservableCollection<Account> Accounts { get; } = new();

    [ObservableProperty]
    private Installation? _selectedInstallation;

    [ObservableProperty]
    private Account? _selectedAccount;

    public AppState()
    {
        // Saved list from CityHelper.ini; empty until first-run setup or Options adds some.
        ApplyInstallations(InstallationStore.Load() ?? new List<Installation>());
    }

    partial void OnSelectedInstallationChanged(Installation? value) => LoadAccounts(value);

    /// <summary>Rescans the selected installation's accounts folder, keeping the selected account if it still exists.</summary>
    public void LoadAccounts(Installation? installation)
    {
        var names = new List<string>();
        try
        {
            var dir = installation is null ? null : System.IO.Path.Combine(installation.Path, "accounts");
            if (dir != null && System.IO.Directory.Exists(dir))
                names = System.IO.Directory.EnumerateDirectories(dir)
                    .Select(d => System.IO.Path.GetFileName(d))
                    .OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
        catch (System.Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.ArgumentException)
        {
            // Unreadable folder: treat as no accounts.
        }

        var previous = SelectedAccount?.Name;
        Accounts.Clear();
        foreach (var n in names)
            Accounts.Add(new Account(n));
        SelectedAccount = Accounts.FirstOrDefault(a => a.Name == previous) ?? Accounts.FirstOrDefault();
    }

    /// <summary>Replaces the installation list, keeping the current selection if it still exists (by name), else the default.</summary>
    public void ApplyInstallations(IEnumerable<Installation> items)
    {
        var list = InstallationStore.Normalize(items.ToList());
        var previous = SelectedInstallation?.Name;

        Installations.Clear();
        foreach (var i in list)
            Installations.Add(i);

        SelectedInstallation = Installations.FirstOrDefault(i => i.Name == previous)
                               ?? Installations.FirstOrDefault(i => i.IsDefault);
    }
}
