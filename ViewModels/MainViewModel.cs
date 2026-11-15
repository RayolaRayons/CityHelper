using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CityHelper.Ribbon;
using CityHelper.Services;
using CityHelper.Tools;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly Dictionary<ITool, ViewModelBase> _viewModels = new();

    // CityHelper has no project site yet. Set HasProjectSite to true and fill in the two URLs to show
    // "Latest Version" and "GitHub" on the Information tab.
    private static readonly bool HasProjectSite = false;
    private const string GitHubUrl = "";
    private const string LatestVersionUrl = "";

    private readonly RibbonTab _fileTab = new("File");
    private readonly RibbonTab _homeTab = new("Home");
    private readonly RibbonTab _infoTab = new("Information");
    private readonly RibbonGroup _appGroup;
    private readonly RibbonGroup _aboutGroup;

    // Follow the selected tool, and are disabled when it has nothing to save / refresh
    private readonly ForwardingCommand _save = new();
    private readonly ForwardingCommand _refresh = new();

    public MainViewModel()
    {
        State = new AppState();
        Tools = ToolRegistry.CreateAll(State);
        var windows = new WindowService(State, Tools);

        // File tab: one App group (Save, Options, Quit), then the selected tool's own file commands
        _appGroup = new RibbonGroup("App", new[]
        {
            new RibbonCommand("Save", _save, "Save the current tool's changes (Ctrl+S)"),
            new RibbonCommand("Options", new AsyncRelayCommand(windows.ShowOptionsAsync),
                "Installations and every tool's options"),
            new RibbonCommand("Quit", new RelayCommand(Quit)),
        });

        // Information tab: About is always there, then the selected tool's links
        var about = new List<RibbonCommand> { new("About", new AsyncRelayCommand(windows.ShowAboutAsync)) };
        if (HasProjectSite)
        {
            about.Add(new("Latest Version", new RelayCommand(() => windows.OpenUrl(LatestVersionUrl))));
            about.Add(new("GitHub", new RelayCommand(() => windows.OpenUrl(GitHubUrl))));
        }
        _aboutGroup = new RibbonGroup("About", about);

        RibbonTabs = new[] { _fileTab, _homeTab, _infoTab };
        _selectedRibbonTab = _homeTab;

        SelectedTool = Tools.FirstOrDefault();
    }

    public AppState State { get; }
    public IReadOnlyList<ITool> Tools { get; }
    public IReadOnlyList<RibbonTab> RibbonTabs { get; }

    /// <summary>Ctrl+S: the selected tool's Save.</summary>
    public ICommand SaveCommand => _save;

    /// <summary>F5: the selected tool's Refresh.</summary>
    public ICommand RefreshCommand => _refresh;

    private RibbonTab? _selectedRibbonTab;
    public RibbonTab? SelectedRibbonTab
    {
        get => _selectedRibbonTab;
        set => SetProperty(ref _selectedRibbonTab, value);
    }

    private ITool? _selectedTool;
    public ITool? SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (SetProperty(ref _selectedTool, value))
                OnToolChanged(value);
        }
    }

    private ViewModelBase? _currentContent;
    public ViewModelBase? CurrentContent
    {
        get => _currentContent;
        private set => SetProperty(ref _currentContent, value);
    }

    private static void Quit() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    private void OnToolChanged(ITool? tool)
    {
        _fileTab.Groups.Clear();
        _homeTab.Groups.Clear();
        _infoTab.Groups.Clear();

        _save.SetTarget(tool?.SaveCommand);
        _refresh.SetTarget(tool?.RefreshCommand);

        _fileTab.Groups.Add(_appGroup);
        if (tool != null)
            foreach (var g in tool.FileGroups) _fileTab.Groups.Add(g);

        _infoTab.Groups.Add(_aboutGroup);
        if (tool is null) { CurrentContent = null; return; }

        // Home: the standard Refresh first (same place for every tool that has one), then the tool's own groups
        if (tool.RefreshCommand != null)
            _homeTab.Groups.Add(new RibbonGroup("Data", new[]
            {
                new RibbonCommand("Refresh", _refresh, "Re-read from disk and rescan (F5)"),
            }));
        foreach (var g in tool.RibbonGroups) _homeTab.Groups.Add(g);
        foreach (var g in tool.InformationGroups) _infoTab.Groups.Add(g);

        if (!_viewModels.TryGetValue(tool, out var vm))
            _viewModels[tool] = vm = tool.CreateViewModel(State);
        CurrentContent = vm;
    }
}
