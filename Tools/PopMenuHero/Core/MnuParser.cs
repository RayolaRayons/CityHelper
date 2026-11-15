using System.Text.RegularExpressions;

namespace CityHelper.Tools.PopMenuHero.Core;

/// <summary>Parser for City of Heroes PopMenu (.mnu) files.</summary>
public sealed partial class MnuParser
{
    // Same line boundaries as Python's str.splitlines()
    [GeneratedRegex(@"\r\n|[\n\r\v\f\x1c\x1d\x1e\x85  ]")]
    private static partial Regex LineBreak();

    private string[] _lines = [];
    private int _pos;

    public MnuFile Parse(string text)
    {
        _lines = LineBreak().Split(text);
        _pos = 0;
        var result = new MnuFile();

        while (_pos < _lines.Length)
        {
            var line = _lines[_pos].Trim();
            var tokens = MnuTokenizer.Tokenize(line);

            if (tokens.Count == 0 || line.Length == 0)
            {
                _pos++;
                continue;
            }

            var kw = tokens[0].ToUpperInvariant();

            if (kw.StartsWith("//", StringComparison.Ordinal))
            {
                result.RootComments.Add(new CommentNode(line[2..].Trim()));
                _pos++;
            }
            else if (kw == "TITLE")
            {
                // A Title just before the root Menu gets absorbed into the menu as first child.
                var titleNode = new TitleNode(Arg(tokens, 1));
                _pos++;
                var rootMenu = TryParseRootMenu(titleNode);
                if (rootMenu != null)
                    result.RootMenus.Add(rootMenu);
                // else title is orphaned - just ignore
            }
            else if (kw == "MENU")
            {
                result.RootMenus.Add(ParseMenu(tokens));
            }
            else
            {
                // Unknown top-level token
                _pos++;
            }
        }

        return result;
    }

    /// <summary>After seeing a top-level Title, look for the following Menu.</summary>
    private MenuNode? TryParseRootMenu(TitleNode precedingTitle)
    {
        int saved = _pos;
        while (_pos < _lines.Length)
        {
            var line = _lines[_pos].Trim();
            if (line.Length == 0)
            {
                _pos++;
                continue;
            }
            var tokens = MnuTokenizer.Tokenize(line);
            if (tokens.Count > 0 && tokens[0].ToUpperInvariant() == "MENU")
            {
                var menu = ParseMenu(tokens);
                menu.Children.Insert(0, precedingTitle);
                return menu;
            }
            break;
        }
        _pos = saved;
        return null;
    }

    private MenuNode ParseMenu(List<string> headerTokens)
    {
        var menu = new MenuNode(Arg(headerTokens, 1));
        _pos++;

        // Expect opening brace (may be on same line or next)
        ConsumeOpenBrace();

        // Parse children until closing brace
        while (_pos < _lines.Length)
        {
            var line = _lines[_pos].Trim();
            if (line.Length == 0)
            {
                _pos++;
                continue;
            }
            var tokens = MnuTokenizer.Tokenize(line);
            if (tokens.Count == 0)
            {
                _pos++;
                continue;
            }

            var kw = tokens[0].ToUpperInvariant();

            if (kw == "}")
            {
                _pos++;
                break;
            }
            else if (kw.StartsWith("//", StringComparison.Ordinal))
            {
                menu.Children.Add(new CommentNode(line[2..].Trim()));
                _pos++;
            }
            else if (kw == "DIVIDER")
            {
                menu.Children.Add(new DividerNode());
                _pos++;
            }
            else if (kw == "TITLE")
            {
                menu.Children.Add(new TitleNode(Arg(tokens, 1)));
                _pos++;
            }
            else if (kw == "TEXT")
            {
                menu.Children.Add(new TextNode(Arg(tokens, 1)));
                _pos++;
            }
            else if (kw == "REQUIRES")
            {
                menu.Requires = RestOfLine(line, tokens);
                _pos++;
            }
            else if (kw == "VISIBLEREQUIRES")
            {
                menu.VisibleRequires = RestOfLine(line, tokens);
                _pos++;
            }
            else if (kw == "OPTION")
            {
                menu.Children.Add(new OptionNode(Arg(tokens, 1), Arg(tokens, 2)));
                _pos++;
            }
            else if (kw == "LOCKEDOPTION")
            {
                menu.Children.Add(ParseLockedOption());
            }
            else if (kw == "DIALOG")
            {
                menu.Children.Add(new DialogNode(Arg(tokens, 1), Arg(tokens, 2)));
                _pos++;
            }
            else if (kw == "MENU")
            {
                menu.Children.Add(ParseMenu(tokens));
            }
            else
            {
                // Unknown - skip
                _pos++;
            }
        }

        return menu;
    }

    /// <summary>Advance past an opening '{', which may be on the current or next line.</summary>
    private void ConsumeOpenBrace()
    {
        while (_pos < _lines.Length)
        {
            var line = _lines[_pos].Trim();
            if (line.Contains('{'))
            {
                _pos++;
                return;
            }
            if (line.Length > 0)
            {
                // Non-empty line without { - maybe it's something else, don't consume
                return;
            }
            _pos++;
        }
    }

    private LockedOptionNode ParseLockedOption()
    {
        var lo = new LockedOptionNode();
        _pos++;  // consume 'LockedOption'
        ConsumeOpenBrace();

        while (_pos < _lines.Length)
        {
            var line = _lines[_pos].Trim();
            if (line.Length == 0)
            {
                _pos++;
                continue;
            }
            var tokens = MnuTokenizer.Tokenize(line);
            if (tokens.Count == 0)
            {
                _pos++;
                continue;
            }

            var kw = tokens[0].ToUpperInvariant();
            _pos++;

            switch (kw)
            {
                case "}":
                    return lo;
                case "DISPLAYNAME":
                    lo.DisplayName = Arg(tokens, 1);
                    break;
                case "COMMAND":
                    lo.Command = Arg(tokens, 1);
                    break;
                case "ICON":
                    lo.Icon = Arg(tokens, 1);
                    break;
                case "BADGE":
                    // Unquoted, space-separated list
                    lo.Badge = string.Join(" ", tokens.Skip(1));
                    break;
                case "POWERREADY":
                    lo.PowerReady = Arg(tokens, 1, unquote: false);
                    break;
                case "POWEROWNED":
                    lo.PowerOwned = Arg(tokens, 1, unquote: false);
                    break;
                case "REQUIRES":
                    lo.Requires = RestOfLine(line, tokens);
                    break;
                case "VISIBLEREQUIRES":
                    lo.VisibleRequires = RestOfLine(line, tokens);
                    break;
                case "AUTHBIT":
                    lo.Authbit = Arg(tokens, 1, unquote: false);
                    break;
                case "REWARDTOKEN":
                    lo.RewardToken = Arg(tokens, 1, unquote: false);
                    break;
                case "STOREPRODUCT":
                    lo.StoreProduct = Arg(tokens, 1, unquote: false);
                    break;
            }
        }

        return lo;
    }

    private static string Arg(List<string> tokens, int index, bool unquote = true)
    {
        if (tokens.Count <= index)
            return "";
        return unquote ? MnuTokenizer.Unquote(tokens[index]) : tokens[index];
    }

    private static string RestOfLine(string line, List<string> tokens) =>
        line[tokens[0].Length..].Trim();
}
