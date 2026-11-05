namespace BoosterAI;

/// <summary>Open Interpreter's current standalone CLI and safe basic starts for the older Python CLI.</summary>
internal static class InterpreterCli
{
    // Both generations use the same executable name. Probe the executable without user aliases,
    // with a deadline; never turn a failed current invocation into a second, legacy invocation.
    private const string CurrentCli = "(set -o pipefail\ncommand timeout --kill-after=1s 5s interpreter --help 2>/dev/null | command grep -- '--oss' >/dev/null)";
    private const string UpgradeMessage = "BoosterAI: denna funktion kräver aktuella Open Interpreter. Installera via https://www.openinterpreter.com/install eller starta utan orkester och fortsättning.";

    public static string Command(TabConfig tab, string? prompt)
    {
        var once = tab.NonInteractive && tab.Orkester && prompt is null;
        var verb = once ? " exec" : "";
        var firstPrompt = prompt ?? (tab.Orkester ? "/orkester" + (once ? " " + Launcher.ForegroundWatchNote : "") : null);
        var compact = AutoCompact.Arguments("interpreter", tab.CompactTokens);
        var command = "interpreter" + verb + (compact.Length > 0 ? " " + compact : "") + Model(tab.Model, current: true)
            + (firstPrompt is null ? "" : " " + Launcher.Quote(firstPrompt));
        if (firstPrompt is not null) return RequireCurrent(command);
        if (IsLocal(tab.Model) || compact.Length > 0)
            return ChooseGeneration(command,
                (compact.Length > 0 ? "printf '%s\\n' 'BoosterAI: auto-compact requires the current Open Interpreter CLI. Using legacy defaults.' >&2 && " : "")
                + "interpreter" + Model(tab.Model, current: false));
        // --model and a plain interactive start work in both generations without a probe.
        return command;
    }

    private static bool IsLocal(string model) => model == "local" || model.StartsWith("ollama/", StringComparison.Ordinal);

    private static string Model(string model, bool current)
    {
        if (model == Catalog.StandardModel) return "";
        if (model == "local") return current ? " --oss" : " --local";
        if (current && model.StartsWith("ollama/", StringComparison.Ordinal))
            return " --oss --local-provider ollama --model " + Launcher.Quote(model[7..]);
        return " --model " + Launcher.Quote(model);
    }

    private static string RequireCurrent(string command) => ChooseGeneration(command,
        "printf '%s\\n' " + Launcher.Quote(UpgradeMessage) + " >&2 && false");

    // Newlines avoid Windows Terminal's semicolon command separator. The grouping is required
    // when the expression is joined to cd/exit hooks with && and ||.
    private static string ChooseGeneration(string current, string legacy) =>
        "{ if " + CurrentCli + "\nthen " + current + "\nelse " + legacy + "\nfi\n}";
}
