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

    /// <summary>Tab colour themes; a tab keeps its index, so switching theme recolours every tab.</summary>
    public static readonly IReadOnlyList<ColorTheme> Themes =
    [
        new("Standard", ["#1E66F5", "#2E9E44", "#E8A317", "#8839EF", "#D20F39", "#04A5E5", "#FE640B", "#EA76CB"]),
        new("Pastell", ["#8AADF4", "#A6DA95", "#EED49F", "#C6A0F6", "#ED8796", "#91D7E3", "#F5A97F", "#F5BDE6"]),
        new("Nordisk", ["#5E81AC", "#A3BE8C", "#EBCB8B", "#B48EAD", "#BF616A", "#88C0D0", "#D08770", "#8FBCBB"]),
        new("Neon", ["#00E5FF", "#39FF14", "#FFEA00", "#D500F9", "#FF1744", "#2979FF", "#FF9100", "#FF4081"]),
        new("Solnedgång", ["#F94144", "#F3722C", "#F8961E", "#F9C74F", "#90BE6D", "#43AA8B", "#577590", "#277DA1"]),
    ];

    public static ColorTheme Theme(string name) => Themes.FirstOrDefault(t => t.Name == name) ?? Themes[0];

    /// <summary>Model ids and directories end up in a shell command, so only plain characters pass.</summary>
    public static bool IsSafeModel(string s) =>
        s.Length is > 0 and < 100 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':' or '/');
}

public sealed record ColorTheme(string Name, IReadOnlyList<string> Colors)
{
    public string ColorAt(int index) => Colors[((index % Colors.Count) + Colors.Count) % Colors.Count];
    /// <summary>Localised name; Name stays the stable id saved in the config.</summary>
    public string DisplayName => Loc.Display("theme", Name);
    public IReadOnlyList<Avalonia.Media.IBrush> Swatches { get; } =
        Colors.Select(c => (Avalonia.Media.IBrush)new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(c))).ToList();
}
