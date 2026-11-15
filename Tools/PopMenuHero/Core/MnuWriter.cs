namespace CityHelper.Tools.PopMenuHero.Core;

/// <summary>Serializes an <see cref="MnuFile"/> back to .mnu text.</summary>
public sealed class MnuWriter
{
    /// <summary>Writes the whole file. Lines are separated by '\n'.</summary>
    public string Write(MnuFile mnuFile, Dictionary<MnuNode, (int Start, int End)>? ranges = null)
    {
        var lines = new List<string>();

        // Root-level comments
        foreach (var comment in mnuFile.RootComments)
        {
            ranges?.TryAdd(comment, (lines.Count, lines.Count));
            lines.Add($"// {comment.Text}");
        }

        foreach (var menu in mnuFile.RootMenus)
        {
            lines.Add("");
            lines.AddRange(WriteNode(menu, 0, ranges, lines.Count));
        }

        return string.Join("\n", lines) + "\n";
    }

    /// <summary>Writes a single node (and its children, for a Menu). Used by the MNU preview.</summary>
    public List<string> WriteNode(MnuNode node, int indent) => WriteNode(node, indent, null, 0);

    /// <summary>
    /// Writes a node; if <paramref name="ranges"/> is given, records the inclusive line range of the node and
    /// all its descendants, where <paramref name="offset"/> is the line index this node's first line will have.
    /// </summary>
    public List<string> WriteNode(MnuNode node, int indent, Dictionary<MnuNode, (int Start, int End)>? ranges, int offset)
    {
        var lines = WriteNodeCore(node, indent, ranges, offset);
        if (ranges != null && lines.Count > 0)
            ranges[node] = (offset, offset + lines.Count - 1);
        return lines;
    }

    private List<string> WriteNodeCore(MnuNode node, int indent, Dictionary<MnuNode, (int Start, int End)>? ranges, int offset)
    {
        var tab = new string('\t', indent);
        return node switch
        {
            CommentNode c => [$"{tab}// {c.Text}"],
            DividerNode => [$"{tab}DIVIDER"],
            TitleNode t => [$"{tab}Title {QuoteValue(t.Text)}"],
            TextNode t => [$"{tab}Text {QuoteValue(t.Text)}"],
            OptionNode o => [$"{tab}Option {QuoteValue(o.DisplayName)} {QuoteValue(o.Command)}"],
            LockedOptionNode lo => WriteLockedOption(lo, indent),
            DialogNode d => [$"{tab}Dialog {QuoteValue(d.DisplayName)} {QuoteValue(d.Command)}"],
            MenuNode m => WriteMenu(m, indent, ranges, offset),
            _ => [],
        };
    }

    private List<string> WriteMenu(MenuNode menu, int indent, Dictionary<MnuNode, (int Start, int End)>? ranges, int offset)
    {
        var tab = new string('\t', indent);
        var inner = new string('\t', indent + 1);
        var lines = new List<string>
        {
            $"{tab}Menu {QuoteValue(menu.Name)}",
            $"{tab}{{",
        };
        if (menu.Requires.Length > 0)
            lines.Add($"{inner}Requires {menu.Requires}");
        if (menu.VisibleRequires.Length > 0)
            lines.Add($"{inner}VisibleRequires {menu.VisibleRequires}");
        foreach (var child in menu.Children)
            lines.AddRange(WriteNode(child, indent + 1, ranges, offset + lines.Count));
        lines.Add($"{tab}}}");
        return lines;
    }

    private static List<string> WriteLockedOption(LockedOptionNode lo, int indent)
    {
        var tab = new string('\t', indent);
        var inner = new string('\t', indent + 1);
        var lines = new List<string>
        {
            $"{tab}LockedOption",
            $"{tab}{{",
            $"{inner}DisplayName {QuoteValue(lo.DisplayName)}",
            $"{inner}Command {QuoteValue(lo.Command)}",
        };
        if (lo.Icon.Length > 0)
            lines.Add($"{inner}Icon {QuoteValue(lo.Icon)}");
        if (lo.Badge.Length > 0)
            lines.Add($"{inner}Badge {lo.Badge}");
        if (lo.PowerReady.Length > 0)
            lines.Add($"{inner}PowerReady {lo.PowerReady}");
        if (lo.PowerOwned.Length > 0)
            lines.Add($"{inner}PowerOwned {lo.PowerOwned}");
        if (lo.Requires.Length > 0)
            lines.Add($"{inner}Requires {lo.Requires}");
        if (lo.VisibleRequires.Length > 0)
            lines.Add($"{inner}VisibleRequires {lo.VisibleRequires}");
        // Deprecated but preserved if present
        if (lo.Authbit.Length > 0)
            lines.Add($"{inner}Authbit {lo.Authbit}");
        if (lo.RewardToken.Length > 0)
            lines.Add($"{inner}RewardToken {lo.RewardToken}");
        if (lo.StoreProduct.Length > 0)
            lines.Add($"{inner}StoreProduct {lo.StoreProduct}");
        lines.Add($"{tab}}}");
        return lines;
    }

    /// <summary>Quote a value, using &lt;&amp; &amp;&gt; if it contains double-quotes.</summary>
    internal static string QuoteValue(string text) =>
        text.Contains('"') ? $"<& {text} &>" : $"\"{text}\"";
}
