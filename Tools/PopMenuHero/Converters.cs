using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CityHelper.Tools.PopMenuHero.ViewModels;

namespace CityHelper.Tools.PopMenuHero;

/// <summary>Colour for a node kind: translucent row backgrounds in the tree (they work on light and dark themes), dark ones for the property header.</summary>
public sealed class NodeKindBrushConverter : IValueConverter
{
    private static readonly Dictionary<NodeKind, IBrush> RowBrushes = new()
    {
        [NodeKind.Menu] = Brush("#384F9BE8"),
        [NodeKind.Option] = Brush("#384CC24C"),
        [NodeKind.LockedOption] = Brush("#38E85C5C"),
        [NodeKind.Dialog] = Brush("#38F0B030"),
        [NodeKind.Title] = Brush("#38E8E040"),
        [NodeKind.Text] = Brush("#383CD0A0"),
        [NodeKind.Divider] = Brush("#38909090"),
        [NodeKind.Comment] = Brush("#388080C0"),
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
