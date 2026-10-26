using System.Text.RegularExpressions;

namespace BoosterStart;

/// <summary>
/// Paths a tab can point at, and their forms for WSL, the file system and the folder dialog.
/// On Windows a project is normally a Windows folder (C:\Users\me\webshop, or a WSL share
/// \\wsl.localhost\Ubuntu\home\me\x); the tab's WSL shell gets /mnt/c/Users/me/webshop or
/// /home/me/x. Linux paths (~, /home/…) still work on Windows and are the norm on Linux.
/// </summary>
public static partial class PathMapping
{
    [GeneratedRegex(@"^\\\\(?:wsl\.localhost|wsl\$)\\[^\\]+(?<rest>\\.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex WslUnc();

    [GeneratedRegex(@"^(?<drive>[A-Za-z]):(?:\\(?<rest>.*))?$")]
    private static partial Regex WindowsDrive();

    /// <summary>A drive path (C:\…) or a WSL share (\\wsl.localhost\…).</summary>
    public static bool IsWindowsPath(string path) => WindowsDrive().IsMatch(path.Trim()) || WslUnc().IsMatch(path.Trim());

    /// <summary>The path the tab's WSL shell cds into; Linux paths pass through unchanged.</summary>
    public static string ToWslPath(string path)
    {
        path = path.Trim();
        if (WslUnc().Match(path.TrimEnd('\\')) is { Success: true } m)
            return "/" + m.Groups["rest"].Value.TrimStart('\\').Replace('\\', '/');
        if (WindowsDrive().Match(path.TrimEnd('\\')) is { Success: true } d)
            return $"/mnt/{d.Groups["drive"].Value.ToLowerInvariant()}/{d.Groups["rest"].Value.Replace('\\', '/')}".TrimEnd('/');
        return path;
    }

    /// <summary>What a folder picked in the dialog is stored as.</summary>
    public static string ToTabPath(string picked)
    {
        picked = picked.TrimEnd('\\', '/');
        // Windows keeps the folder as picked; the WSL form is derived at launch.
        if (OperatingSystem.IsWindows()) return picked.EndsWith(':') ? picked + "\\" : picked;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (picked == home) return "~";
        return picked.StartsWith(home + "/") ? "~" + picked[home.Length..] : picked;
    }

    /// <summary>
    /// The tab directory as a path this process can read. On Windows a Windows path is used
    /// as is, Linux paths go through \\wsl.localhost\distro (wslHome resolves ~). Null when ~
    /// cannot be resolved yet.
    /// </summary>
    public static string? ToLocalPath(string tabPath, string distro, string? wslHome)
    {
        tabPath = tabPath.Trim();
        if (OperatingSystem.IsWindows())
        {
            if (IsWindowsPath(tabPath)) return tabPath;
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
            if (IsWindowsPath(tabPath)) return tabPath;
            if (tabPath.StartsWith("/mnt/") && tabPath.Length >= 6)
                return $"{char.ToUpperInvariant(tabPath[5])}:\\" + tabPath[Math.Min(7, tabPath.Length)..].Replace('/', '\\');
            if (tabPath.StartsWith('/')) return $@"\\wsl.localhost\{distro}" + tabPath.Replace('/', '\\');
            // New projects usually live in Documents.
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return tabPath == "~" || tabPath == "" ? home
            : tabPath.StartsWith("~/") ? Path.Combine(home, tabPath[2..]) : tabPath;
    }
}
