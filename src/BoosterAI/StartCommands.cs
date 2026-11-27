using System.Text;
using System.Text.Json;

namespace BoosterAI;

/// <summary>Where a start command was found: the tab's folder, the user's (or the account's) directory, or an installed plugin.</summary>
public enum StartCommandSource { Project, User, Plugin }

/// <summary>
/// A command a tab can start its agent with (#255): <paramref name="Name"/> is exactly what is typed as the first prompt
/// ("/deploy", "/frontend:review", "$release-notes"), <paramref name="Description"/> comes from the file's frontmatter.
/// </summary>
public sealed record StartCommand(string Name, StartCommandSource Source, string? Description = null);

/// <summary>What to discover: the tab's agent, its Linux directory and its account's configuration directory (null: the default).</summary>
public sealed record StartCommandRequest(string Llm, string Directory, string? ConfigDirectory = null);

/// <summary>
/// The start-command list of a tab (#255): instead of only Off/On/Run once for the orchestra (#152), a tab starts with no
/// command, with <c>/orkester</c> (which keeps its install offer, status and reload), or with a command found for its agent
/// and folder. Discovery reads the command files in the tabs' WSL or Linux environment (one hidden bash for every request,
/// with a time limit, like the agent detection) and returns them grouped by <see cref="StartCommandSource"/>.
/// <list type="bullet">
/// <item>claude: <c>.claude/commands/**/*.md</c> in the folder and in the account's directory (subdirectories become a
/// namespace, <c>commands/ns/x.md</c> is <c>/ns:x</c>, checked against Claude Code 2.1.332), user-invocable skills
/// <c>.claude/skills/*/SKILL.md</c> there, and the commands and skills of enabled plugins (<c>/plugin:name</c>).</item>
/// <item>codex: skills in <c>.agents/skills</c> of the folder, <c>~/.agents/skills</c> and <c>$CODEX_HOME/skills</c>, typed as
/// <c>$name</c>. Custom prompts (<c>~/.codex/prompts</c>, <c>/prompts:name</c>) are not listed: Codex 0.172 no longer reads them.</item>
/// <item>agy: workflows in <c>.agent/workflows</c>, <c>.agents/workflows</c>, <c>_agent/workflows</c>, <c>_agents/workflows</c>
/// and <c>~/.gemini/config/global_workflows</c> or <c>workflows</c>, typed as <c>/name</c>.</item>
/// <item>aider, interpreter, openhands: none (they have no user-defined commands).</item>
/// </list>
/// </summary>
public static class StartCommands
{
    /// <summary>The orchestra's command; it is never listed among the discovered ones, it has its own entry at the top.</summary>
    public const string Orkester = "/orkester";

    /// <summary>The agents with command files BoosterAI can list.</summary>
    public static bool Supports(string llm) => llm is "claude" or "codex" or "agy";

    /// <summary>Whether the tab starts with a command: the orchestra or a chosen one.</summary>
    public static bool Has(TabConfig tab) => tab.Orkester || !string.IsNullOrWhiteSpace(tab.Command);

    /// <summary>Run once applies: a start command and NonInteractive (#255; before, only with the orchestra).</summary>
    public static bool RunsOnce(TabConfig? tab) => tab is { NonInteractive: true } && Has(tab);

    /// <summary>The tab's own command (without arguments): <see cref="Orkester"/>, the chosen one, or null.</summary>
    public static string? Name(TabConfig tab) => tab.Orkester ? Orkester
        : string.IsNullOrWhiteSpace(tab.Command) ? null : tab.Command.Trim();

    /// <summary>The arguments as typed after the command: one line, trimmed; "" when there are none.</summary>
    public static string Arguments(TabConfig tab) => OneLine(tab.CommandArgs);

    /// <summary>The first prompt the tab's command sends: the command and its arguments, or null without a command.</summary>
    public static string? Text(TabConfig tab) => Name(tab) is not { } name ? null
        : Arguments(tab) is { Length: > 0 } args ? name + " " + args : name;

    /// <summary>Newlines and tabs become spaces, so the prompt stays one line in the tab script.</summary>
    public static string OneLine(string? text) =>
        string.Join(' ', (text ?? "").Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0));

    /// <summary>Lines of the discovery output start with this; everything else is noise from the shell.</summary>
    private const string Marker = "bscmd";

    /// <summary>
    /// The discovery bash, run as <c>bash -c script bash llm dir confdir [llm dir confdir ...]</c> (no login shell: it only reads
    /// files). Each found file gives one line <c>bscmd index kind root path head</c>, with root, path relative to it and the
    /// file's first 60 lines (at most 6000 bytes) in base64. Plugin and settings files of claude are sent whole (at most 200 kB).
    /// </summary>
    public static readonly string DiscoverScript = """
bs_emit() {
  r=$(printf '%s' "$3" | base64 -w0)
  p=$(printf '%s' "${4#"$3"/}" | base64 -w0)
  h=$(head -n 60 -- "$4" 2>/dev/null | head -c 6000 | base64 -w0)
  printf 'bscmd %s %s %s %s %s\n' "$1" "$2" "${r:--}" "${p:--}" "${h:--}"
}
bs_files() {
  [ -d "$3" ] || return 0
  find -L "$3" -mindepth "$4" -maxdepth "$5" -type f -name "$6" 2>/dev/null | LC_ALL=C sort | head -n 300 | while IFS= read -r f
  do
    bs_emit "$1" "$2" "$3" "$f"
  done
}
bs_whole() {
  [ -f "$3" ] || return 0
  w=$(head -c 200000 -- "$3" 2>/dev/null | base64 -w0)
  printf 'bscmd %s %s - - %s\n' "$1" "$2" "${w:--}"
}
i=0
while [ $# -ge 3 ]
do
  llm=$1
  dir=$2
  conf=$3
  shift 3
  case $dir in
    '~') dir=$HOME ;;
    '~/'*) dir=$HOME/${dir#'~/'} ;;
  esac
  case $conf in
    '~') conf=$HOME ;;
    '~/'*) conf=$HOME/${conf#'~/'} ;;
  esac
  if [ -d "$dir" ]
  then
    case $llm in
      claude)
        c=${conf:-$HOME/.claude}
        bs_files $i cc-pc "$dir/.claude/commands" 1 4 '*.md'
        bs_files $i cc-ps "$dir/.claude/skills" 2 2 SKILL.md
        bs_files $i cc-uc "$c/commands" 1 4 '*.md'
        bs_files $i cc-us "$c/skills" 2 2 SKILL.md
        bs_whole $i cc-plugins "$c/plugins/installed_plugins.json"
        bs_whole $i cc-set-user "$c/settings.json"
        bs_whole $i cc-set-project "$dir/.claude/settings.json"
        bs_whole $i cc-set-local "$dir/.claude/settings.local.json"
        if [ -f "$c/plugins/installed_plugins.json" ]
        then
          sed -n 's/.*"installPath"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$c/plugins/installed_plugins.json" | LC_ALL=C sort -u | head -n 100 | while IFS= read -r p
          do
            bs_files $i cc-plc "$p/commands" 1 4 '*.md'
            bs_files $i cc-pls "$p/skills" 2 2 SKILL.md
          done
        fi
        ;;
      codex)
        c=${conf:-${CODEX_HOME:-$HOME/.codex}}
        bs_files $i cx-ps "$dir/.agents/skills" 2 2 SKILL.md
        bs_files $i cx-us "$HOME/.agents/skills" 2 2 SKILL.md
        bs_files $i cx-us "$c/skills" 2 2 SKILL.md
        ;;
      agy)
        for w in .agent .agents _agent _agents
        do
          bs_files $i ag-pw "$dir/$w/workflows" 1 1 '*.md'
        done
        bs_files $i ag-uw "$HOME/.gemini/config/global_workflows" 1 1 '*.md'
        bs_files $i ag-uw "$HOME/.gemini/config/workflows" 1 1 '*.md'
        ;;
    esac
  fi
  printf 'bscmd %s done - - -\n' $i
  i=$((i + 1))
done
true
""";

    /// <summary>The arguments after the script for <paramref name="requests"/>: three per request.</summary>
    public static IEnumerable<string> ScriptArguments(IEnumerable<StartCommandRequest> requests) =>
        requests.SelectMany(r => new[] { r.Llm, string.IsNullOrWhiteSpace(r.Directory) ? "~" : r.Directory.Trim(), r.ConfigDirectory ?? "" });

    /// <summary>
    /// Runs the discovery for every request in one hidden bash (inside WSL on Windows) and returns one list per request; a
    /// request whose part of the output never finished (time limit, no WSL) gets null, so its old list stays.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<StartCommand>?> Discover(string distro, IReadOnlyList<StartCommandRequest> requests, TimeSpan? timeout = null)
    {
        if (requests.Count == 0) return [];
        var args = new List<string> { "-c", DiscoverScript, "bash" };
        args.AddRange(ScriptArguments(requests));
        var psi = OperatingSystem.IsWindows()
            ? Prerequisites.Hidden("wsl.exe", new[] { "-d", distro, "--exec", "bash" }.Concat(args))
            : Prerequisites.Hidden("bash", args);
        var (_, _, output) = Prerequisites.Run(psi, timeout ?? TimeSpan.FromSeconds(20));
        return Parse(output ?? "", requests);
    }

    private sealed record Found(string Kind, string Root, string Path, string Text);

    /// <summary>Reads the discovery output for <paramref name="requests"/> (in their order); null for a request without its "done" line.</summary>
    public static IReadOnlyList<IReadOnlyList<StartCommand>?> Parse(string output, IReadOnlyList<StartCommandRequest> requests)
    {
        var found = new List<Found>[requests.Count];
        var done = new bool[requests.Count];
        for (var i = 0; i < found.Length; i++) found[i] = [];
        foreach (var raw in output.Split('\n'))
        {
            var parts = raw.TrimEnd('\r').Split(' ');
            if (parts.Length != 6 || parts[0] != Marker || !int.TryParse(parts[1], out var index) || index < 0 || index >= requests.Count) continue;
            if (parts[2] == "done") { done[index] = true; continue; }
            found[index].Add(new Found(parts[2], Decode(parts[3]), Decode(parts[4]), Decode(parts[5])));
        }
        return requests.Select((r, i) => done[i] ? List(r, found[i]) : null).ToArray();
    }

    private static string Decode(string value)
    {
        if (value == "-") return "";
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
        catch (FormatException) { return ""; }
    }

    private static IReadOnlyList<StartCommand> List(StartCommandRequest request, List<Found> files)
    {
        var list = new List<StartCommand>();
        void Add(string? name, StartCommandSource source, string? description)
        {
            if (name is null || name.Length < 2 || name.Any(char.IsWhiteSpace)) return;
            if (name[1..] == Orkester[1..] || list.Any(c => c.Name == name)) return;
            list.Add(new StartCommand(name, source, description));
        }
        var plugins = request.Llm == "claude" ? EnabledPlugins(request, files) : [];
        // Project first, so a project command wins over a user command of the same name.
        foreach (var f in files.OrderBy(f => SourceOf(f.Kind)))
        {
            var (fields, body, broken) = Frontmatter.Parse(f.Text);
            var description = Describe(fields, body, broken);
            switch (f.Kind)
            {
                case "cc-pc" or "cc-uc":
                    Add("/" + CommandName(f.Path), SourceOf(f.Kind), description);
                    break;
                case "cc-ps" or "cc-us":
                    if (UserInvocable(fields)) Add("/" + SkillName(f.Path, fields), SourceOf(f.Kind), description);
                    break;
                case "cc-plc" or "cc-pls":
                    // The file's root is the install directory's commands/ or skills/.
                    var installDir = f.Root.TrimEnd('/');
                    if (plugins.GetValueOrDefault(installDir[..Math.Max(0, installDir.LastIndexOf('/'))]) is not { } plugin) break;
                    if (f.Kind == "cc-pls" && !UserInvocable(fields)) break;
                    Add("/" + plugin + ":" + (f.Kind == "cc-plc" ? CommandName(f.Path) : SkillName(f.Path, fields)), StartCommandSource.Plugin, description);
                    break;
                case "cx-ps" or "cx-us":
                    Add("$" + SkillName(f.Path, fields), SourceOf(f.Kind), description);
                    break;
                case "ag-pw" or "ag-uw":
                    Add("/" + Path.GetFileNameWithoutExtension(f.Path), SourceOf(f.Kind), description);
                    break;
            }
        }
        return list.OrderBy(c => c.Source).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static StartCommandSource SourceOf(string kind) => kind switch
    {
        "cc-pc" or "cc-ps" or "cx-ps" or "ag-pw" => StartCommandSource.Project,
        "cc-plc" or "cc-pls" => StartCommandSource.Plugin,
        _ => StartCommandSource.User,
    };

    /// <summary>A command file's name: <c>ns/sub/x.md</c> is <c>ns:sub:x</c> (Claude Code's namespace for subdirectories).</summary>
    internal static string CommandName(string path)
    {
        var p = path.Replace('\\', '/').Trim('/');
        if (p.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) p = p[..^3];
        return string.Join(':', p.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>A skill's name: its frontmatter <c>name</c> when it is a valid name, else its directory's.</summary>
    internal static string SkillName(string path, IReadOnlyDictionary<string, string> fields)
    {
        if (fields.TryGetValue("name", out var name) && name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':')) return name;
        var p = path.Replace('\\', '/').Trim('/');
        var slash = p.LastIndexOf('/');
        return slash > 0 ? p[..slash] : p;
    }

    private static bool UserInvocable(IReadOnlyDictionary<string, string> fields) =>
        !fields.TryGetValue("user-invocable", out var v) || !v.Equals("false", StringComparison.OrdinalIgnoreCase);

    /// <summary>The description from the frontmatter, else (as Claude Code does) the body's first line; none for broken frontmatter.</summary>
    private static string? Describe(IReadOnlyDictionary<string, string> fields, string body, bool broken)
    {
        var text = fields.GetValueOrDefault("description");
        if (string.IsNullOrWhiteSpace(text) && !broken)
            text = body.Split('\n').Select(l => l.Trim().TrimStart('#').Trim()).FirstOrDefault(l => l.Length > 0);
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = OneLine(text);
        return text.Length > 160 ? text[..159].TrimEnd() + "…" : text;
    }

    /// <summary>
    /// The enabled plugins that apply to the request's folder, by install directory: their name
    /// (the key before '@'). A plugin is on when the settings enable it (user, then project, then local settings win), and a
    /// project- or local-scope install only counts in its own project.
    /// </summary>
    private static Dictionary<string, string> EnabledPlugins(StartCommandRequest request, List<Found> files)
    {
        var enabled = new Dictionary<string, bool>();
        foreach (var kind in new[] { "cc-set-user", "cc-set-project", "cc-set-local" })
            foreach (var f in files.Where(f => f.Kind == kind))
                try
                {
                    using var doc = JsonDocument.Parse(f.Text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("enabledPlugins", out var map) && map.ValueKind == JsonValueKind.Object)
                        foreach (var p in map.EnumerateObject())
                            if (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) enabled[p.Name] = p.Value.ValueKind == JsonValueKind.True;
                }
                catch (JsonException) { }
        var result = new Dictionary<string, string>();
        foreach (var f in files.Where(f => f.Kind == "cc-plugins"))
            try
            {
                using var doc = JsonDocument.Parse(f.Text);
                if (!doc.RootElement.TryGetProperty("plugins", out var plugins) || plugins.ValueKind != JsonValueKind.Object) continue;
                foreach (var plugin in plugins.EnumerateObject())
                {
                    if (!enabled.GetValueOrDefault(plugin.Name) || plugin.Value.ValueKind != JsonValueKind.Array) continue;
                    var name = plugin.Name.Split('@')[0];
                    foreach (var install in plugin.Value.EnumerateArray())
                    {
                        if (install.ValueKind != JsonValueKind.Object || !install.TryGetProperty("installPath", out var path) || path.ValueKind != JsonValueKind.String) continue;
                        var scope = install.TryGetProperty("scope", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : "user";
                        var project = install.TryGetProperty("projectPath", out var pp) && pp.ValueKind == JsonValueKind.String ? pp.GetString() : null;
                        if (scope is "project" or "local" && !SameDirectory(project, request.Directory)) continue;
                        result.TryAdd(path.GetString()!.TrimEnd('/'), name);
                    }
                }
            }
            catch (JsonException) { }
        return result;
    }

    private static bool SameDirectory(string? a, string b) =>
        a is not null && a.TrimEnd('/') == b.Trim().TrimEnd('/');

    /// <summary>Invented commands for --screenshot (neutral names, never read from disk).</summary>
    public static IReadOnlyList<StartCommand> Samples(string llm) => llm switch
    {
        "codex" => [new("$release-notes", StartCommandSource.Project, "Draft release notes from merged changes"), new("$tidy-imports", StartCommandSource.User, "Sort and prune imports")],
        "agy" => [new("/deploy-preview", StartCommandSource.Project, "Build and publish a preview"), new("/standup", StartCommandSource.User, "Summarise yesterday's commits")],
        "claude" =>
        [
            new("/deploy-preview", StartCommandSource.Project, "Build and publish a preview"),
            new("/docs:check-links", StartCommandSource.Project, "Find broken links in the docs"),
            new("/review", StartCommandSource.Project, "Review the current branch"),
            new("/standup", StartCommandSource.User, "Summarise yesterday's commits"),
            new("/frontend-kit:component", StartCommandSource.Plugin, "Scaffold a UI component"),
        ],
        _ => [],
    };
}

/// <summary>The YAML frontmatter of a command or skill file, read as flat <c>key: value</c> lines (enough for name and description).</summary>
public static class Frontmatter
{
    /// <summary>
    /// The fields and the body after the frontmatter. <c>broken</c> is true when the file opens a frontmatter block that never
    /// closes; it then has no fields and no body. Lines that are not <c>key: value</c> are skipped; quoted values are unquoted and
    /// block scalars (<c>|</c>, <c>&gt;</c>) are joined into one line.
    /// </summary>
    public static (IReadOnlyDictionary<string, string> Fields, string Body, bool Broken) Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (lines.Length == 0 || lines[0].TrimEnd() != "---") return (fields, text, false);
        var end = Array.FindIndex(lines, 1, l => l.TrimEnd() is "---" or "...");
        if (end < 0) return (fields, "", true);
        for (var i = 1; i < end; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line.StartsWith('#')) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (value is "|" or ">" or "|-" or ">-" or "|+" or ">+")
            {
                var block = new List<string>();
                while (i + 1 < end && (lines[i + 1].Length == 0 || char.IsWhiteSpace(lines[i + 1][0]))) block.Add(lines[++i].Trim());
                value = string.Join(' ', block.Where(b => b.Length > 0));
            }
            else if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
                value = value[0] == '"' ? value[1..^1].Replace("\\\"", "\"") : value[1..^1].Replace("''", "'");
            fields.TryAdd(key, value);
        }
        return (fields, string.Join('\n', lines.Skip(end + 1)), false);
    }
}
