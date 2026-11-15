using CityHelper.Services;
using CityHelper.ViewModels;

namespace CityHelper.Tools;

/// <summary>Stand-in tool used until real modules exist. Shows its name and the shared selection; it has no ribbon commands.</summary>
public sealed class PlaceholderTool : ITool
{
    public PlaceholderTool(string name) => Name = name;

    public string Name { get; }
    public bool IsComplete => false;
    public ViewModelBase CreateViewModel(AppState state) => new PlaceholderToolViewModel(Name, state);
}
