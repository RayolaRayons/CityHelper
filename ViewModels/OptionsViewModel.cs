using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.ViewModels;

/// <summary>The single Options window: every section stacked in one scrollable page, one Save / Cancel.</summary>
public sealed partial class OptionsViewModel : ViewModelBase
{
    public OptionsViewModel(IEnumerable<OptionsSectionViewModel> sections) => Sections = sections.ToList();

    public IReadOnlyList<OptionsSectionViewModel> Sections { get; }

    /// <summary>Raised when the window should close (true = saved).</summary>
    public event Action<bool>? CloseRequested;

    /// <summary>Raised when saving fails, so the view can tell the user.</summary>
    public event Action<string>? SaveFailed;

    [RelayCommand]
    private void Save()
    {
        var saved = new List<OptionsSectionViewModel>();
        foreach (var section in Sections)
        {
            var error = section.Save();
            if (error != null)
            {
                SaveFailed?.Invoke($"{section.Title}: {error}");
                break;
            }
            saved.Add(section);
        }

        // Tools reload what was written, even when a later section failed
        foreach (var section in saved)
            section.NotifySaved();

        if (saved.Count == Sections.Count)
            CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
