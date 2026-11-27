namespace BoosterAI;

/// <summary>The AI CLIs and their known models. Any other model id can be typed in the UI.</summary>
public static class Catalog
{
    public const string DefaultLlm = "claude";
    /// <summary>Start without --model, using the CLI's own default.</summary>
    public const string StandardModel = "standard";
    /// <summary>No AI CLI, just a shell in the directory.</summary>
    public const string ShellOnly = "skal";

    public static readonly IReadOnlyList<string> Llms = ["claude", "codex", "agy", "aider", "interpreter", "openhands", ShellOnly];

    // Built-in fallback lists (from `agy models` and the Codex model cache, 2026-11-27); ModelDiscovery adds what the CLIs report.
    private static readonly Dictionary<string, string[]> Models = new()
    {
        ["claude"] = [StandardModel, "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-fable-5-2", "claude-fable-5-1"],
        ["codex"] = [StandardModel, "gpt-6.2-sol", "gpt-6.2-astra", "gpt-6.1-sol", "gpt-6.1-terra", "gpt-6-astra", "gpt-6-sol", "gpt-6-luna", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna"],
        ["agy"] =
        [
            StandardModel,
            "gemini-4-neon-high", "gemini-4-neon-xhigh", "gemini-4-neon-medium", "gemini-4-neon-low",
            "gemini-4-carbon-medium", "gemini-4-carbon-high", "gemini-4-carbon-low",
            "gemini-4-argon-medium", "gemini-4-argon-high", "gemini-4-argon-low",
            "gemini-4-hydrogen-high", "gemini-4-hydrogen-xhigh", "gemini-4-hydrogen-medium", "gemini-4-hydrogen-low",
            "gemini-pro-orchestrator-neon", "gemini-pro-orchestrator", "gemini-pro-orchestrator-argon",
            "gemini-3.8-flash-high", "gemini-3.8-flash-medium", "gemini-3.8-flash-low",
            "claude-opus-5-5-low", "claude-opus-5-5-medium", "claude-opus-5-5-high",
            "claude-sonnet-5-5-low", "claude-sonnet-5-5-medium", "claude-sonnet-5-5-high",
            "gpt-oss-120b-medium",
        ],
        ["aider"] =
        [
            StandardModel, "claude-3-7-sonnet", "deepseek/deepseek-chat", "ollama/qwen2.5-coder", "ollama/llama3", "gpt-4o",
        ],
        ["openhands"] = [StandardModel],
        ["interpreter"] = [StandardModel, "local", "ollama/qwen2.5-coder:7b"],
        [ShellOnly] = [StandardModel],
    };

    private static readonly object DiscoveredLock = new();
    private static Dictionary<string, string[]> _discovered = [];
    private static readonly Dictionary<string, IReadOnlyList<string>> Merged = [];

    /// <summary>Models the agent CLIs reported (#265); replaces the earlier discovery. Ids that are not safe are dropped.</summary>
    public static void SetDiscovered(IReadOnlyDictionary<string, string[]> found)
    {
        lock (DiscoveredLock)
        {
            _discovered = found.ToDictionary(p => p.Key, p => p.Value.Where(IsSafeModel).ToArray());
            Merged.Clear();
        }
    }

    /// <summary>Standard first, then the discovered ids in the CLI's order, then built-in ones the CLI did not list; no duplicates.</summary>
    public static IReadOnlyList<string> ModelsFor(string llm)
    {
        lock (DiscoveredLock)
        {
            if (Merged.TryGetValue(llm, out var cached)) return cached;
            var builtIn = Models.TryGetValue(llm, out var list) ? list : [StandardModel];
            var found = _discovered.TryGetValue(llm, out var f) ? f : [];
            return Merged[llm] = [StandardModel, .. found.Concat(builtIn).Where(m => m != StandardModel).Distinct()];
        }
    }

    /// <summary>Tab colour themes; a tab keeps its index, so switching theme recolours every tab.</summary>
    public static readonly IReadOnlyList<ColorTheme> Themes =
    [
        new("Standard", ["#1E66F5", "#2E9E44", "#E8A317", "#8839EF", "#D20F39", "#04A5E5", "#FE640B", "#EA76CB"]),
        new("Pastell", ["#8AADF4", "#A6DA95", "#EED49F", "#C6A0F6", "#ED8796", "#91D7E3", "#F5A97F", "#F5BDE6"]),
        new("Nordisk", ["#5E81AC", "#A3BE8C", "#EBCB8B", "#B48EAD", "#BF616A", "#88C0D0", "#D08770", "#8FBCBB"]),
        new("Neon", ["#00E5FF", "#39FF14", "#FFEA00", "#D500F9", "#FF1744", "#2979FF", "#FF9100", "#FF4081"]),
        new("Solnedgång", ["#F94144", "#F3722C", "#F8961E", "#F9C74F", "#90BE6D", "#43AA8B", "#577590", "#277DA1"]),
    ];

    /// <summary>
    /// The colour index a new project gets: the one after the previous project's (#152), so neighbouring tabs differ.
    /// The first project starts at index 1, not the accent-like blue at 0, which sits beside the accent checkbox.
    /// </summary>
    public static int NextColorIndex(int? previous) => ((previous is >= 0 ? previous.Value : 0) + 1) % Themes[0].Colors.Count;

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
