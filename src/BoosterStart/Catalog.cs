namespace BoosterStart;

/// <summary>The AI CLIs and their known models. Any other model id can be typed in the UI.</summary>
public static class Catalog
{
    public const string DefaultLlm = "claude";
    /// <summary>Start without --model, using the CLI's own default.</summary>
    public const string StandardModel = "standard";
    /// <summary>No AI CLI, just a shell in the directory.</summary>
    public const string ShellOnly = "skal";

    public static readonly IReadOnlyList<string> Llms = ["claude", "codex", "agy", ShellOnly];

    // Lists from `agy models` and the Codex model cache, 2026-10-26.
    private static readonly Dictionary<string, string[]> Models = new()
    {
        ["claude"] = [StandardModel, "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-fable-5-1"],
        ["codex"] = [StandardModel, "gpt-6.1-sol", "gpt-6-astra", "gpt-6-sol", "gpt-6-luna", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna"],
        ["agy"] =
        [
            StandardModel, "gemini-4-argon-high", "gemini-4-argon-medium", "gemini-4-argon-low",
            "gemini-3.8-flash-high", "gemini-3.8-flash-medium", "gemini-3.8-flash-low",
            "claude-opus-5-5-high", "claude-opus-5-5-medium", "claude-sonnet-5-5-high", "claude-sonnet-5-5-medium",
            "gpt-oss-120b-medium",
        ],
        [ShellOnly] = [StandardModel],
    };

    public static IReadOnlyList<string> ModelsFor(string llm) =>
        Models.TryGetValue(llm, out var list) ? list : [StandardModel];

    public static readonly IReadOnlyList<string> Palette =
        ["#1E66F5", "#2E9E44", "#E8A317", "#8839EF", "#D20F39", "#04A5E5", "#FE640B", "#EA76CB", "#7C7F93"];

    /// <summary>Model ids and directories end up in a shell command, so only plain characters pass.</summary>
    public static bool IsSafeModel(string s) =>
        s.Length is > 0 and < 100 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':' or '/');
}
