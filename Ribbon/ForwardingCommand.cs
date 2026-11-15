using System;
using System.Windows.Input;

namespace CityHelper.Ribbon;

/// <summary>
/// A command whose target can be swapped (the selected tool's Save / Refresh). It is disabled while there is no
/// target or the target cannot run, so a ribbon button or shortcut bound to it never looks usable when it isn't.
/// </summary>
public sealed class ForwardingCommand : ICommand
{
    private ICommand? _target;

    public event EventHandler? CanExecuteChanged;

    public void SetTarget(ICommand? target)
    {
        if (_target != null)
            _target.CanExecuteChanged -= OnTargetChanged;
        _target = target;
        if (_target != null)
            _target.CanExecuteChanged += OnTargetChanged;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTargetChanged(object? sender, EventArgs e) => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter) => _target?.CanExecute(parameter) ?? false;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
            _target!.Execute(parameter);
    }
}
