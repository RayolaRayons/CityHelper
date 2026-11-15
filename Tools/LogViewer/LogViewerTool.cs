using System;
using System.Windows.Input;
using CityHelper.Services;
using CityHelper.Tools.LogViewer.ViewModels;
using CityHelper.ViewModels;

namespace CityHelper.Tools.LogViewer;

/// <summary>Reads a chat log with channel / character filters and optional color rendering.</summary>
public sealed class LogViewerTool : ITool
{
    private readonly Lazy<LogViewerViewModel> _vm;

    public LogViewerTool(AppState state)
    {
        _vm = new(() => new LogViewerViewModel(state));
    }

    public string Name => "Log Viewer";

    public ViewModelBase CreateViewModel(AppState state) => _vm.Value;

    public ICommand? RefreshCommand => _vm.Value.RefreshCommand;

    public OptionsSectionViewModel? CreateOptionsSection() =>
        new LogViewerOptionsViewModel(LogViewerSettings.Load())
        {
            Saved = () =>
            {
                if (_vm.IsValueCreated)
                    _vm.Value.ReloadOptions();
            },
        };
}
