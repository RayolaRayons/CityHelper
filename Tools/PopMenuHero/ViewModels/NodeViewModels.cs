using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CityHelper.ViewModels;
using CityHelper.Tools.PopMenuHero.Core;

namespace CityHelper.Tools.PopMenuHero.ViewModels;

public enum NodeKind
{
    Menu,
    Option,
    LockedOption,
    Dialog,
    Title,
    Text,
    Divider,
    Comment,
}

/// <summary>The document a node belongs to; told about field edits so it can record undo and mark itself modified.</summary>
public interface IEditHost
{
    void BeforeEdit(NodeViewModel node);
    void AfterEdit(NodeViewModel node);
}

/// <summary>A tree row wrapping one <see cref="MnuNode"/>. Property setters write straight through to the model.</summary>
public abstract partial class NodeViewModel : ViewModelBase
{
    private static readonly ObservableCollection<NodeViewModel> NoChildren = [];

    protected NodeViewModel(IEditHost host)
    {
        Host = host;
    }

    public IEditHost Host { get; }

    /// <summary>Containing menu, or null at the root.</summary>
    public MenuNodeViewModel? Parent { get; internal set; }

    public abstract MnuNode Model { get; }
    public abstract NodeKind Kind { get; }
    public abstract string Label { get; }

    public virtual ObservableCollection<NodeViewModel> Children => NoChildren;

    public string TypeIcon => Kind switch
    {
        NodeKind.Menu => "📁",
        NodeKind.Option => "▶",
        NodeKind.LockedOption => "🔒",
        NodeKind.Dialog => "💬",
        NodeKind.Title => "📝",
        NodeKind.Text => "📄",
        NodeKind.Divider => "─",
        NodeKind.Comment => "💬",
        _ => "",
    };

    public virtual string Header => $"{TypeIcon}  {Kind}";

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>The model with its children list rebuilt from the tree.</summary>
    public virtual MnuNode ToModel() => Model;

    public IEnumerable<NodeViewModel> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var n in child.SelfAndDescendants())
                yield return n;
    }

    public bool IsDescendantOf(NodeViewModel ancestor)
    {
        for (var p = Parent; p != null; p = p.Parent)
            if (p == ancestor)
                return true;
        return false;
    }

    protected void Edit(string current, string? value, Action<string> apply, [CallerMemberName] string? propertyName = null)
    {
        value ??= "";
        if (current == value)
            return;
        Host.BeforeEdit(this);
        apply(value);
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(Label));
        Host.AfterEdit(this);
    }

    public static NodeViewModel Create(MnuNode node, IEditHost host) => node switch
    {
        MenuNode m => new MenuNodeViewModel(m, host),
        OptionNode o => new OptionNodeViewModel(o, host),
        LockedOptionNode lo => new LockedOptionNodeViewModel(lo, host),
        DialogNode d => new DialogNodeViewModel(d, host),
        TitleNode t => new TitleNodeViewModel(t, host),
        TextNode t => new TextNodeViewModel(t, host),
        DividerNode d => new DividerNodeViewModel(d, host),
        CommentNode c => new CommentNodeViewModel(c, host),
        _ => throw new ArgumentException($"Unknown node type {node.GetType().Name}", nameof(node)),
    };
}

public sealed class MenuNodeViewModel : NodeViewModel
{
    private readonly MenuNode _model;

    public MenuNodeViewModel(MenuNode model, IEditHost host) : base(host)
    {
        _model = model;
        Children = [];
        Children.CollectionChanged += OnChildrenChanged;
        foreach (var child in model.Children)
            Children.Add(Create(child, host));
    }

    public override MnuNode Model => _model;
    public override NodeKind Kind => NodeKind.Menu;
    public override string Label => $"📁 {Name}";
    public override ObservableCollection<NodeViewModel> Children { get; }

    public string Name
    {
        get => _model.Name;
        set => Edit(_model.Name, value, v => _model.Name = v);
    }

    public string Requires
    {
        get => _model.Requires;
        set => Edit(_model.Requires, value, v => _model.Requires = v);
    }

    public string VisibleRequires
    {
        get => _model.VisibleRequires;
        set => Edit(_model.VisibleRequires, value, v => _model.VisibleRequires = v);
    }

    public override MnuNode ToModel()
    {
        _model.Children = Children.Select(c => c.ToModel()).ToList();
        return _model;
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (NodeViewModel n in e.NewItems)
                n.Parent = this;
    }
}

public sealed class OptionNodeViewModel(OptionNode model, IEditHost host) : NodeViewModel(host)
{
    public override MnuNode Model => model;
    public override NodeKind Kind => NodeKind.Option;
    public override string Label => $"▶ {DisplayName}";

    public string DisplayName
    {
        get => model.DisplayName;
        set => Edit(model.DisplayName, value, v => model.DisplayName = v);
    }

    public string Command
    {
        get => model.Command;
        set => Edit(model.Command, value, v => model.Command = v);
    }
}

public sealed class LockedOptionNodeViewModel(LockedOptionNode model, IEditHost host) : NodeViewModel(host)
{
    public override MnuNode Model => model;
    public override NodeKind Kind => NodeKind.LockedOption;
    public override string Label => $"🔒 {(DisplayName.Length > 0 ? DisplayName : "(no name)")}";

    public string DisplayName
    {
        get => model.DisplayName;
        set => Edit(model.DisplayName, value, v => model.DisplayName = v);
    }

    public string Command
    {
        get => model.Command;
        set => Edit(model.Command, value, v => model.Command = v);
    }

    public string Icon
    {
        get => model.Icon;
        set => Edit(model.Icon, value, v => model.Icon = v);
    }

    public string Badge
    {
        get => model.Badge;
        set => Edit(model.Badge, value, v => model.Badge = v);
    }

    public string PowerReady
    {
        get => model.PowerReady;
        set => Edit(model.PowerReady, value, v => model.PowerReady = v);
    }

    public string PowerOwned
    {
        get => model.PowerOwned;
        set => Edit(model.PowerOwned, value, v => model.PowerOwned = v);
    }

    public string Requires
    {
        get => model.Requires;
        set => Edit(model.Requires, value, v => model.Requires = v);
    }

    public string VisibleRequires
    {
        get => model.VisibleRequires;
        set => Edit(model.VisibleRequires, value, v => model.VisibleRequires = v);
    }

    public string Authbit
    {
        get => model.Authbit;
        set => Edit(model.Authbit, value, v => model.Authbit = v);
    }

    public string RewardToken
    {
        get => model.RewardToken;
        set => Edit(model.RewardToken, value, v => model.RewardToken = v);
    }

    public string StoreProduct
    {
        get => model.StoreProduct;
        set => Edit(model.StoreProduct, value, v => model.StoreProduct = v);
    }
}

public sealed class DialogNodeViewModel(DialogNode model, IEditHost host) : NodeViewModel(host)
{
    public override MnuNode Model => model;
    public override NodeKind Kind => NodeKind.Dialog;
    public override string Label => $"💬 {(DisplayName.Length > 0 ? DisplayName : "(no name)")}";

    public string DisplayName
    {
        get => model.DisplayName;
        set => Edit(model.DisplayName, value, v => model.DisplayName = v);
    }

    public string Command
    {
        get => model.Command;
        set => Edit(model.Command, value, v => model.Command = v);
    }
}

public sealed class TitleNodeViewModel(TitleNode model, IEditHost host) : NodeViewModel(host)
{
    public override MnuNode Model => model;
    public override NodeKind Kind => NodeKind.Title;
    public override string Label => $"📝 {Text}";

    public string Text
    {
        get => model.Text;
        set => Edit(model.Text, value, v => model.Text = v);
    }
}

public sealed class TextNodeViewModel(TextNode model, IEditHost host) : NodeViewModel(host)
{
    public override MnuNode Model => model;
    public override NodeKind Kind => NodeKind.Text;
    public override string Label => $"📄 {Text}";

    public string Text
    {
        get => model.Text;
        set => Edit(model.Text, value, v => model.Text = v);
    }
}

public sealed class DividerNodeViewModel(DividerNode model, IEditHost host) : NodeViewModel(host)
{
    public override MnuNode Model => model;
    public override NodeKind Kind => NodeKind.Divider;
    public override string Label => "──────────────────────────";
    public override string Header => "── Divider ──   (No properties)";
}

public sealed class CommentNodeViewModel(CommentNode model, IEditHost host) : NodeViewModel(host)
{
    public override MnuNode Model => model;
    public override NodeKind Kind => NodeKind.Comment;
    public override string Label => $"💬 {(Text.Length > 100 ? Text[..100] + "…" : Text)}";

    public string Text
    {
        get => model.Text;
        set => Edit(model.Text, value, v => model.Text = v);
    }
}
