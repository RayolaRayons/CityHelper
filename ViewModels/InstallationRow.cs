using CommunityToolkit.Mvvm.ComponentModel;

namespace CityHelper.ViewModels;

/// <summary>Editable working copy of one installation, used by the Set Installation dialog.</summary>
public sealed partial class InstallationRow : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name = "";

    [ObservableProperty]
    private string _path = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private bool _isDefault;

    public string DisplayName => IsDefault ? Name + " (default)" : Name;
}
