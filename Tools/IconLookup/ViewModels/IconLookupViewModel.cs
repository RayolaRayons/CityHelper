using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CityHelper.Services;
using CityHelper.Services.MacroImages;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.IconLookup.ViewModels;

/// <summary>The icon lookup: the macro image cache as a searchable list with a section filter and a picture of the selected icon.</summary>
public sealed partial class IconLookupViewModel : ViewModelBase, IStatusSource
{
    public const string AllSections = "All sections";

    private List<MacroImageEntry> _all = new();

    public IconLookupViewModel()
    {
        SectionNames = new[] { AllSections };
        _selectedSection = AllSections;
        Load();
        if (_all.Count == 0)
            _ = SeedAsync();
    }

    public IconLookupViewModel(IEnumerable<MacroImageEntry> entries)
    {
        SectionNames = new[] { AllSections };
        _selectedSection = AllSections;
        SetEntries(entries);
    }

    [ObservableProperty] private IReadOnlyList<string> _sectionNames;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _selectedSection;
    [ObservableProperty] private IReadOnlyList<MacroImageRow> _rows = Array.Empty<MacroImageRow>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedName), nameof(SelectedGroup))]
    private MacroImageRow? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBarText))]
    private string _countText = "";

    public bool HasSelection => Selected?.Entry is not null;
    public string SelectedName => Selected?.Name ?? "";
    public string SelectedGroup => Selected?.Entry is { } e ? (e.Group.Length == 0 ? e.Section : $"{e.Section}  -  {e.Group}") : "";

    public string StatusBarText => CountText;

    partial void OnSearchChanged(string value) => Rebuild();
    partial void OnSelectedSectionChanged(string value) => Rebuild();

    private async Task SeedAsync()
    {
        await Task.Run(() => MacroImageCache.SeedIfMissing());
        Load();
    }

    private void Load() => SetEntries(MacroImageCache.Load()?.Entries ?? new List<MacroImageEntry>());

    private void SetEntries(IEnumerable<MacroImageEntry> entries)
    {
        _all = entries.ToList();
        var keep = SelectedSection;
        SectionNames = new[] { AllSections }.Concat(_all.Select(e => e.Section).Distinct()).ToList();
        SelectedSection = SectionNames.Contains(keep) ? keep : AllSections;
        Rebuild();
    }

    private void Rebuild()
    {
        var text = Search.Trim();
        var matches = _all.Where(e =>
            (SelectedSection == AllSections || e.Section == SelectedSection) &&
            (text.Length == 0 || e.Name.Contains(text, StringComparison.OrdinalIgnoreCase))).ToList();

        var rows = new List<MacroImageRow>(matches.Count + 20);
        string? lastHeading = null;
        foreach (var e in matches)
        {
            var heading = e.Group.Length == 0 ? e.Section : $"{e.Section}  -  {e.Group}";
            if (heading != lastHeading)
            {
                rows.Add(new MacroImageRow(heading));
                lastHeading = heading;
            }
            rows.Add(new MacroImageRow(e));
        }
        Rows = rows;
        CountText = _all.Count == 0 ? "No icons" : $"{matches.Count:N0} of {_all.Count:N0} icons";
        Selected = rows.FirstOrDefault(r => r.IsImage);
    }

    /// <summary>Reads the icon cache again (after a sync in Options).</summary>
    [RelayCommand]
    public void Refresh() => Load();
}
