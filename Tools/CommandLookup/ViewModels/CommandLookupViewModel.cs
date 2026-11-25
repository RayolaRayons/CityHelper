using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CityHelper.Services;
using CityHelper.Services.Emotes;
using CityHelper.Services.SlashCommands;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.CommandLookup.ViewModels;

/// <summary>One parameter line of the detail pane.</summary>
public sealed class ParamRow
{
    public ParamRow(SlashCommandParam p)
    {
        Name = p.Name;
        Type = p.Type;
        Range = p.Min is { } min && p.Max is { } max
            ? $"{min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}"
            : p.Values is { Count: > 0 } values ? string.Join(" | ", values) : "";
        Requirement = p.Optional ? "optional" : "required";
        DefaultText = p.Default is { } d ? d.ToString(CultureInfo.InvariantCulture) : "";
        Numbers = (p.Numbers ?? new List<SlashCommandNumber>())
            .Select(n => $"{n.Value.ToString(CultureInfo.InvariantCulture)} = {n.Meaning}").ToList();
    }

    /// <summary>The value used when the parameter is left out, if the command's page names one.</summary>
    public string DefaultText { get; }
    public bool HasDefault => DefaultText.Length > 0;

    /// <summary>The numbers the command's page lists for this parameter: "0 = Turn ON the particle system".</summary>
    public IReadOnlyList<string> Numbers { get; }
    public bool HasNumbers => Numbers.Count > 0;

    public string Name { get; }
    public string Type { get; }
    public string Range { get; }
    public string Requirement { get; }
}

/// <summary>A slash command as the lookup shows it.</summary>
public sealed class CommandRow
{
    public CommandRow(SlashCommand command)
    {
        Command = command;
        Title = "/" + command.Name;
        AliasText = command.Aliases.Count == 0 ? "" : string.Join(", ", command.Aliases.Select(a => "/" + a));
        KeyText = string.IsNullOrEmpty(command.DefaultKey) ? "" : command.DefaultKey!;
        SyntaxLine = string.IsNullOrEmpty(command.RawSyntax) ? Title : Title + " " + command.RawSyntax;
        Params = command.Params.Select(p => new ParamRow(p)).ToList();
        ToggleText = string.IsNullOrEmpty(command.Toggle) ? "" : "/" + command.Toggle;
        Examples = (command.Examples ?? new List<string>()).Select(e => "/" + e).ToList();
    }

    /// <summary>The toggle form of a 0 / 1 command that flips between the two: /++noparticles.</summary>
    public string ToggleText { get; }
    public bool HasToggle => ToggleText.Length > 0;

    /// <summary>The explanation from the command's own wiki page.</summary>
    public string Details => Command.Details ?? "";
    public bool HasDetails => Details.Length > 0;

    public IReadOnlyList<string> Examples { get; }
    public bool HasExamples => Examples.Count > 0;

    public SlashCommand Command { get; }

    /// <summary>The detail heading, with the slash: /afk.</summary>
    public string Title { get; }

    /// <summary>What the list shows: the name and the aliases without the slash (every command is assumed to have it).</summary>
    public string ListName => Command.Name;
    public string ListAliases => string.Join(", ", Command.Aliases);

    public string Description => Command.Description;
    public string AliasText { get; }
    public bool HasAliases => AliasText.Length > 0;
    public string KeyText { get; }
    public bool HasKey => KeyText.Length > 0;

    /// <summary>The command as it is typed, with its syntax: /angle_snap [0-359].</summary>
    public string SyntaxLine { get; }

    public IReadOnlyList<ParamRow> Params { get; }
    public bool HasParams => Params.Count > 0;
    public string WikiUrl => Command.WikiUrl;

    /// <summary>True if the text is in the name, an alias or the description (ignoring case; a leading / is ignored).</summary>
    public bool Matches(string text)
    {
        text = text.Trim().TrimStart('/');
        return text.Length == 0
            || Command.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Command.Aliases.Any(a => a.Contains(text, StringComparison.OrdinalIgnoreCase))
            || Command.Description.Contains(text, StringComparison.OrdinalIgnoreCase)
            || (Command.Details?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}

/// <summary>An emote as the lookup shows it: the emote, its aliases and its description.</summary>
public sealed class EmoteRow
{
    public EmoteRow(Emote emote) => Emote = emote;

    public Emote Emote { get; }
    public string Name => Emote.Name;
    public string AliasText => string.Join(", ", Emote.Aliases);
    public bool HasAliases => Emote.Aliases.Count > 0;
    public string Description => Emote.Description;
    public bool HasDescription => Emote.Description.Length > 0;

    /// <summary>How the emote is used in the chat line: /e afraid.</summary>
    public string UseLine => "/e " + Emote.Name;

    public bool Matches(string text)
    {
        text = text.Trim().TrimStart('/');
        return text.Length == 0
            || Emote.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Emote.Aliases.Any(a => a.Contains(text, StringComparison.OrdinalIgnoreCase))
            || Emote.Description.Contains(text, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Looks up slash commands and emotes in an accordion (one section open at a time): a searchable list per section, and
/// the details of the selected one.
/// </summary>
public sealed partial class CommandLookupViewModel : ViewModelBase, IStatusSource
{
    private IReadOnlyList<CommandRow> _allCommands = Array.Empty<CommandRow>();
    private IReadOnlyList<EmoteRow> _allEmotes = Array.Empty<EmoteRow>();

    public CommandLookupViewModel() : this(SlashCommandCache.Load(), EmoteCache.Load()) { }

    public CommandLookupViewModel(IEnumerable<SlashCommand> commands) : this(commands, Array.Empty<Emote>()) { }

    public CommandLookupViewModel(IEnumerable<SlashCommand> commands, IEnumerable<Emote> emotes)
    {
        _allCommands = commands.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c => new CommandRow(c)).ToList();
        _allEmotes = emotes.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(e => new EmoteRow(e)).ToList();
        ApplyFilter();
    }

    public ObservableCollection<CommandRow> Commands { get; } = new();
    public ObservableCollection<EmoteRow> Emotes { get; } = new();

    [ObservableProperty] private string _search = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ShowCommandDetail))]
    private CommandRow? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmoteDetail))]
    private EmoteRow? _selectedEmote;

    // --- The accordion: Commands and Emotes, one open at a time ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandsHeader), nameof(ShowCommandDetail), nameof(CountText), nameof(StatusBarText))]
    private bool _isCommandsOpen = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmotesHeader), nameof(ShowEmoteDetail), nameof(ShowCommandDetail), nameof(CountText), nameof(StatusBarText))]
    private bool _isEmotesOpen;

    partial void OnIsCommandsOpenChanged(bool value)
    {
        if (value)
            IsEmotesOpen = false;
    }

    partial void OnIsEmotesOpenChanged(bool value)
    {
        if (value)
            IsCommandsOpen = false;
    }

    [RelayCommand]
    private void ToggleCommands() => IsCommandsOpen = !IsCommandsOpen;

    [RelayCommand]
    private void ToggleEmotes() => IsEmotesOpen = !IsEmotesOpen;

    private string _commandCount = "";
    private string _emoteCount = "";

    public string CommandsHeader => (IsCommandsOpen ? "▼  " : "▶  ") + "Commands  (" + _commandCount + ")";
    public string EmotesHeader => (IsEmotesOpen ? "▼  " : "▶  ") + "Emotes  (" + _emoteCount + ")";

    public bool HasSelection => Selected != null;
    public bool ShowCommandDetail => !IsEmotesOpen && Selected != null;
    public bool ShowEmoteDetail => IsEmotesOpen && SelectedEmote != null;

    /// <summary>"544 commands" / "12 of 544 commands" (or the emotes, while that section is open).</summary>
    public string CountText => IsEmotesOpen
        ? (Search.Trim().Length == 0 ? $"{_allEmotes.Count:N0} emotes" : $"{Emotes.Count:N0} of {_allEmotes.Count:N0} emotes")
        : (Search.Trim().Length == 0 ? $"{_allCommands.Count:N0} commands" : $"{Commands.Count:N0} of {_allCommands.Count:N0} commands");

    /// <summary>Shown at the right of the main status bar.</summary>
    public string StatusBarText => CountText;

    partial void OnSearchChanged(string value) => ApplyFilter();

    private static int Rank(string name, IEnumerable<string> aliases, string text) =>
        name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0
        : name.Contains(text, StringComparison.OrdinalIgnoreCase) ? 1
        : aliases.Any(a => a.Contains(text, StringComparison.OrdinalIgnoreCase)) ? 2 : 3;

    private void ApplyFilter()
    {
        var text = Search.Trim().TrimStart('/');

        // Names first, so typing "arena" lists /arena before the commands that only mention it
        var keepCommand = Selected?.Command.Name;
        var commands = _allCommands.Where(c => c.Matches(Search)).ToList();
        if (text.Length > 0)
            commands = commands.OrderBy(c => Rank(c.Command.Name, c.Command.Aliases, text)).ThenBy(c => c.Command.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Commands.Clear();
        foreach (var row in commands)
            Commands.Add(row);
        Selected = Commands.FirstOrDefault(c => c.Command.Name == keepCommand) ?? Commands.FirstOrDefault();

        var keepEmote = SelectedEmote?.Emote.Name;
        var emotes = _allEmotes.Where(e => e.Matches(Search)).ToList();
        if (text.Length > 0)
            emotes = emotes.OrderBy(e => Rank(e.Emote.Name, e.Emote.Aliases, text)).ThenBy(e => e.Emote.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Emotes.Clear();
        foreach (var row in emotes)
            Emotes.Add(row);
        SelectedEmote = Emotes.FirstOrDefault(e => e.Emote.Name == keepEmote) ?? Emotes.FirstOrDefault();

        _commandCount = text.Length == 0 ? $"{_allCommands.Count:N0}" : $"{Commands.Count:N0} of {_allCommands.Count:N0}";
        _emoteCount = text.Length == 0 ? $"{_allEmotes.Count:N0}" : $"{Emotes.Count:N0} of {_allEmotes.Count:N0}";
        OnPropertyChanged(nameof(CommandsHeader));
        OnPropertyChanged(nameof(EmotesHeader));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(StatusBarText));
    }

    /// <summary>Reads the caches again (after Options > System > Reparse).</summary>
    [RelayCommand]
    public void Refresh()
    {
        _allCommands = SlashCommandCache.Load().OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c => new CommandRow(c)).ToList();
        _allEmotes = EmoteCache.Load().OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(e => new EmoteRow(e)).ToList();
        ApplyFilter();
    }
}
