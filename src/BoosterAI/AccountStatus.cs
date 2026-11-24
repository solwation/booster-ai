using System.Text;
using System.Text.Json;

namespace BoosterAI;

public enum LoginState { Unknown, LoggedIn, LoggedOut }

/// <summary>
/// What an agent says about an account's login (#192). <see cref="Who"/> is an identity such as "name@example.com · Org" (Claude),
/// <see cref="Method"/> how it logged in ("ChatGPT", Codex). Both only ever reach the UI; nothing here logs or saves them.
/// </summary>
public sealed record AccountLoginStatus(LoginState State, string? Who = null, string? Method = null)
{
    public static readonly AccountLoginStatus Unknown = new(LoginState.Unknown);
    public static readonly AccountLoginStatus LoggedOut = new(LoginState.LoggedOut);

    /// <summary>The identity shown beside the account's name; empty unless logged in and known.</summary>
    public string Identity => State == LoginState.LoggedIn ? Who ?? Method ?? "" : "";

    /// <summary>The status line: "Inloggad som ...", "Inloggad med ...", "Inloggad", "Inte inloggad" or "Okänd status".</summary>
    public string Text => State switch
    {
        LoginState.LoggedIn when Who is not null => Loc.Format("account.status.as", Who),
        LoginState.LoggedIn when Method is not null => Loc.Format("account.status.with", Method),
        LoginState.LoggedIn => Loc.Get("account.status.in"),
        LoginState.LoggedOut => Loc.Get("account.status.out"),
        _ => Loc.Get("account.status.unknown"),
    };
}

/// <summary>
/// Asks an agent whether an account is logged in (#192): <c>claude auth status --json</c> and <c>codex login status</c> in the tab's
/// login shell (WSL on Windows), with the account's variable set for a non-default account and nothing set for the default one.
/// </summary>
public static class AccountStatusProbe
{
    /// <summary>The exit code of the script when the account's directory does not exist: nothing has logged in there yet.</summary>
    internal const int NoDirectory = 3;
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(12);

    /// <summary>The bash that runs the status command; the inner timeout keeps a hung agent from outliving the check.</summary>
    public static string Script(string llm, string? directory)
    {
        var export = "";
        if (directory is not null)
        {
            var name = Accounts.EnvVar(llm) ?? throw new ArgumentException("The agent has no separate accounts.", nameof(llm));
            export = $"export {name}={Accounts.ShellPath(directory)}; [ -d \"${name}\" ] || exit {NoDirectory}; ";
        }
        var command = llm switch
        {
            "claude" => "claude auth status --json 2>/dev/null",
            "codex" => "codex login status 2>&1",
            _ => throw new ArgumentException("The agent has no separate accounts.", nameof(llm)),
        };
        return $"{export}timeout -k 1 9 {command} </dev/null";
    }

    /// <summary>The process that runs <see cref="Script"/>: <c>wsl.exe -d distro --exec bash -lic</c> on Windows, <c>bash -lic</c> elsewhere.</summary>
    public static System.Diagnostics.ProcessStartInfo Command(string distro, string llm, string? directory) =>
        OperatingSystem.IsWindows()
            ? Prerequisites.Hidden("wsl.exe", ["-d", distro, "--exec", "bash", "-lic", Script(llm, directory)])
            : Prerequisites.Hidden("bash", ["-lic", Script(llm, directory)]);

    /// <summary>Runs the check (blocking, bounded by <see cref="Limit"/>); call it off the UI thread.</summary>
    public static AccountLoginStatus Check(string distro, string llm, string? directory)
    {
        var (started, code, output) = Prerequisites.Run(Command(distro, llm, directory), Limit);
        return Parse(llm, started, code, output);
    }

    /// <summary>Reads the command's result; anything unexpected (not started, timeout, other failure) is Unknown, never LoggedOut.</summary>
    public static AccountLoginStatus Parse(string llm, bool started, int exitCode, string? output)
    {
        if (!started) return AccountLoginStatus.Unknown;
        if (exitCode == NoDirectory) return AccountLoginStatus.LoggedOut;
        return llm switch
        {
            "claude" => ParseClaude(output),
            "codex" => ParseCodex(exitCode, output),
            _ => AccountLoginStatus.Unknown,
        };
    }

    private static AccountLoginStatus ParseClaude(string? output)
    {
        // Startup files may print before the JSON: take the outermost braces.
        var text = output ?? "";
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return AccountLoginStatus.Unknown;
        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("loggedIn", out var flag)) return AccountLoginStatus.Unknown;
            if (flag.ValueKind == JsonValueKind.False) return AccountLoginStatus.LoggedOut;
            if (flag.ValueKind != JsonValueKind.True) return AccountLoginStatus.Unknown;
            var email = Field(root, "email");
            var org = Field(root, "orgName");
            var who = email is null ? null : org is null ? email : email + " · " + org;
            return new AccountLoginStatus(LoginState.LoggedIn, who, who is null ? Field(root, "authMethod") : null);
        }
        catch (JsonException) { return AccountLoginStatus.Unknown; }
    }

    private static AccountLoginStatus ParseCodex(int exitCode, string? output)
    {
        foreach (var raw in (output ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Not logged in", StringComparison.OrdinalIgnoreCase)) return AccountLoginStatus.LoggedOut;
            if (!line.StartsWith("Logged in", StringComparison.OrdinalIgnoreCase)) continue;
            if (exitCode != 0) return AccountLoginStatus.Unknown;
            const string prefix = "Logged in using ";
            var method = line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? Clean(line[prefix.Length..]) : null;
            return new AccountLoginStatus(LoginState.LoggedIn, null, method);
        }
        return AccountLoginStatus.Unknown;
    }

    private static string? Field(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? Clean(v.GetString()) : null;

    /// <summary>Single line, no control characters, bounded: the text comes from a CLI and is only displayed.</summary>
    private static string? Clean(string? value)
    {
        if (value is null) return null;
        var sb = new StringBuilder();
        foreach (var c in value) if (!char.IsControl(c)) sb.Append(c);
        var text = sb.ToString().Trim();
        if (text.Length > 120) text = text[..120];
        return text.Length == 0 ? null : text;
    }
}
