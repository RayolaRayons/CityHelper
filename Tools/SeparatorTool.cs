using System;
using CityHelper.Services;
using CityHelper.ViewModels;

namespace CityHelper.Tools;

/// <summary>The divider line in the navigation list. Drawn as a line and not selectable.</summary>
public sealed class SeparatorTool : ITool
{
    public string Name => "";
    public bool IsSeparator => true;
    public ViewModelBase CreateViewModel(AppState state) => throw new InvalidOperationException("The divider has no page.");
}
