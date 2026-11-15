using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CityHelper.Tools.CharacterOrdering.ViewModels;

/// <summary>One numbered slot in the list; holds a character or is empty.</summary>
public sealed partial class SlotViewModel : ViewModelBase
{
    public const int SectionSize = 10;

    public SlotViewModel(int position) => Position = position;

    public int Position { get; }
    public bool IsSectionStart => Position % SectionSize == 0;
    public string SectionTitle => $"Positions {Position} - {Position + SectionSize - 1}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CharacterName), nameof(HasCharacter), nameof(IsEmpty))]
    private SlotEntry? _entry;

    [ObservableProperty]
    private bool _isDropTarget;

    [ObservableProperty]
    private bool _isDragging;

    public string? CharacterName => Entry?.Name;
    public bool HasCharacter => Entry != null;
    public bool IsEmpty => Entry == null;
}
