using System.Windows.Input;
using Avalonia.Media;

namespace CityHelper.Ribbon;

/// <summary>
/// A menu entry. The optional tooltip says what it does (and its shortcut, if it has one).
/// A command with <see cref="IsSeparator"/> set is drawn as a divider between groups.
/// </summary>
public sealed record RibbonCommand(string Label, ICommand? Command, string? Tooltip = null)
{
    public static RibbonCommand Separator { get; } = new("-", null);

    public bool IsSeparator => Command is null && Label == "-";

    /// <summary>Icon picked from the label; null when no icon matches.</summary>
    public Geometry? Icon => IsSeparator ? null : RibbonIcons.For(Label);
}
