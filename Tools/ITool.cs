using System.Collections.Generic;
using CityHelper.Ribbon;
using CityHelper.Services;
using CityHelper.ViewModels;

namespace CityHelper.Tools;

/// <summary>
/// A self-contained tool module: nav entry + content view model (+ optional ribbon groups).
/// The view is located by convention: FooViewModel -> Views/FooView (see ViewLocator).
/// </summary>
public interface ITool
{
    string Name { get; }

    /// <summary>False for placeholder tools; finished tools are listed first in the navigation pane.</summary>
    bool IsComplete => true;
    ViewModelBase CreateViewModel(AppState state);
    IReadOnlyList<RibbonGroup> RibbonGroups => System.Array.Empty<RibbonGroup>();

    /// <summary>Added to the File ribbon tab (New / Open / Save As ...) while this tool is selected.</summary>
    IReadOnlyList<RibbonGroup> FileGroups => System.Array.Empty<RibbonGroup>();

    /// <summary>This tool's section of the combined Options window, or null if it has no options.</summary>
    OptionsSectionViewModel? CreateOptionsSection() => null;

    /// <summary>Shown on the Information ribbon tab while this tool is selected (links, about, ...).</summary>
    IReadOnlyList<RibbonGroup> InformationGroups => System.Array.Empty<RibbonGroup>();

    /// <summary>Invoked by the global File > Save button (and Ctrl+S) while this tool is selected. Null if there is nothing to save.</summary>
    System.Windows.Input.ICommand? SaveCommand => null;

    /// <summary>
    /// Re-reads from disk / rescans. Shown as the standard Refresh on the Home tab (and F5) while this tool is selected.
    /// Null if the tool has no such action. (Commands that throw away unsaved edits are called "Reload from disk" instead.)
    /// </summary>
    System.Windows.Input.ICommand? RefreshCommand => null;
}
