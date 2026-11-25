using System.Collections.ObjectModel;
using System.Linq;

namespace CityHelper.Ribbon;

/// <summary>A top-level menu. Its groups are flattened into <see cref="Entries"/>, with a separator between groups.</summary>
public sealed class RibbonTab
{
    public RibbonTab(string header)
    {
        Header = header;
        Groups.CollectionChanged += (_, _) => Rebuild();
    }

    public string Header { get; }
    public ObservableCollection<RibbonGroup> Groups { get; } = new();
    public ObservableCollection<RibbonCommand> Entries { get; } = new();

    private void Rebuild()
    {
        Entries.Clear();
        foreach (var group in Groups.Where(g => g.Commands.Count > 0))
        {
            if (Entries.Count > 0) Entries.Add(RibbonCommand.Separator);
            foreach (var c in group.Commands) Entries.Add(c);
        }
    }
}
