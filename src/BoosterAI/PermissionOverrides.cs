using System.Text.RegularExpressions;

namespace BoosterAI;

/// <summary>Explicitly selected presets override one shell name without editing its original source.</summary>
public static class PermissionOverrides
{
    public static string Start(string cli) => $"# >>> BoosterAI permissions for {cli} >>>";
    public static string End(string cli) => $"# <<< BoosterAI permissions for {cli} <<<";
    private static string ClearAlias(string cli) => $"unalias {cli} 2>/dev/null || true";
    private static string ClearFunction(string cli) => $"unset -f {cli} 2>/dev/null || true";

    private static bool Locate(string text, string cli, out int start, out int end)
    {
        var starts = Regex.Matches(text, "^" + Regex.Escape(Start(cli)) + "\\r?$", RegexOptions.Multiline);
        var ends = Regex.Matches(text, "^" + Regex.Escape(End(cli)) + "\\r?$", RegexOptions.Multiline);
        start = starts.Count == 1 ? starts[0].Index : -1;
        end = ends.Count == 1 ? ends[0].Index + ends[0].Length : -1;
        return starts.Count == 1 && ends.Count == 1 && end > start
            && text.Split(Start(cli)).Length == 2 && text.Split(End(cli)).Length == 2;
    }

    public static bool TryRead(string text, string cli, out AgentModeChoice? choice)
    {
        choice = null;
        if (!Prerequisites.Clis.Contains(cli) || !Locate(text, cli, out var start, out var end)) return false;
        var lines = text[start..end].Split('\n').Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray();
        if (lines.Length is < 2 or > 3 || lines[0] != ClearAlias(cli) || lines[1] != ClearFunction(cli)) return false;
        choice = AgentModes.AllKnownChoices(cli).FirstOrDefault(c =>
            AgentModes.AliasLine(cli, c) == (lines.Length == 3 ? lines[2] : ""));
        return choice is not null;
    }

    public static string Change(string text, string cli, AgentModeChoice choice)
    {
        if (!Prerequisites.Clis.Contains(cli) || choice.Mode == AgentMode.Custom || !AgentModes.AllKnownChoices(cli).Contains(choice))
            throw new InvalidOperationException(Loc.Get("agents.mode.unsupported"));
        if (text.Contains(Start(cli)) || text.Contains(End(cli)))
        {
            if (!TryRead(text, cli, out _) || !Locate(text, cli, out var start, out var end))
                throw new InvalidOperationException(Loc.Get("agents.alias.override.invalid"));
            if (end < text.Length && text[end] == '\n') end++;
            text = text[..start] + text[end..];
        }
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var alias = AgentModes.AliasLine(cli, choice);
        return text + (text.Length == 0 || text.EndsWith('\n') ? "" : newline)
            + string.Join(newline, new[] { Start(cli), ClearAlias(cli), ClearFunction(cli) }
                .Concat(alias.Length == 0 ? [] : new[] { alias }).Append(End(cli))) + newline;
    }
}
