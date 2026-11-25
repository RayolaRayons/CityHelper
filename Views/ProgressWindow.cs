using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CityHelper.Views;

/// <summary>Modal progress box with a Cancel button, for a long job that reports (done, total, message).</summary>
public sealed class ProgressWindow : Window
{
    private readonly TextBlock _message = new() { Text = "Starting...", TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _bar = new() { IsIndeterminate = true, MinWidth = 420 };
    private readonly CancellationTokenSource _cancel = new();
    private bool _finished;

    public ProgressWindow(string title)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 560;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cancel = new Button { Content = "Cancel", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) =>
        {
            cancel.IsEnabled = false;
            _message.Text = "Stopping...";
            _cancel.Cancel();
        };
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 12,
            Children = { _message, _bar, cancel },
        };
    }

    public CancellationToken Token => _cancel.Token;

    public void Report(string message, int done, int total)
    {
        _message.Text = message;
        _bar.IsIndeterminate = total <= 0;
        if (total > 0)
        {
            _bar.Maximum = total;
            _bar.Value = done;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_finished)
            _cancel.Cancel();       // closing the box stops the job
        base.OnClosing(e);
    }

    /// <summary>Shows the box over the owner, runs the job, closes the box and returns the job's result.</summary>
    public static async Task<T> RunAsync<T>(Window owner, string title, Func<IProgress<(string Message, int Done, int Total)>, CancellationToken, Task<T>> job)
    {
        var window = new ProgressWindow(title);
        var progress = new Progress<(string Message, int Done, int Total)>(p => window.Report(p.Message, p.Done, p.Total));
        var shown = window.ShowDialog(owner);
        try
        {
            return await job(progress, window.Token);
        }
        finally
        {
            window._finished = true;
            window.Close();
            await shown;
        }
    }
}
