using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace BoosterAI;

/// <summary>A per-model launch preference. No CLI configuration file is modified.</summary>
public sealed record AutoCompactSetting
{
    public string Cli { get; set; } = "";
    public string Model { get; set; } = Catalog.StandardModel;
    public string Profile { get; set; } = "code";
    public string Mode { get; set; } = "default";
    public int ContextTokens { get; set; }
    public int CustomTokens { get; set; }
    public bool Active { get; set; } = true;
}

public sealed record CompactRecommendation(int Tokens, int ContextTokens, int ReserveTokens, int FileBudgetTokens);

public static class AutoCompact
{
    public const int MaxContext = 10_000_000;
    public static bool Supports(string cli) => cli is "claude" or "codex" or "aider" or "interpreter";
    public static bool ValidValue(string cli, int value) => Supports(cli) && value >= (cli == "claude" ? 100_000 : 1)
        && value <= (cli == "claude" ? 1_000_000 : MaxContext);

    /// <summary>Deliberate heuristic: reserve 10/25/40% for the next turn, at least 16k/32k/64k.
    /// Aider additionally budgets half of total context for code, maps and instructions.</summary>
    public static CompactRecommendation? Recommend(string cli, int context, string profile)
    {
        if (!Supports(cli) || context is < 1_000 or > MaxContext) return null;
        var (percent, minimum) = profile switch
        {
            "questions" => (10, 16_000), "code" => (25, 32_000), "orchestra" => (40, 64_000), _ => (0, 0),
        };
        if (percent == 0) return null;
        var reserve = Math.Max(minimum, context * percent / 100);
        var files = cli == "aider" ? context / 2 : 0;
        var tokens = (context - reserve - files) / 1000 * 1000;
        if (cli == "claude") tokens = Math.Min(tokens, 1_000_000);
        return ValidValue(cli, tokens) ? new(tokens, context, reserve, files) : null;
    }

    public static int? Resolve(AutoCompactSetting? setting)
    {
        if (setting is null || setting.ContextTokens is < 0 or > MaxContext || !setting.Active || setting.Model == Catalog.StandardModel || !Catalog.IsSafeModel(setting.Model)) return null;
        var value = setting.Mode switch
        {
            "recommended" => Recommend(setting.Cli, setting.ContextTokens, setting.Profile)?.Tokens,
            "custom" => setting.CustomTokens,
            _ => null,
        };
        return value is { } n && ValidValue(setting.Cli, n) && (setting.ContextTokens <= 0 || n <= setting.ContextTokens) ? n : null;
    }

    public static int? ForTab(IEnumerable<AutoCompactSetting> settings, string cli, string model) =>
        Resolve(settings.LastOrDefault(s => s.Active && s.Cli == cli && s.Model == model));

    public static string Arguments(string cli, int? tokens)
    {
        if (tokens is not { } n || !ValidValue(cli, n)) return "";
        var value = n.ToString(CultureInfo.InvariantCulture);
        return cli switch
        {
            "codex" => "-c model_auto_compact_token_limit=" + value + " -c 'model_auto_compact_token_limit_scope=\"total\"'",
            "aider" => "--max-chat-history-tokens " + value,
            "interpreter" => "-c model_auto_compact_token_limit=" + value,
            "claude" => "--autocompact " + value,
            _ => "",
        };
    }
    public static string GuardClaude(string selected, string fallback) =>
        "{ [ -z \"${CLAUDE_CODE_AUTO_COMPACT_WINDOW:-}\" ] || printf '%s\\n' 'BoosterAI: CLAUDE_CODE_AUTO_COMPACT_WINDOW overrides the selected auto-compact window.' >&2\n"
        + "if (set -o pipefail\ncommand timeout --kill-after=1s 5s claude --help 2>/dev/null | command grep -E -- '(^|[[:space:]])--autocompact([[:space:]=]|$)' >/dev/null)\nthen "
        + selected + "\nelse printf '%s\\n' 'BoosterAI: --autocompact support could not be verified. Using CLI defaults.' >&2\n"
        + fallback + "\nfi\n}";

}

/// <summary>Only public context metadata is read; never credentials, conversation contents or full cache copies.</summary>
public static class AutoCompactModels
{
    // Exact native-window model IDs documented at code.claude.com/docs/en/model-config (2026-11-05).
    public static int? ClaudeContext(string model) => model is "claude-opus-5-5" or "claude-sonnet-5-5"
        or "claude-haiku-5-5" or "claude-fable-5-1" ? 1_000_000 : null;

    public static IReadOnlyDictionary<string, int> ParseCodex(string json)
    {
        Dictionary<string, int> result = [];
        if (json.Length > 2_000_000) return result;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return result;
            foreach (var model in models.EnumerateArray().Take(500))
            {
                if (model.ValueKind != JsonValueKind.Object || !model.TryGetProperty("slug", out var slug) || slug.ValueKind != JsonValueKind.String
                    || slug.GetString() is not { } id || !Catalog.IsSafeModel(id)
                    || !model.TryGetProperty("context_window", out var window) || window.ValueKind != JsonValueKind.Number || !window.TryGetInt32(out var context)
                    || !model.TryGetProperty("effective_context_window_percent", out var percent) || percent.ValueKind != JsonValueKind.Number || !percent.TryGetInt32(out var effective)
                    || context is < 1_000 or > AutoCompact.MaxContext || effective is < 1 or > 100) continue;
                result[id] = (int)((long)context * effective / 100);
            }
        }
        catch (JsonException) { }
        return result;
    }

    // Run in the same login shell/WSL distro as tabs. Emit a bounded projection, never the personal cache itself.
    internal const string Probe = "python3 - <<'PY'\nimport json,os\ntry:\n p=os.path.join(os.environ.get('CODEX_HOME') or os.path.expanduser('~/.codex'),'models_cache.json')\n with open(p,'rb') as f: raw=f.read(2000001)\n if len(raw)>2000000: raise ValueError('size')\n data=json.loads(raw)\n models=data.get('models',[])\n print('BOOSTER_COMPACT_METADATA='+json.dumps({'models':[{k:m.get(k) for k in ('slug','context_window','effective_context_window_percent')} for m in models[:500] if isinstance(m,dict)]}))\nexcept Exception: print('BOOSTER_COMPACT_METADATA={}')\nPY";

    internal static IReadOnlyDictionary<string, int> ParseProbeOutput(string output)
    {
        var line = output.Split('\n').LastOrDefault(l => l.StartsWith("BOOSTER_COMPACT_METADATA=", StringComparison.Ordinal));
        return ParseCodex(line?["BOOSTER_COMPACT_METADATA=".Length..] ?? "{}");
    }

    public static async Task<IReadOnlyDictionary<string, int>> ReadCodexAsync(string distro)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "wsl.exe" : "bash")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        foreach (var arg in OperatingSystem.IsWindows() ? new[] { "-d", distro, "--exec", "bash", "-lic", Probe } : new[] { "-lic", Probe })
            process.StartInfo.ArgumentList.Add(arg);
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await process.WaitForExitAsync(timeout.Token);
            await errors;
            return ParseProbeOutput(await output);
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or OperationCanceledException) { return new Dictionary<string, int>(); }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); process.WaitForExit(); }
            catch (InvalidOperationException) { }
        }
    }
}
