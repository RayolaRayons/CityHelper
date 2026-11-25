using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using CityHelper.Ribbon;
using CityHelper.Services;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.SystemSection.ViewModels;

/// <summary>One block of the System page: a title and its content (a view model with its own view).</summary>
public sealed class SystemSectionItem
{
    public SystemSectionItem(string title, ViewModelBase content)
    {
        Title = title;
        Content = content;
    }

    public string Title { get; }
    public ViewModelBase Content { get; }
}

/// <summary>A link in the tree on the left; choosing it jumps the page to <see cref="Target"/>.</summary>
public sealed class SystemNode
{
    public SystemNode(string title, SystemSectionItem? target) => (Title, Target) = (title, target);

    public string Title { get; }
    public SystemSectionItem? Target { get; set; }
    public ObservableCollection<SystemNode> Children { get; } = new();
}

public sealed record InfoGroup(string Title, IReadOnlyList<RibbonCommand> Links);

/// <summary>The links the tools offer (forums, wikis...), grouped by tool.</summary>
public sealed class SystemLinksViewModel : ViewModelBase
{
    public SystemLinksViewModel(IReadOnlyList<InfoGroup> groups) => Groups = groups;

    public IReadOnlyList<InfoGroup> Groups { get; }
    public bool HasGroups => Groups.Count > 0;
}

/// <summary>Where things are: the settings file, how it is used, and the installations in use.</summary>
public sealed class SystemInformationViewModel : ViewModelBase
{
    public SystemInformationViewModel(AppState state)
    {
        SettingsFile = IniStore.FilePath;
        StorageMode = IniStore.DataDirectory.Equals(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)
            ? "Standalone: settings are kept next to the program."
            : "Installed: settings are kept in your user profile.";
        Installations = state.Installations.Count == 0
            ? "No installations are set up yet. Add them under Options > Installations."
            : string.Join("\n", state.Installations.Select(i => $"{i.Name}  -  {i.Path}{(i.IsDefault ? "  (default)" : "")}"));
    }

    public string SettingsFile { get; }
    public string StorageMode { get; }
    public string Installations { get; }
}

public sealed class SystemAboutViewModel : ViewModelBase
{
    public SystemAboutViewModel()
    {
        var assembly = Assembly.GetExecutingAssembly();
        Version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
                  ?? assembly.GetName().Version?.ToString(3) ?? "";
    }

    public string Version { get; }
    public string Headline => $"CityHelper {Version}";
}

/// <summary>The Options section: a tree of sections on the left and the chosen section's page on the right.</summary>
public sealed partial class SystemViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly IReadOnlyList<ITool> _tools;
    private List<OptionsSectionViewModel> _optionSections = new();

    public SystemViewModel(AppState state, IReadOnlyList<ITool> tools)
    {
        _state = state;
        _tools = tools;
        Reload();
    }

    public ObservableCollection<SystemSectionItem> Sections { get; } = new();
    public ObservableCollection<SystemNode> Nodes { get; } = new();

    [ObservableProperty] private SystemNode? _selectedNode;
    [ObservableProperty] private SystemSectionItem? _selectedSection;
    [ObservableProperty] private string _statusText = "";

    partial void OnSelectedNodeChanged(SystemNode? value)
    {
        if (value?.Target is { } target)
            SelectedSection = target;
    }

    /// <summary>Rebuilds the page with fresh working copies of every option (drops unsaved edits).</summary>
    public void Reload()
    {
        Sections.Clear();
        Nodes.Clear();
        StatusText = "";

        var information = new SystemSectionItem("Information", new SystemInformationViewModel(_state));
        var about = new SystemSectionItem("About", new SystemAboutViewModel());
        var links = new SystemSectionItem("Links", new SystemLinksViewModel(BuildInfoGroups()));
        Sections.Add(information);
        Sections.Add(about);
        Sections.Add(links);

        _optionSections = new List<OptionsSectionViewModel> { new ThemeOptionsViewModel(), new InstallationsViewModel(_state) };
        _optionSections.AddRange(_tools.Select(t => t.CreateOptionsSection()).OfType<OptionsSectionViewModel>());

        var optionNode = new SystemNode("Options", null);
        SystemNode? systemNode = null, logsNode = null;
        foreach (var section in _optionSections)
        {
            var item = new SystemSectionItem(section.Title, section);
            Sections.Add(item);
            if (section is ThemeOptionsViewModel)
                systemNode = new SystemNode(section.Title, item);       // System sits at the top level, not under Options
            else if (section.Title is "Log Manager" or "Log Viewer")
            {
                // The log tools' options sit together under one "Logs" heading
                if (logsNode is null)
                {
                    logsNode = new SystemNode("Logs", null);
                    optionNode.Children.Add(logsNode);
                }
                logsNode.Children.Add(new SystemNode(section.Title, item));
                logsNode.Target ??= item;
            }
            else
                optionNode.Children.Add(new SystemNode(section.Title, item));
        }
        optionNode.Target = optionNode.Children.FirstOrDefault()?.Target;

        Nodes.Add(new SystemNode("Information", information));
        Nodes.Add(new SystemNode("About", about));
        Nodes.Add(new SystemNode("Links", links));
        if (systemNode != null)
            Nodes.Add(systemNode);
        Nodes.Add(optionNode);

        SelectedNode = null;
        SelectedNode = Nodes[0];        // opens on Information
    }

    private IReadOnlyList<InfoGroup> BuildInfoGroups()
    {
        var groups = new List<InfoGroup>();
        foreach (var tool in _tools)
            foreach (var g in tool.InformationGroups.Where(g => g.Commands.Count > 0))
                groups.Add(new InfoGroup($"{tool.Name}: {g.Header}", g.Commands));
        return groups;
    }

    [RelayCommand]
    private void Save()
    {
        var saved = new List<OptionsSectionViewModel>();
        string? failure = null;
        foreach (var section in _optionSections)
        {
            var error = section.Save();
            if (error != null)
            {
                failure = $"{section.Title}: {error}";
                break;
            }
            saved.Add(section);
        }

        // Tools reload what was written, even when a later section failed
        foreach (var section in saved)
            section.NotifySaved();

        StatusText = failure ?? "Options saved.";
    }

    [RelayCommand]
    private void Revert() => Reload();
}
