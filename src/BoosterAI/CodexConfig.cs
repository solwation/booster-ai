using System.Text.RegularExpressions;

namespace BoosterAI;

/// <summary>What Codex's config.toml says about sandbox and approvals, and whether BoosterAI holds a backup of it.</summary>
public sealed record CodexConfigState(string? SandboxMode, string? ApprovalPolicy, bool HasBackup)
{
    public bool IsYolo => SandboxMode == CodexConfig.YoloSandbox && ApprovalPolicy == CodexConfig.YoloApproval;
}

/// <summary>
/// Line-based edits of the two top-level permission keys in Codex's <c>config.toml</c> (#182). The YOLO alias only
/// applies in interactive shells; Codex started from a script, <c>exec</c> or tmux reads this file instead. Only
/// top-level keys (lines before the first table header, outside multi-line strings) are read or changed; keys inside
/// tables, comments and every other line stay byte-identical. Profiles are not followed; the CODEX_HOME of each BoosterAI
/// account (#186) is handled like the default one.
/// </summary>
public static partial class CodexConfig
{
    /// <summary>Codex's home, relative to the home directory of the Linux the tabs run in.</summary>
    public const string Home = "~/.codex";
    public const string FileName = "config.toml";
    /// <summary>The values before the first YOLO apply; restored and removed by Standard or Auto Approve.</summary>
    public const string BackupName = "config.toml.before-boosterai-yolo";
    public const string SandboxKey = "sandbox_mode";
    public const string ApprovalKey = "approval_policy";
    public const string YoloSandbox = "danger-full-access";
    public const string YoloApproval = "never";
    private static readonly string[] Keys = [SandboxKey, ApprovalKey];

    [GeneratedRegex(@"[^\n]*\n|[^\n]+$")]
    private static partial Regex Line();

    // [table], [[array of tables]] and [a."b c".d], with an optional trailing comment.
    [GeneratedRegex(@"^\s*\[\[?[^\[\]=,]+\]\]?\s*(?:#.*)?$")]
    private static partial Regex Header();

    // A basic or literal string or a bare word, then whitespace and an optional comment.
    [GeneratedRegex(@"^(?<value>""(?:[^""\\]|\\.)*""|'[^']*'|[^\s#]+)(?<tail>\s*(?:#.*)?)$")]
    private static partial Regex Value();

    private static Regex KeyLine(string key) => new(
        @"^(?<lead>[ \t]*(?:" + key + "|\"" + key + "\"|'" + key + @"')[ \t]*=[ \t]*)(?<rest>.*)$");

    public static bool IsYolo(string? text) => State(text, false).IsYolo;

    /// <summary>
    /// The state to show for several CODEX_HOMEs (#186), the default first: one that disagrees with the alias (not YOLO while
    /// it is, YOLO while it is not) so the warning appears, otherwise the default's. Null when none could be read.
    /// </summary>
    public static CodexConfigState? Pick(IReadOnlyList<CodexConfigState> states, bool aliasYolo) =>
        states.FirstOrDefault(s => s.IsYolo != aliasYolo) ?? states.FirstOrDefault();

    public static CodexConfigState State(string? text, bool hasBackup)
    {
        var doc = new Doc(text);
        return new(doc.Get(SandboxKey), doc.Get(ApprovalKey), hasBackup);
    }

    /// <summary>The top-level value of a key, unquoted; null when the key is not set at the top level.</summary>
    public static string? Get(string? text, string key) => new Doc(text).Get(key);

    /// <summary>Sets both keys to their YOLO values: in place when present (keeping a trailing comment), otherwise
    /// inserted before the first table header (and the comment lines directly above it) or at the end.</summary>
    public static string ApplyYolo(string? text)
    {
        var doc = new Doc(text);
        foreach (var (key, value) in new[] { (SandboxKey, YoloSandbox), (ApprovalKey, YoloApproval) })
        {
            var found = doc.Find(key);
            if (found.Count == 0) { doc = doc.Insert(key + " = \"" + value + "\""); continue; }
            foreach (var i in found)
                if (doc.ValueAt(i, key) != value) doc.Lines[i] = doc.WithValue(i, key, "\"" + value + "\"");
        }
        return doc.ToString();
    }

    /// <summary>
    /// Takes each key's top-level line from the backup: set when the backup has it, removed when it does not.
    /// Other lines of the current file are untouched.
    /// </summary>
    public static string Restore(string? current, string? backup)
    {
        var source = new Doc(backup);
        var doc = new Doc(current);
        foreach (var key in Keys)
        {
            var original = source.Find(key) is [var first, ..] ? Doc.Body(source.Lines[first]) : null;
            var found = doc.Find(key);
            if (original is not null && found.Count == 0) { doc = doc.Insert(original); continue; }
            if (original is not null) doc.Lines[found[0]] = original + Doc.Eol(doc.Lines[found[0]]);
            foreach (var i in found.Skip(original is null ? 0 : 1).Reverse()) doc.Lines.RemoveAt(i);
            doc = new Doc(doc.ToString());
        }
        return doc.ToString();
    }

    /// <summary>The file's lines (each with its own line ending) and where its top-level section ends.</summary>
    private sealed class Doc
    {
        public List<string> Lines { get; }
        private readonly bool[] _inString;
        private readonly int _topEnd;
        private readonly string _eol;

        public Doc(string? text)
        {
            text ??= "";
            _eol = text.Contains("\r\n") ? "\r\n" : "\n";
            Lines = Line().Matches(text).Select(m => m.Value).ToList();
            _inString = new bool[Lines.Count];
            _topEnd = Lines.Count;
            string? open = null;
            for (var i = 0; i < Lines.Count; i++)
            {
                var body = Body(Lines[i]);
                _inString[i] = open is not null;
                if (open is null && Header().IsMatch(body)) { _topEnd = i; break; }
                // A multi-line string opens or closes on a line with an odd number of its delimiters.
                foreach (var delimiter in open is null ? ["\"\"\"", "'''"] : new[] { open })
                    if (body.Split(delimiter).Length % 2 == 0) { open = open is null ? delimiter : null; break; }
            }
        }

        public static string Body(string line) => line.TrimEnd('\n').TrimEnd('\r');
        public static string Eol(string line) => line.EndsWith("\r\n") ? "\r\n" : line.EndsWith('\n') ? "\n" : "";

        public List<int> Find(string key)
        {
            var pattern = KeyLine(key);
            return Enumerable.Range(0, _topEnd).Where(i => !_inString[i] && pattern.IsMatch(Body(Lines[i]))).ToList();
        }

        public string? Get(string key) => Find(key) is [var first, ..] ? ValueAt(first, key) : null;

        public string ValueAt(int index, string key)
        {
            var rest = KeyLine(key).Match(Body(Lines[index])).Groups["rest"].Value;
            var value = Value().Match(rest) is { Success: true } m ? m.Groups["value"].Value : rest.Trim();
            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'') return value[1..^1];
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
            return value;
        }

        public string WithValue(int index, string key, string quoted)
        {
            var m = KeyLine(key).Match(Body(Lines[index]));
            var tail = Value().Match(m.Groups["rest"].Value) is { Success: true } v ? v.Groups["tail"].Value : "";
            return m.Groups["lead"].Value + quoted + tail + Eol(Lines[index]);
        }

        /// <summary>A new top-level line: before the first table header and the comments that introduce it, or at the end.</summary>
        public Doc Insert(string line)
        {
            var at = _topEnd;
            if (at < Lines.Count)
                while (at > 0 && !_inString[at - 1] && Body(Lines[at - 1]).TrimStart().StartsWith('#')) at--;
            else if (Lines.Count > 0 && Eol(Lines[^1]) == "") Lines[^1] += _eol;
            Lines.Insert(at, line + _eol);
            return new Doc(ToString());
        }

        public override string ToString() => string.Concat(Lines);
    }
}
