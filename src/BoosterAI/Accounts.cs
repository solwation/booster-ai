using System.Globalization;
using System.Text;

namespace BoosterAI;

/// <summary>
/// An extra login for an agent (#186): its own configuration directory, passed to the agent through the agent's
/// environment variable (<see cref="Accounts.EnvVar"/>). The default account is implicit: it has no entry, sets no
/// variable and uses the agent's usual directory, so configs without accounts behave as before.
/// </summary>
public sealed record AgentAccount
{
    /// <summary>Stable id that <see cref="TabConfig.Account"/> refers to; never changes on rename.</summary>
    public string Id { get; set; } = "";
    /// <summary>The agent (claude, codex) the account belongs to.</summary>
    public string Agent { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Linux path of the configuration directory (inside WSL on Windows), normally <c>~/.claude-&lt;name&gt;</c>.</summary>
    public string Directory { get; set; } = "";
}

/// <summary>Which agents support separate accounts, and how a tab's chosen account reaches the agent.</summary>
public static class Accounts
{
    /// <summary>The environment variable that points an agent at another configuration directory; null when it has none.</summary>
    public static string? EnvVar(string llm) => llm switch
    {
        // https://code.claude.com/docs/en/settings#environment-variables
        "claude" => "CLAUDE_CONFIG_DIR",
        // https://github.com/openai/codex/blob/main/docs/config.md (CODEX_HOME)
        "codex" => "CODEX_HOME",
        // agy, aider, interpreter and openhands document no such variable: they only offer the default account.
        _ => null,
    };

    public static bool Supports(string llm) => EnvVar(llm) is not null;

    /// <summary>The agent's own directory, which the default account uses.</summary>
    public static string DefaultDirectory(string llm) => llm switch
    {
        "claude" => "~/.claude",
        "codex" => CodexConfig.Home,
        _ => "",
    };

    /// <summary>The agent's command that logs in to the account the variable points at.</summary>
    public static string LoginCommand(string llm) => llm switch
    {
        "claude" => "claude auth login",
        "codex" => "codex login",
        _ => throw new ArgumentException("The agent has no separate accounts.", nameof(llm)),
    };

    public static IReadOnlyList<AgentAccount> For(AppConfig cfg, string llm) =>
        Supports(llm) ? cfg.Accounts.Where(a => a.Agent == llm).ToList() : [];

    /// <summary>The tab's account; null for the default account and for an id that no longer exists or belongs to another agent.</summary>
    public static AgentAccount? Find(IEnumerable<AgentAccount> accounts, string llm, string? id) =>
        string.IsNullOrEmpty(id) || !Supports(llm) ? null : accounts.FirstOrDefault(a => a.Id == id && a.Agent == llm);

    /// <summary>
    /// The configuration directory a launch passes for <paramref name="tab"/>: null for the default account. An account
    /// that is missing (removed by hand) also gives null; <see cref="Launcher.Validate"/> then refuses the tab rather
    /// than starting it silently with the default login.
    /// </summary>
    public static string? DirectoryFor(AppConfig cfg, TabConfig tab) => Find(cfg.Accounts, tab.Llm, tab.Account)?.Directory;

    /// <summary>Fills in <see cref="TabConfig.AccountDirectory"/> for a launch, as the main window does through ToConfig.</summary>
    public static void Resolve(AppConfig cfg, IEnumerable<TabConfig> tabs)
    {
        foreach (var tab in tabs) tab.AccountDirectory = DirectoryFor(cfg, tab);
    }

    /// <summary>True when the tab asks for an account that a launch cannot use.</summary>
    public static bool IsMissing(TabConfig tab) => !string.IsNullOrEmpty(tab.Account) && Supports(tab.Llm) && tab.AccountDirectory is null;

    /// <summary>
    /// A directory is passed to bash quoted (<see cref="Launcher.Quote"/>); it must be absolute or under the home directory and
    /// may not contain what <see cref="Launcher.Validate"/> refuses in a project directory.
    /// </summary>
    public static bool IsValidDirectory(string dir) =>
        (dir.StartsWith("~/", StringComparison.Ordinal) && dir.Length > 2 || dir.StartsWith('/') && dir.Length > 1)
        && dir.IndexOfAny([';', '\n', '\r']) < 0;

    /// <summary>A bash word for <paramref name="dir"/>: <c>~/</c> stays unquoted so it expands to the home directory.</summary>
    public static string ShellPath(string dir) =>
        dir.StartsWith("~/", StringComparison.Ordinal) ? "~/" + Launcher.Quote(dir[2..]) : Launcher.Quote(dir);

    /// <summary>
    /// Bash that exports the account's variable in the tab's shell, so the agent, its update step and the shell left after it
    /// all use the account, and creates the directory (Codex refuses a missing CODEX_HOME). bs_acct remembers which variable
    /// BoosterAI set, so an in-place restart with the default account can take it back (<see cref="ResetCommand"/>).
    /// Ends in " &amp;&amp; " when not empty; no ';' or '"' (see <see cref="Launcher.TabScript"/>).
    /// </summary>
    public static string ExportCommand(TabConfig tab)
    {
        if (tab.AccountDirectory is not { } dir || EnvVar(tab.Llm) is not { } name) return "";
        var path = ShellPath(dir);
        return $"export {name}={path} && bs_acct={name} && mkdir -p {path} && ";
    }

    /// <summary>
    /// For an in-place restart: unsets the variable an earlier run in this shell exported (none on a fresh tab: it then only
    /// unsets bs_acct itself). It always succeeds, so it fits anywhere in an &amp;&amp; chain.
    /// </summary>
    public const string ResetCommand = "unset ${bs_acct:-bs_acct} bs_acct";

    /// <summary>The one-off tab of Settings → Login: the agent's login with the account's variable, then a shell that keeps it.</summary>
    public static string LoginScript(AgentAccount account)
    {
        var name = EnvVar(account.Agent) ?? throw new ArgumentException("The agent has no separate accounts.", nameof(account));
        var path = ShellPath(account.Directory);
        // Windows Terminal splits its command line on every ';', also inside quotes (see Launcher.TabScript).
        var note = $"BoosterAI: {account.Name} ({name}={account.Directory})".Replace(';', ',');
        return $"export {name}={path} && mkdir -p {path} && cd ~ && printf '%s\\n' {Launcher.Quote(note)} && {LoginCommand(account.Agent)} || true && exec bash";
    }

    /// <summary>
    /// A new account for <paramref name="agent"/>: its id and directory come from the name (<c>~/.claude-work</c>) and stay
    /// the same when it is renamed; a number keeps them apart from existing accounts and the default directory.
    /// </summary>
    public static AgentAccount Create(IEnumerable<AgentAccount> existing, string agent, string name)
    {
        if (!Supports(agent)) throw new ArgumentException("The agent has no separate accounts.", nameof(agent));
        var taken = existing.SelectMany(a => new[] { a.Id, a.Directory }).ToHashSet(StringComparer.Ordinal);
        var slug = Slug(name);
        var id = $"{agent}-{slug}";
        for (var n = 2; taken.Contains(id) || taken.Contains($"~/.{id}"); n++) id = $"{agent}-{slug}-{n}";
        return new AgentAccount { Id = id, Agent = agent, Name = name.Trim(), Directory = $"~/.{id}" };
    }

    /// <summary>Lower-case ASCII letters, digits and single hyphens: "Jobb Åsa" becomes "jobb-asa"; nothing usable gives "account".</summary>
    public static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            if (sb.Length >= 24) break;
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "account" : slug;
    }

    /// <summary>The account's display name for <paramref name="tab"/>'s choice; the localized "Standard" for the default account.</summary>
    public static string DisplayName(IEnumerable<AgentAccount> accounts, string llm, string? id) =>
        Find(accounts, llm, id)?.Name ?? Loc.Get("account.default");
}
