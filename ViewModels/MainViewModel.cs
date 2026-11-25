using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using CityHelper.Ribbon;
using CityHelper.Services;
using CityHelper.Tools;
using CityHelper.Tools.SystemSection;
using CityHelper.Tools.SystemSection.ViewModels;

namespace CityHelper.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly Dictionary<ITool, ViewModelBase> _viewModels = new();

    // Follow the selected tool, and are disabled when it has nothing to save / refresh
    private readonly ForwardingCommand _save = new();
    private readonly ForwardingCommand _refresh = new();

    public MainViewModel()
    {
        ToolMenus.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasToolMenus));
        State = new AppState();

        // The System section (information, about, installations and every tool's options) is always last
        var tools = ToolRegistry.CreateAll(State).ToList();
        tools.Add(new SystemTool(State, tools.Where(t => !t.IsSeparator).ToList()));
        Tools = tools;

        SelectedTool = Tools.FirstOrDefault(t => !t.IsSeparator);
    }

    public AppState State { get; }
    public IReadOnlyList<ITool> Tools { get; }

    /// <summary>Menus of the selected tool, shown in a menu bar above its content.</summary>
    public ObservableCollection<RibbonGroup> ToolMenus { get; } = new();

    public bool HasToolMenus => ToolMenus.Count > 0;

    /// <summary>Ctrl+S: the selected tool's Save.</summary>
    public ICommand SaveCommand => _save;

    /// <summary>F5: the selected tool's Refresh.</summary>
    public ICommand RefreshCommand => _refresh;

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
        private set
        {
            if (_currentContent is INotifyPropertyChanged old)
                old.PropertyChanged -= OnContentPropertyChanged;
            if (SetProperty(ref _currentContent, value))
            {
                if (value is INotifyPropertyChanged now)
                    now.PropertyChanged += OnContentPropertyChanged;
                OnPropertyChanged(nameof(ToolStatus));
            }
        }
    }

    /// <summary>The selected tool's status text (shown at the right of the status bar), if it provides one.</summary>
    public string ToolStatus => (_currentContent as IStatusSource)?.StatusBarText ?? "";

    private void OnContentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IStatusSource.StatusBarText))
            OnPropertyChanged(nameof(ToolStatus));
    }

    private void OnToolChanged(ITool? tool)
    {
        ToolMenus.Clear();

        _save.SetTarget(tool?.SaveCommand);
        _refresh.SetTarget(tool?.RefreshCommand);

        if (tool is null) { CurrentContent = null; return; }

        // The tool's own commands go in a menu bar at the top of its content, one menu per group
        foreach (var g in tool.FileGroups.Concat(tool.RibbonGroups))
            ToolMenus.Add(g);

        if (!_viewModels.TryGetValue(tool, out var vm))
            _viewModels[tool] = vm = tool.CreateViewModel(State);
        (vm as SystemViewModel)?.Reload();      // fresh working copies of the options each time System is opened
        CurrentContent = vm;
    }
}
