using System;

namespace CityHelper.ViewModels;

/// <summary>
/// One section of the combined Options window. Edits a working copy; nothing is persisted until the
/// window's Save calls <see cref="Save"/>. The view is located by convention (FooViewModel -> Views/FooView).
/// </summary>
public abstract class OptionsSectionViewModel : ViewModelBase
{
    public abstract string Title { get; }

    /// <summary>Persists this section. Returns null on success, otherwise a message for the user.</summary>
    public abstract string? Save();

    /// <summary>Called by the owning tool after a successful save, so it can pick up the new values.</summary>
    public Action? Saved { get; set; }

    internal void NotifySaved() => Saved?.Invoke();
}
