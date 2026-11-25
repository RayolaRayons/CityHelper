using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CityHelper.Services;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CityHelper.Tools.Scheduler.ViewModels;

/// <summary>One line of the grid of scheduled tasks.</summary>
public sealed class SchedulerRow
{
    public SchedulerRow(ScheduledTask task) => Task = task;

    public ScheduledTask Task { get; }
    public string Name => Task.DisplayName;
    public string Commands => Task.Spec.Summary;
    public string Schedule => Task.Spec.When;
    public string NextRun => Task.NextRun;
    public string LastRun => Task.LastRun;
    public string Result => Task.LastResult;
}

/// <summary>A command line function that can be ticked for a schedule.</summary>
public sealed partial class CommandToggle : ObservableObject
{
    private readonly Action _changed;

    public CommandToggle(ScheduleCommand command, Action changed)
    {
        Switch = command.Switch;
        Label = command.Label;
        _changed = changed;
    }

    public string Switch { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => _changed();
}

public sealed partial class DayToggle : ObservableObject
{
    public DayToggle(DayOfWeek day, Action changed)
    {
        Day = day;
        Label = day.ToString()[..3];
        _changed = changed;
    }

    private readonly Action _changed;
    public DayOfWeek Day { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => _changed();
}

/// <summary>
/// The Scheduler: the Windows Task Scheduler tasks that run CityHelper's command line for the current installation
/// and account (top), and the command line parameters and start time of the selected one (bottom).
/// </summary>
public sealed partial class SchedulerViewModel : ViewModelBase
{
    private readonly AppState _state;
    private int _version;
    private string? _editingTaskName;           // the task being edited; null while writing a new schedule
    private string _defaultName = "";           // the name suggested for the schedule being written
    private bool _loadingEditor;

    public SchedulerViewModel(AppState state)
    {
        _state = state;
        Commands = ScheduleSpec.Commands.Select(c => new CommandToggle(c, UpdatePreview)).ToList();
        Days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }
            .Select(d => new DayToggle(d, UpdatePreview)).ToList();
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.SelectedInstallation) or nameof(AppState.SelectedAccount))
            {
                // A new schedule that still has its default name follows the installation and account
                if (_editingTaskName is null && ScheduleName == _defaultName)
                    ScheduleName = DefaultName();
                _ = RefreshAsync();
            }
        };
        NewSchedule();
        _ = RefreshAsync();
    }

    // ------------------------------------------------------------ the grid

    public ObservableCollection<SchedulerRow> Tasks { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteScheduleCommand), nameof(RunNowCommand))]
    private SchedulerRow? _selectedTask;

    [ObservableProperty] private string _headerText = "";
    [ObservableProperty] private string _statusText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveScheduleCommand), nameof(DeleteScheduleCommand), nameof(RunNowCommand), nameof(AddScheduleCommand))]
    private bool _isBusy;

    public bool IsSupported => SchedulerService.IsSupported;

    partial void OnSelectedTaskChanged(SchedulerRow? value)
    {
        if (value is not null)
            LoadEditor(value.Task);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        int version = ++_version;
        if (_state.SelectedInstallation is not { } inst || _state.SelectedAccount is not { } account)
        {
            Tasks.Clear();
            HeaderText = "";
            StatusText = "Select an installation and account.";
            return;
        }
        if (!SchedulerService.IsSupported)
        {
            StatusText = "Scheduling uses the Windows Task Scheduler, so it needs Windows.";
            return;
        }

        HeaderText = $"Scheduled tasks for {inst.Name}, account {account.Name}";
        StatusText = "Reading the Task Scheduler...";
        var keep = SelectedTask?.Task.TaskName;
        var tasks = await Task.Run(() => SchedulerService.List(inst.Name, account.Name));
        if (version != _version)
            return;

        Tasks.Clear();
        foreach (var t in tasks)
            Tasks.Add(new SchedulerRow(t));
        _loadingEditor = true;                       // re-selecting the same task must not overwrite what is being edited
        SelectedTask = Tasks.FirstOrDefault(t => t.Task.TaskName.Equals(keep, StringComparison.OrdinalIgnoreCase));
        _loadingEditor = false;
        StatusText = Tasks.Count == 0 ? "No schedules yet. Add Schedule creates one." : $"{Tasks.Count} schedule(s).";
    }

    [RelayCommand]
    private void OpenTaskScheduler()
    {
        try
        {
            SchedulerService.OpenTaskScheduler();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            StatusText = "Could not open the Task Scheduler: " + ex.Message;
        }
    }

    // ------------------------------------------------------------ the editor (bottom)

    public IReadOnlyList<CommandToggle> Commands { get; }
    public IReadOnlyList<DayToggle> Days { get; }
    public IReadOnlyList<string> Frequencies { get; } = new[] { "Daily", "Weekly" };

    [ObservableProperty] private string _scheduleName = "";
    [ObservableProperty] private bool _force;
    [ObservableProperty] private string _selectedFrequency = "Daily";
    [ObservableProperty] private string _startTimeText = "03:00";
    [ObservableProperty] private string _commandLinePreview = "";

    public bool IsWeekly => SelectedFrequency == "Weekly";

    partial void OnForceChanged(bool value) => UpdatePreview();
    partial void OnScheduleNameChanged(string value) => UpdatePreview();
    partial void OnStartTimeTextChanged(string value) => UpdatePreview();

    partial void OnSelectedFrequencyChanged(string value)
    {
        OnPropertyChanged(nameof(IsWeekly));
        UpdatePreview();
    }

    /// <summary>The command line the task will run, as it will be written.</summary>
    private void UpdatePreview()
    {
        if (_loadingEditor || _state.SelectedInstallation is not { } inst || _state.SelectedAccount is not { } account)
            return;
        var spec = BuildSpec(out _);
        CommandLinePreview = $"{System.IO.Path.GetFileName(Environment.ProcessPath ?? "CityHelper.exe")} {spec.Arguments(inst.Name, account.Name)}";
    }

    private ScheduleSpec BuildSpec(out string? problem)
    {
        problem = null;
        var spec = new ScheduleSpec
        {
            Name = ScheduleName.Trim(),
            Switches = Commands.Where(c => c.IsChecked).Select(c => c.Switch).ToList(),
            Force = Force,
            Weekly = IsWeekly,
            Days = Days.Where(d => d.IsChecked).Select(d => d.Day).ToList(),
        };
        if (TimeSpan.TryParseExact(StartTimeText.Trim(), new[] { @"h\:mm", @"hh\:mm" }, null, out var time) && time < TimeSpan.FromDays(1))
            spec.StartTime = time;
        else
            problem = "Enter the start time as hours and minutes, for example 03:00 or 21:30.";
        return spec;
    }

    private void LoadEditor(ScheduledTask task)
    {
        if (_loadingEditor)
            return;
        _loadingEditor = true;
        var spec = task.Spec;
        _editingTaskName = task.TaskName;
        ScheduleName = task.DisplayName;
        foreach (var c in Commands)
            c.IsChecked = spec.Switches.Contains(c.Switch, StringComparer.OrdinalIgnoreCase);
        Force = spec.Force;
        SelectedFrequency = spec.Weekly ? "Weekly" : "Daily";
        foreach (var d in Days)
            d.IsChecked = spec.Days.Contains(d.Day);
        StartTimeText = spec.StartTime.ToString(@"hh\:mm");
        _loadingEditor = false;
        UpdatePreview();
    }

    /// <summary>Add Schedule: a blank schedule in the editor, ready to be filled in and saved.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    private void AddSchedule()
    {
        SelectedTask = null;
        NewSchedule();
        StatusText = "Fill in the schedule below and press Save Schedule.";
    }

    private void NewSchedule()
    {
        _loadingEditor = true;
        _editingTaskName = null;
        ScheduleName = _defaultName = DefaultName();
        foreach (var c in Commands) c.IsChecked = false;
        Force = false;
        SelectedFrequency = "Daily";
        foreach (var d in Days) d.IsChecked = d.Day == DayOfWeek.Monday;
        StartTimeText = "03:00";
        _loadingEditor = false;
        UpdatePreview();
    }

    /// <summary>"City of Heroes - Installation - Account" (with a number added if a schedule already has that name).</summary>
    private string DefaultName()
    {
        var name = $"City of Heroes - {_state.SelectedInstallation?.Name} - {_state.SelectedAccount?.Name}";
        var candidate = name;
        for (int n = 2; Tasks.Any(t => t.Name.Replace(' ', '-').Equals(candidate.Replace(' ', '-'), StringComparison.OrdinalIgnoreCase)); n++)
            candidate = $"{name} ({n})";
        return candidate;
    }

    private bool NotBusy => !IsBusy;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task SaveScheduleAsync()
    {
        if (_state.SelectedInstallation is not { } inst || _state.SelectedAccount is not { } account)
        {
            StatusText = "Select an installation and account first.";
            return;
        }
        var spec = BuildSpec(out var problem);
        problem ??=
            spec.Name.Length == 0 ? "Give the schedule a name." :
            spec.Switches.Count == 0 ? "Tick at least one command to run." :
            spec.Weekly && spec.Days.Count == 0 ? "Tick at least one day of the week." : null;
        if (problem != null)
        {
            StatusText = problem;
            return;
        }

        IsBusy = true;
        StatusText = "Saving to the Task Scheduler...";
        var previous = _editingTaskName;
        var error = await Task.Run(() => SchedulerService.Save(inst.Name, account.Name, spec, previous));
        IsBusy = false;

        if (error != null)
        {
            StatusText = error;
            return;
        }
        _editingTaskName = SchedulerService.TaskNameFor(inst.Name, account.Name, spec.Name);
        await RefreshAsync();
        _loadingEditor = true;
        SelectedTask = Tasks.FirstOrDefault(t => t.Task.TaskName.Equals(_editingTaskName, StringComparison.OrdinalIgnoreCase));
        _loadingEditor = false;
        StatusText = $"Saved \"{spec.Name}\": {spec.Summary}, {spec.When}.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectionAndNotBusy))]
    private async Task DeleteScheduleAsync()
    {
        if (SelectedTask is not { } row)
            return;
        IsBusy = true;
        var error = await Task.Run(() => SchedulerService.Delete(row.Task.TaskName));
        IsBusy = false;
        if (error != null)
        {
            StatusText = error;
            return;
        }
        SelectedTask = null;
        NewSchedule();
        await RefreshAsync();
        StatusText = $"Deleted \"{row.Name}\".";
    }

    [RelayCommand(CanExecute = nameof(HasSelectionAndNotBusy))]
    private async Task RunNowAsync()
    {
        if (SelectedTask is not { } row)
            return;
        var error = await Task.Run(() => SchedulerService.RunNow(row.Task.TaskName));
        StatusText = error ?? $"Started \"{row.Name}\"; it runs without a window (see CityHelper-cli.log in the settings folder).";
    }

    private bool HasSelectionAndNotBusy => SelectedTask != null && !IsBusy;
}
