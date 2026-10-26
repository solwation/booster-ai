using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BoosterAI;

/// <summary>
/// agy's allow-rules for commands. In print mode (Run once) agy cannot ask, so a command
/// without a rule is denied and the turn ends; tab-lib.sh logs the denied command line and
/// BoosterAI offers to add <c>command(&lt;program&gt;)</c> to agy's settings.json.
/// </summary>
public static partial class AgyPermissions
{
    /// <summary>agy's user settings, relative to the home directory of the Linux the tabs run in.</summary>
    public const string SettingsPath = "~/.gemini/antigravity-cli/settings.json";

    [GeneratedRegex(@"&&|\|\||[|;&\n]")]
    private static partial Regex Separator();

    [GeneratedRegex(@"^[A-Za-z0-9._+-]+$")]
    private static partial Regex ProgramName();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*=")]
    private static partial Regex Assignment();

    private static readonly HashSet<string> Keywords =
        ["if", "then", "else", "elif", "fi", "do", "done", "for", "while", "until", "case", "esac", "!", "{", "}", "(", ")"];

    /// <summary>
    /// The programs a command line runs: the first word of each segment between &amp;&amp;, ||, |, ;
    /// and &amp;, after VAR=value prefixes, as a bare name (/usr/bin/git → git). Shell keywords and
    /// words that cannot be a plain program name are left out.
    /// </summary>
    public static IReadOnlyList<string> Programs(string commandLine)
    {
        var found = new List<string>();
        foreach (var segment in Separator().Split(commandLine))
        {
            var words = segment.Trim().TrimStart('(', '{').Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                // VAR=$(cat f): the substitution's program is what runs.
                .Select(w => Assignment().IsMatch(w) && w.Contains("$(") ? w[(w.IndexOf("$(") + 2)..] : w)
                .SkipWhile(w => w == "" || Assignment().IsMatch(w) || Keywords.Contains(w));
            var first = words.FirstOrDefault()?.Trim('"', '\'');
            if (first is null) continue;
            var name = first[(first.LastIndexOf('/') + 1)..];
            if (ProgramName().IsMatch(name) && !Keywords.Contains(name) && !found.Contains(name)) found.Add(name);
        }
        return found;
    }

    /// <summary>The programs without a rule that allows them in full (<c>command(p)</c> or <c>command(*)</c>).</summary>
    public static IReadOnlyList<string> Missing(string? settingsJson, IEnumerable<string> programs)
    {
        var allowed = AllowRules(settingsJson);
        if (allowed.Contains("command(*)")) return [];
        return programs.Distinct().Where(p => !allowed.Contains(Rule(p))).ToList();
    }

    /// <summary>
    /// The settings with a rule for each program added; other keys and existing rules are kept.
    /// Throws JsonException for a file that is not a JSON object, so it is never replaced.
    /// </summary>
    public static string AddRules(string? settingsJson, IEnumerable<string> programs)
    {
        var root = string.IsNullOrWhiteSpace(settingsJson) ? new JsonObject()
            : JsonNode.Parse(settingsJson, documentOptions: Options) as JsonObject ?? throw new JsonException("not a JSON object");
        if (root["permissions"] is not JsonObject permissions) root["permissions"] = permissions = new JsonObject();
        if (permissions["allow"] is not JsonArray allow) permissions["allow"] = allow = new JsonArray();
        var existing = allow.Select(n => n?.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : null).ToHashSet();
        foreach (var p in programs.Distinct())
            if (ProgramName().IsMatch(p) && existing.Add(Rule(p))) allow.Add(Rule(p));
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static string Rule(string program) => $"command({program})";

    private static HashSet<string> AllowRules(string? settingsJson) =>
        (Parse(settingsJson)?["permissions"]?["allow"] as JsonArray ?? [])
            .Where(n => n?.GetValueKind() == JsonValueKind.String).Select(n => n!.GetValue<string>().Trim()).ToHashSet();

    /// <summary>The settings object, or null for a missing, empty or unreadable file.</summary>
    private static JsonObject? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json, documentOptions: Options) as JsonObject; }
        catch (JsonException) { return null; }
    }
}
