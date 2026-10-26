using System.Text.RegularExpressions;

namespace BoosterStart;

/// <summary>
/// Converts between folders picked in the OS dialog and the Linux paths tabs use.
/// On Windows the tabs run in WSL, so \\wsl.localhost\Ubuntu\home\me\x becomes /home/me/x
/// and C:\Users\me becomes /mnt/c/Users/me.
/// </summary>
public static partial class PathMapping
{
    [GeneratedRegex(@"^\\\\(?:wsl\.localhost|wsl\$)\\[^\\]+(?<rest>\\.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex WslUnc();

    [GeneratedRegex(@"^(?<drive>[A-Za-z]):\\?(?<rest>.*)$")]
    private static partial Regex WindowsDrive();

    public static string ToTabPath(string picked)
    {
        picked = picked.TrimEnd('\\', '/');
        if (OperatingSystem.IsWindows())
        {
            if (WslUnc().Match(picked) is { Success: true } m)
                return "/" + m.Groups["rest"].Value.TrimStart('\\').Replace('\\', '/');
            if (WindowsDrive().Match(picked) is { Success: true } d)
                return $"/mnt/{d.Groups["drive"].Value.ToLowerInvariant()}/{d.Groups["rest"].Value.Replace('\\', '/')}".TrimEnd('/');
            return picked;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (picked == home) return "~";
        return picked.StartsWith(home + "/") ? "~" + picked[home.Length..] : picked;
    }

    /// <summary>
    /// The tab directory as a path this process can read: \\wsl.localhost\distro\… on Windows
    /// (wslHome resolves ~), the expanded path on Linux. Null when ~ cannot be resolved yet.
    /// </summary>
    public static string? ToLocalPath(string tabPath, string distro, string? wslHome)
    {
        tabPath = tabPath.Trim();
        if (OperatingSystem.IsWindows())
        {
            if (tabPath == "" || tabPath == "~" || tabPath.StartsWith("~/"))
            {
                if (wslHome is null) return null;
                tabPath = wslHome + (tabPath.Length > 1 ? tabPath[1..] : "");
            }
            if (tabPath.StartsWith("/mnt/") && tabPath.Length >= 6 && (tabPath.Length == 6 || tabPath[6] == '/'))
                return $"{char.ToUpperInvariant(tabPath[5])}:\\" + tabPath[Math.Min(7, tabPath.Length)..].Replace('/', '\\');
            return $@"\\wsl.localhost\{distro}" + tabPath.Replace('/', '\\');
        }
        return StartFolder(tabPath, distro);
    }

    /// <summary>True when the project has the shared orchestra (policy, Claude command or Codex skill).</summary>
    public static bool HasOrkester(string localPath) =>
        System.IO.Directory.Exists(Path.Combine(localPath, ".orchestra"))
        || File.Exists(Path.Combine(localPath, ".claude", "commands", "orkester.md"))
        || System.IO.Directory.Exists(Path.Combine(localPath, ".agents", "skills", "orkester"));

    /// <summary>Where the folder dialog opens for a tab's current directory.</summary>
    public static string StartFolder(string tabPath, string distro)
    {
        tabPath = tabPath.Trim();
        if (OperatingSystem.IsWindows())
        {
            var root = $@"\\wsl.localhost\{distro}";
            if (tabPath.StartsWith("/mnt/") && tabPath.Length >= 6)
                return $"{char.ToUpperInvariant(tabPath[5])}:\\" + tabPath[Math.Min(7, tabPath.Length)..].Replace('/', '\\');
            // The WSL user's home is unknown from Windows; /home lists it.
            return tabPath.StartsWith('/') ? root + tabPath.Replace('/', '\\') : root + @"\home";
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return tabPath == "~" || tabPath == "" ? home
            : tabPath.StartsWith("~/") ? Path.Combine(home, tabPath[2..]) : tabPath;
    }
}
