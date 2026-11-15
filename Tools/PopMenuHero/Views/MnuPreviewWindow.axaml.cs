using System;
using Avalonia.Controls;
using Avalonia.Threading;
using CityHelper.Tools.PopMenuHero.ViewModels;

namespace CityHelper.Tools.PopMenuHero.Views;

public partial class MnuPreviewWindow : Window
{
    private MnuPreviewViewModel? _vm;

    public MnuPreviewWindow()
    {
        InitializeComponent();
    }

    public MnuPreviewWindow(MnuPreviewViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        vm.RevealRequested += OnRevealRequested;
        Closed += (_, _) =>
        {
            vm.RevealRequested -= OnRevealRequested;
            vm.IsOpen = false;
        };
    }

    private void OnRevealRequested(int start, int end)
    {
        // Wait for the list to lay out the (possibly new) lines before scrolling
        Dispatcher.UIThread.Post(() =>
        {
            if (_vm is null) return;
            var list = LineList;
            int count = _vm.Lines.Count;
            if (start < 0 || start >= count) return;
            list.ScrollIntoView(Math.Min(end, count - 1));
            list.ScrollIntoView(start);   // the start of the block wins if it is taller than the window
        }, DispatcherPriority.Background);
    }
}
