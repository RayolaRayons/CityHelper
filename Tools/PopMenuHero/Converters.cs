using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CityHelper.Tools.PopMenuHero.ViewModels;

namespace CityHelper.Tools.PopMenuHero;

/// <summary>Colour for a node kind: pale row backgrounds in the tree, dark ones for the property header.</summary>
public sealed class NodeKindBrushConverter : IValueConverter
{
    private static readonly Dictionary<NodeKind, IBrush> RowBrushes = new()
    {
        [NodeKind.Menu] = Brush("#dceeff"),
        [NodeKind.Option] = Brush("#dcffdc"),
        [NodeKind.LockedOption] = Brush("#ffdcdc"),
        [NodeKind.Dialog] = Brush("#fff0cc"),
        [NodeKind.Title] = Brush("#ffffd0"),
        [NodeKind.Text] = Brush("#dcfff0"),
        [NodeKind.Divider] = Brush("#ebebeb"),
        [NodeKind.Comment] = Brush("#e8e8f0"),
    };

    private static readonly Dictionary<NodeKind, IBrush> HeaderBrushes = new()
    {
        [NodeKind.Menu] = Brush("#1e3a5f"),
        [NodeKind.Option] = Brush("#1a3a1a"),
        [NodeKind.LockedOption] = Brush("#3a1a1a"),
        [NodeKind.Dialog] = Brush("#3a2a00"),
        [NodeKind.Title] = Brush("#2a2a00"),
        [NodeKind.Text] = Brush("#1a3a2a"),
        [NodeKind.Divider] = Brush("#2a2a2a"),
        [NodeKind.Comment] = Brush("#1a1a2e"),
    };

    public static NodeKindBrushConverter Row { get; } = new(RowBrushes);
    public static NodeKindBrushConverter Header { get; } = new(HeaderBrushes);

    private readonly Dictionary<NodeKind, IBrush> _brushes;

    private NodeKindBrushConverter(Dictionary<NodeKind, IBrush> brushes)
    {
        _brushes = brushes;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is NodeKind kind && _brushes.TryGetValue(kind, out var brush) ? brush : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static IBrush Brush(string hex) => new ImmutableSolidColorBrush(Color.Parse(hex));
}

/// <summary>TreeViewItem.Level to a left indent.</summary>
public sealed class IndentConverter : IValueConverter
{
    public static IndentConverter Instance { get; } = new();

    public double Indent { get; set; } = 20;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int level ? new Thickness(level * Indent, 0, 0, 0) : new Thickness();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
