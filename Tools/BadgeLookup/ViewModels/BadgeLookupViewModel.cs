using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CityHelper.Services;
using CityHelper.Services.Badges;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.BadgeLookup.ViewModels;

/// <summary>An entry of the category filter: one category with its badge count, or all of them (Name is null).</summary>
public sealed record CategoryChoice(string? Name, int Count)
{
    public override string ToString() => Name is null ? $"All categories  ({Count:N0})" : $"{Name}  ({Count:N0})";
}

/// <summary>A badge as the lookup shows it. Heroes and villains often have different names and texts; both are kept.</summary>
public sealed class BadgeRow
{
    public BadgeRow(Badge badge) => Badge = badge;

    public Badge Badge { get; }

    public string HeroName => Badge.HeroName;
    public string VillainName => Badge.VillainName;
    public bool NamesDiffer => !Badge.HeroName.Equals(Badge.VillainName, StringComparison.Ordinal) && Badge.VillainName.Length > 0;

    /// <summary>"Adamant / Ironman" when the hero and villain names differ, otherwise the one name.</summary>
    public string Title => NamesDiffer ? $"{Badge.HeroName} / {Badge.VillainName}" : Badge.HeroName;

    public string Category => Badge.Category;
    public string Description => Badge.Description;
    public string VillainDescription => Badge.VillainDescription;
    public bool DescriptionsDiffer => Badge.VillainDescription.Length > 0 && !Badge.VillainDescription.Equals(Badge.Description, StringComparison.Ordinal);

    public string Requirements => Badge.Requirements;
    public bool HasRequirements => Badge.Requirements.Length > 0;
    public string InternalName => Badge.InternalName;
    public string BadgeId => Badge.BadgeId;

    /// <summary>The icon names (hero, and villain when it is a different one).</summary>
    public string IconText => Badge.VillainIcon.Length > 0 && !Badge.VillainIcon.Equals(Badge.Icon, StringComparison.Ordinal)
        ? $"{Badge.Icon}  (villain: {Badge.VillainIcon})" : Badge.Icon;

    public string ReleaseDate => Badge.ReleaseDate;
    public bool HasReleaseDate => Badge.ReleaseDate.Length > 0;
    public string Notes => Badge.Notes;
    public bool HasNotes => Badge.Notes.Length > 0;

    /// <summary>True if the text is in a name, the internal name, the id or a description (ignoring case).</summary>
    public bool Matches(string text)
    {
        text = text.Trim();
        return text.Length == 0
            || Badge.HeroName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Badge.VillainName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Badge.InternalName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Badge.BadgeId.Equals(text, StringComparison.OrdinalIgnoreCase)
            || Badge.Description.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Badge.VillainDescription.Contains(text, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Looks up badges (from badges-all.json): a searchable list with a category filter, and the selected badge's details.</summary>
public sealed partial class BadgeLookupViewModel : ViewModelBase, IStatusSource
{
    private IReadOnlyList<BadgeRow> _all = Array.Empty<BadgeRow>();

    public BadgeLookupViewModel() : this(BadgeCache.Load())
    {
        BadgeCache.Updated += () => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
    }

    public BadgeLookupViewModel(IEnumerable<Badge> badges) => SetBadges(badges);

    public ObservableCollection<BadgeRow> Badges { get; } = new();

    public IReadOnlyList<CategoryChoice> Categories { get; private set; } = Array.Empty<CategoryChoice>();

    [ObservableProperty] private string _search = "";

    [ObservableProperty] private CategoryChoice? _selectedCategory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private BadgeRow? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBarText))]
    private string _countText = "";

    public bool HasSelection => Selected != null;

    /// <summary>Shown at the right of the main status bar.</summary>
    public string StatusBarText => CountText;

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(CategoryChoice? value) => ApplyFilter();

    private void SetBadges(IEnumerable<Badge> badges)
    {
        _all = badges.Select(b => new BadgeRow(b)).OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase).ToList();

        var keep = SelectedCategory?.Name;
        var categories = _all.GroupBy(b => b.Category).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                             .Select(g => new CategoryChoice(g.Key, g.Count())).ToList();
        Categories = new[] { new CategoryChoice(null, _all.Count) }.Concat(categories).ToList();
        OnPropertyChanged(nameof(Categories));
        _selectedCategory = Categories.FirstOrDefault(c => c.Name == keep) ?? Categories[0];
        OnPropertyChanged(nameof(SelectedCategory));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var text = Search.Trim();
        var category = SelectedCategory?.Name;
        var keep = Selected?.Badge.InternalName;

        var matches = _all.Where(b => (category is null || b.Category == category) && b.Matches(text)).ToList();
        if (text.Length > 0)
            matches = matches.OrderBy(b => b.HeroName.StartsWith(text, StringComparison.OrdinalIgnoreCase) || b.VillainName.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0
                                         : b.HeroName.Contains(text, StringComparison.OrdinalIgnoreCase) || b.VillainName.Contains(text, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                             .ThenBy(b => b.Title, StringComparer.OrdinalIgnoreCase).ToList();

        Badges.Clear();
        foreach (var row in matches)
            Badges.Add(row);
        CountText = text.Length == 0 && category is null ? $"{_all.Count:N0} badges" : $"{matches.Count:N0} of {_all.Count:N0} badges";
        Selected = Badges.FirstOrDefault(b => b.Badge.InternalName == keep) ?? Badges.FirstOrDefault();
    }

    /// <summary>Reads the badge list again (after importing a newer badges-all.json).</summary>
    [RelayCommand]
    public void Refresh() => SetBadges(BadgeCache.Load());
}
