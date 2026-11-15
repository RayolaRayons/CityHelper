using System.Windows.Input;

namespace CityHelper.Ribbon;

/// <summary>A ribbon button. The optional tooltip says what it does (and its shortcut, if it has one).</summary>
public sealed record RibbonCommand(string Label, ICommand Command, string? Tooltip = null);
