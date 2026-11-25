using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CityHelper.Tools.LogViewer.ViewModels;

namespace CityHelper.Tools.LogViewer.Views;

public partial class LogViewerView : UserControl
{
    public LogViewerView()
    {
        InitializeComponent();

        // The rows can't take focus, so give it to the list when it is clicked
        LogList.AddHandler(PointerPressedEvent, (_, _) => LogList.Focus(), RoutingStrategies.Tunnel, handledEventsToo: true);

        // Ctrl+C / Ctrl+A anywhere in this view, except while typing in a text box (which keeps its own copy and select all)
        AddHandler(KeyDownEvent, OnLogKeyDown, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
                _vm.ResultRequested -= OnResultRequested;
            _vm = DataContext as LogViewerViewModel;
            if (_vm != null)
            {
                _vm.ResultRequested += OnResultRequested;
                _vm.Confirm = ConfirmAsync;
            }
        };
    }

    private LogViewerViewModel? _vm;

    private async Task<bool> ConfirmAsync(string message) =>
        TopLevel.GetTopLevel(this) is Window owner && await new CityHelper.Views.ConfirmBox("Cleanup Logfile", message).ShowDialog<bool>(owner);

    /// <summary>Search: select the result's line and bring it into view.</summary>
    private void OnResultRequested(LogLine line)
    {
        LogList.SelectedItems?.Clear();
        LogList.SelectedItem = line;
        LogList.ScrollIntoView(line);
    }

    private async void OnLogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control)
            return;
        if ((e.Source as Control)?.FindAncestorOfType<TextBox>(includeSelf: true) != null)
            return;
        if (e.Key == Key.C)
        {
            e.Handled = true;
            await CopySelectionAsync();
        }
        else if (e.Key == Key.A)
        {
            e.Handled = true;
            LogList.SelectAll();
        }
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e) => await CopySelectionAsync();

    /// <summary>Copies the selected lines as displayed (only the parts that are switched on, no color codes unless shown raw).</summary>
    private async Task CopySelectionAsync()
    {
        if (DataContext is not LogViewerViewModel vm)
            return;

        var selected = LogList.SelectedItems?.OfType<LogLine>().ToList() ?? new();
        if (selected.Count == 0)
        {
            vm.StatusText = "Select the lines to copy first (click, Shift+click, Ctrl+click, or Ctrl+A for all).";
            return;
        }

        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;
        await clipboard.SetTextAsync(LogLineFormatter.Format(selected, vm.Display));
        vm.StatusText = selected.Count == 1 ? "Copied 1 line." : $"Copied {selected.Count:N0} lines.";
    }
}
