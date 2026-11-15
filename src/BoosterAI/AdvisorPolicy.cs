namespace BoosterAI;

/// <summary>
/// Cost rules for Claude's <c>--advisor</c> (#172): an advisor is only worth it as Opus or Fable beside a Sonnet
/// (or lower) worker; two expensive models side by side roughly doubles the token cost for no gain.
/// One place for the UI and <see cref="Launcher"/>, so a hand-edited config cannot bypass it.
/// </summary>
public static class AdvisorPolicy
{
    public enum Verdict { Off, Ok, NotAdvisorModel, ExpensiveWorker, UnknownWorker }

    /// <summary>The advisor choices on offer: Standard (Claude's own setting) and Opus/Fable only.</summary>
    public static readonly IReadOnlyList<string> Choices =
        [Catalog.StandardModel, "opus", "fable", "claude-opus-5-5", "claude-fable-5-2", "claude-fable-5-1"];

    public static bool IsAdvisorModel(string model) => Mentions(model, "opus", "fable");

    /// <summary>True for Sonnet/Haiku, false for Opus/Fable, null when the model cannot be told (Standard, custom ids).</summary>
    public static bool? IsCheapWorker(string? model)
    {
        if (string.IsNullOrWhiteSpace(model) || model == Catalog.StandardModel) return null;
        if (Mentions(model, "opus", "fable")) return false;
        return Mentions(model, "sonnet", "haiku") ? true : null;
    }

    public static Verdict Evaluate(string llm, string? worker, string? advisor)
    {
        if (llm != "claude" || string.IsNullOrWhiteSpace(advisor) || advisor == Catalog.StandardModel) return Verdict.Off;
        if (!Catalog.IsSafeModel(advisor) || !IsAdvisorModel(advisor)) return Verdict.NotAdvisorModel;
        return IsCheapWorker(worker) switch { true => Verdict.Ok, false => Verdict.ExpensiveWorker, null => Verdict.UnknownWorker };
    }

    private static bool Mentions(string s, params string[] words) =>
        words.Any(w => s.Contains(w, StringComparison.OrdinalIgnoreCase));
}
