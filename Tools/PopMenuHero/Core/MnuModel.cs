namespace CityHelper.Tools.PopMenuHero.Core;

/// <summary>Base class for all menu elements.</summary>
public abstract class MnuNode
{
    /// <summary>Deep copy, used for copy/paste.</summary>
    public virtual MnuNode Clone() => (MnuNode)MemberwiseClone();
}

public sealed class CommentNode(string text = "") : MnuNode
{
    public string Text { get; set; } = text;
}

public sealed class DividerNode : MnuNode;

public sealed class TitleNode(string text = "") : MnuNode
{
    public string Text { get; set; } = text;
}

public sealed class TextNode(string text = "") : MnuNode
{
    public string Text { get; set; } = text;
}

public sealed class OptionNode(string displayName = "", string command = "") : MnuNode
{
    public string DisplayName { get; set; } = displayName;
    public string Command { get; set; } = command;
}

public sealed class LockedOptionNode : MnuNode
{
    public string DisplayName { get; set; } = "";
    public string Command { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Badge { get; set; } = "";          // space-separated list
    public string PowerReady { get; set; } = "";
    public string PowerOwned { get; set; } = "";
    public string Requires { get; set; } = "";
    public string VisibleRequires { get; set; } = "";
    public string Authbit { get; set; } = "";        // deprecated
    public string RewardToken { get; set; } = "";    // deprecated
    public string StoreProduct { get; set; } = "";   // deprecated
}

public sealed class DialogNode(string displayName = "", string command = "") : MnuNode
{
    public string DisplayName { get; set; } = displayName;
    public string Command { get; set; } = command;
}

public sealed class MenuNode(string name = "") : MnuNode
{
    public string Name { get; set; } = name;
    public List<MnuNode> Children { get; set; } = [];
    public string Requires { get; set; } = "";
    public string VisibleRequires { get; set; } = "";

    public override MnuNode Clone()
    {
        var copy = (MenuNode)MemberwiseClone();
        copy.Children = Children.Select(c => c.Clone()).ToList();
        return copy;
    }
}

/// <summary>Represents a parsed .mnu file.</summary>
public sealed class MnuFile
{
    public List<CommentNode> RootComments { get; set; } = [];
    public List<MenuNode> RootMenus { get; set; } = [];
}
