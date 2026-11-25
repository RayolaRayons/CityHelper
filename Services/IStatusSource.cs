namespace CityHelper.Services;

/// <summary>A tool view model whose message is shown at the right of the main window's status bar.</summary>
public interface IStatusSource
{
    string StatusBarText { get; }
}
