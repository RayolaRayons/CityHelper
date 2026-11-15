using System;
using System.Collections.Generic;
using System.Linq;
using CityHelper.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CityHelper.Tools.PopMenuHero.ViewModels;

public sealed partial class PreviewLine : ObservableObject
{
    public PreviewLine(int number, string text)
    {
        Number = number;
        Text = text;
    }

    /// <summary>1-based line number.</summary>
    public int Number { get; }
    public string Text { get; }

    [ObservableProperty]
    private bool _isHighlighted;
}

/// <summary>Full .mnu text for the preview window, with the selected node's lines highlighted.</summary>
public sealed partial class MnuPreviewViewModel : ViewModelBase
{
    private int _highlightStart = -1;
    private int _highlightEnd = -1;

    /// <summary>True while the preview window is open (the editor skips the work otherwise).</summary>
    public bool IsOpen { get; set; }

    [ObservableProperty]
    private IReadOnlyList<PreviewLine> _lines = Array.Empty<PreviewLine>();

    /// <summary>Raised with the (start, end) line indexes the window should scroll into view.</summary>
    public event Action<int, int>? RevealRequested;

    public void SetText(string text)
    {
        var rows = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (rows.Count > 1 && rows[^1].Length == 0)
            rows.RemoveAt(rows.Count - 1);

        // Tabs don't render with a stable width in a TextBlock
        Lines = rows.Select((r, i) => new PreviewLine(i + 1, r.Replace("\t", "    "))).ToArray();
        _highlightStart = _highlightEnd = -1;
    }

    public void Highlight((int Start, int End)? range)
    {
        for (int i = Math.Max(_highlightStart, 0); i <= _highlightEnd && i < Lines.Count; i++)
            Lines[i].IsHighlighted = false;
        _highlightStart = _highlightEnd = -1;

        if (range is not { } r || r.Start < 0 || r.Start >= Lines.Count)
            return;

        _highlightStart = r.Start;
        _highlightEnd = Math.Min(r.End, Lines.Count - 1);
        for (int i = _highlightStart; i <= _highlightEnd; i++)
            Lines[i].IsHighlighted = true;
        RevealRequested?.Invoke(_highlightStart, _highlightEnd);
    }
}
