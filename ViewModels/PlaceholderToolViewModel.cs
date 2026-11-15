using CityHelper.Services;

namespace CityHelper.ViewModels;

public sealed class PlaceholderToolViewModel : ViewModelBase
{
    public PlaceholderToolViewModel(string toolName, AppState state)
    {
        ToolName = toolName;
        State = state;
    }

    public string ToolName { get; }
    public AppState State { get; }
}
