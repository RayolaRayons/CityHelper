using System.Collections.ObjectModel;

namespace CityHelper.Ribbon;

public sealed class RibbonTab
{
    public RibbonTab(string header) => Header = header;
    public string Header { get; }
    public ObservableCollection<RibbonGroup> Groups { get; } = new();
}
