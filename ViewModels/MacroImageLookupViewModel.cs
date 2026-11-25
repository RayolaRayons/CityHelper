using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using CityHelper.Services.MacroImages;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CityHelper.ViewModels;

/// <summary>A line of the lookup table: a section heading, or one image with its texture name.</summary>
public sealed class MacroImageRow
{
    private Bitmap? _image;
    private bool _loaded;

    public MacroImageRow(string heading) => Heading = heading;

    public MacroImageRow(MacroImageEntry entry) => Entry = entry;

    public string Heading { get; } = "";
    public MacroImageEntry? Entry { get; }
    public bool IsHeading => Entry is null;
    public bool IsImage => Entry is not null;
    public string Name => Entry?.Name ?? "";

    /// <summary>The picture, read from the cache the first time the row is shown (null if it isn't downloaded or can't be read).</summary>
    public Bitmap? Image
    {
        get
        {
            if (!_loaded && Entry is not null)
            {
                _loaded = true;
                try
                {
                    var path = MacroImageCache.ImagePath(Entry);
                    if (File.Exists(path))
                        _image = new Bitmap(path);
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or InvalidOperationException)
                {
                    _image = null;
                }
            }
            return _image;
        }
    }
}

/// <summary>The macro image lookup: sections, pictures and texture names, with a search and a section filter.</summary>
public sealed partial class MacroImageLookupViewModel : ViewModelBase
{
    public const string AllSections = "All sections";

    private readonly List<MacroImageEntry> _all;

    public MacroImageLookupViewModel()
    {
        _all = MacroImageCache.Load()?.Entries ?? new List<MacroImageEntry>();
        SectionNames = new[] { AllSections }.Concat(_all.Select(e => e.Section).Distinct()).ToList();
        _selectedSection = AllSections;
        Rebuild();
    }

    public IReadOnlyList<string> SectionNames { get; }

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _selectedSection;
    [ObservableProperty] private IReadOnlyList<MacroImageRow> _rows = Array.Empty<MacroImageRow>();
    [ObservableProperty] private string _countText = "";

    partial void OnSearchChanged(string value) => Rebuild();
    partial void OnSelectedSectionChanged(string value) => Rebuild();

    /// <summary>Filtered images in page order, with a heading line wherever the section / group changes.</summary>
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
        CountText = $"{matches.Count:N0} of {_all.Count:N0}";
    }
}
