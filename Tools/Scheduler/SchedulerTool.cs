using System;
using System.Windows.Input;
using CityHelper.Services;
using CityHelper.Tools.Scheduler.ViewModels;
using CityHelper.ViewModels;

namespace CityHelper.Tools.Scheduler;

/// <summary>Schedules CityHelper's command line functions (backup, log merge, Config Sync...) in the Windows Task Scheduler.</summary>
public sealed class SchedulerTool : ITool
{
    private readonly Lazy<SchedulerViewModel> _vm;

    public SchedulerTool(AppState state) => _vm = new(() => new SchedulerViewModel(state));

    public string Name => "Scheduler";

    public ViewModelBase CreateViewModel(AppState state) => _vm.Value;

    public ICommand? RefreshCommand => _vm.Value.RefreshCommand;
}
