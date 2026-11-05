namespace BoosterAI;

public enum AgentMode { Standard, AutoApprove, Yolo, Custom }

public sealed record AgentModeChoice(AgentMode Mode, string Arguments, string ExplanationKey)
{
    public string Label => Loc.Get("agents.mode." + Mode);
    public string Explanation => Loc.Get(ExplanationKey);
}

/// <summary>Reviewed CLI permission presets and conservative edits of BoosterAI's existing alias block.</summary>
public static class AgentModes
{
    public static IReadOnlyList<AgentModeChoice> Choices(CliStatus status)
    {
        List<AgentModeChoice> choices = [new(AgentMode.Standard, "", "agents.mode.standard.detail")];
        bool Has(string flag) => status.Help.Contains(flag, StringComparison.Ordinal);
        void Add(AgentMode mode, string arguments, string key) => choices.Add(new(mode, arguments, key));
        switch (status.Cli)
        {
            case "claude":
                if (Has("--permission-mode") && Has("acceptEdits")) Add(AgentMode.AutoApprove, "--permission-mode acceptEdits", "agents.mode.claude.auto");
                if (Has("--dangerously-skip-permissions")) Add(AgentMode.Yolo, "--dangerously-skip-permissions", "agents.mode.claude.yolo");
                break;
            case "codex":
                if (Has("--approve-for-me")) Add(AgentMode.AutoApprove, "--approve-for-me", "agents.mode.codex.auto");
                if (Has("--dangerously-bypass-approvals-and-sandbox")) Add(AgentMode.Yolo, "--dangerously-bypass-approvals-and-sandbox", "agents.mode.codex.yolo");
                break;
            case "agy":
                if (Has("--mode") && Has("auto")) Add(AgentMode.AutoApprove, "--mode=auto", "agents.mode.agy.auto");
                if (Has("--dangerously-skip-permissions")) Add(AgentMode.Yolo, "--dangerously-skip-permissions", "agents.mode.agy.yolo");
                break;
            case "aider":
                if (Has("--yes-always")) Add(AgentMode.Yolo, "--yes-always", "agents.mode.aider.yolo");
                break;
            case "openhands":
                if (Has("--llm-approve")) Add(AgentMode.AutoApprove, "--llm-approve", "agents.mode.openhands.auto");
                if (Has("--always-approve")) Add(AgentMode.Yolo, "--always-approve", "agents.mode.openhands.yolo");
                break;
            case "interpreter":
                if (Has("--oss"))
                {
                    if (Has("--ask-for-approval") && Has("--sandbox"))
                        Add(AgentMode.AutoApprove, "--ask-for-approval never --sandbox workspace-write", "agents.mode.interpreter.auto");
                    if (Has("--dangerously-bypass-approvals-and-sandbox"))
                        Add(AgentMode.Yolo, "--dangerously-bypass-approvals-and-sandbox", "agents.mode.codex.yolo");
                }
                else if (Has("--local") && Has("--auto_run"))
                    Add(AgentMode.Yolo, "--auto_run", "agents.mode.interpreter.legacyYolo");
                break;
        }
        return choices;
    }

    public static string AliasLine(string cli, AgentModeChoice choice) =>
        choice.Mode is AgentMode.Standard or AgentMode.Custom ? "" : $"alias {cli}='{cli} {choice.Arguments}'";

    // Include previously written presets even when the current help probe failed or a CLI was removed.
    internal static IEnumerable<AgentModeChoice> AllKnownChoices(string cli) => Choices(new CliStatus(cli, "file", "/known")
    { Help = "acceptEdits --permission-mode --approve-for-me --dangerously-skip-permissions --dangerously-bypass-approvals-and-sandbox --mode auto --yes-always --llm-approve --always-approve --oss --ask-for-approval --sandbox" })
        .Concat(cli == "interpreter" ? [new AgentModeChoice(AgentMode.Yolo, "--auto_run", "agents.mode.interpreter.legacyYolo")] : []);

    public static AgentMode Current(CliStatus status)
    {
        if (status.Kind == "function") return AgentMode.Custom;
        if (!status.HasAlias) return status.ManagedAlias is null ? AgentMode.Standard : AgentMode.Custom;
        if (status.AliasDefinition != status.ManagedAlias) return AgentMode.Custom;
        return AllKnownChoices(status.Cli).FirstOrDefault(c => AliasLine(status.Cli, c) == status.ManagedAlias)?.Mode ?? AgentMode.Custom;
    }

    /// <summary>Only recognized aliases inside one well-formed block are editable; preserve every other byte.</summary>
    public static string Change(string? bashrc, CliStatus status, AgentModeChoice choice)
    {
        var text = bashrc ?? "";
        if (!Prerequisites.Clis.Contains(status.Cli) || choice.Mode == AgentMode.Custom
            || (choice.Mode != AgentMode.Standard && !Choices(status).Contains(choice)))
            throw new InvalidOperationException(Loc.Get("agents.mode.unsupported"));
        var starts = System.Text.RegularExpressions.Regex.Matches(text,
            "^" + System.Text.RegularExpressions.Regex.Escape(Prerequisites.BlockStart) + "\\r?$", System.Text.RegularExpressions.RegexOptions.Multiline);
        var ends = System.Text.RegularExpressions.Regex.Matches(text,
            "^" + System.Text.RegularExpressions.Regex.Escape(Prerequisites.BlockEnd) + "\\r?$", System.Text.RegularExpressions.RegexOptions.Multiline);
        // A marker inside an echo, quoted string or another comment is not an owned block.
        if (starts.Count > 1 || starts.Count != ends.Count
            || text.Split(Prerequisites.BlockStart).Length - 1 != starts.Count
            || text.Split(Prerequisites.BlockEnd).Length - 1 != ends.Count)
            throw new InvalidOperationException(Loc.Get("agents.mode.custom.detail"));
        var start = starts.Count == 0 ? -1 : starts[0].Index;
        var end = ends.Count == 0 ? -1 : ends[0].Index;
        if (start >= 0 && end < start) throw new InvalidOperationException(Loc.Get("agents.mode.custom.detail"));
        var bodyEnd = end < 0 ? -1 : end + Prerequisites.BlockEnd.Length;
        var outside = start < 0 ? text : text[..start] + text[bodyEnd..];
        bool Defines(string source) => Prerequisites.Defined(source).Contains(status.Cli)
            || System.Text.RegularExpressions.Regex.IsMatch(source,
                @"^\s*alias\s+.*\s" + System.Text.RegularExpressions.Regex.Escape(status.Cli) + "=",
                System.Text.RegularExpressions.RegexOptions.Multiline);
        if (Defines(outside) || Current(status) == AgentMode.Custom)
            throw new InvalidOperationException(Loc.Get("agents.mode.custom.detail"));
        var line = AliasLine(status.Cli, choice);
        if (start < 0)
        {
            if (line == "") return text;
            return text + (text.Length == 0 || text.EndsWith('\n') ? "" : "\n")
                + Prerequisites.BlockStart + "\n# Managed by BoosterAI. Applies to new login shells.\n" + line + "\n" + Prerequisites.BlockEnd + "\n";
        }
        var block = text[start..end];
        var known = AllKnownChoices(status.Cli).Select(c => AliasLine(status.Cli, c)).Where(l => l != "").ToHashSet();
        // Split with newline retained: unrelated definitions and the user's line endings stay untouched.
        var lines = System.Text.RegularExpressions.Regex.Matches(block, @"[^\n]*\n|[^\n]+$").Select(m => m.Value).ToList();
        foreach (var candidate in lines)
            if (Defines(candidate) && !known.Contains(candidate.TrimEnd('\r', '\n')))
                throw new InvalidOperationException(Loc.Get("agents.mode.custom.detail"));
        var updated = string.Concat(lines.Where(l => !known.Contains(l.TrimEnd('\r', '\n'))));
        if (line != "") updated += line + (block.Contains("\r\n") ? "\r\n" : "\n");
        return text[..start] + updated + text[end..];
    }
}
