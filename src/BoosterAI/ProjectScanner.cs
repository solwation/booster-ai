namespace BoosterAI;

/// <summary>A folder that looks like a project: a git checkout and/or an AI instruction file in its root.</summary>
/// <param name="Orchestra">Whether the project has the shared orchestra (<see cref="PathMapping.HasOrkester"/>).</param>
/// <param name="OrchestraVersion">The project's own pinned orchestra version ("0.9.0"), or null when it cannot be
/// determined; see <see cref="ProjectScanner.OrchestraVersion"/>.</param>
public sealed record FoundProject(string Path, string Name, bool Git, bool Ai, bool Orchestra = false, string? OrchestraVersion = null);

/// <summary>
/// "Hitta projekt": checks the selected root, then walks a few levels below it when it is not a project. A folder
/// counts when it has .git (directory, or file for worktrees/submodules) or one of the AI
/// instruction files in <see cref="AiMarkers"/>. The walk does not descend into a project it
/// found, nor into hidden folders, links or dependency/build folders.
/// </summary>
public static class ProjectScanner
{
    public const int DefaultDepth = 3;

    /// <summary>Root entries that mark an AI project (files or folders).</summary>
    public static readonly IReadOnlyList<string> AiMarkers =
        ["AGENTS.md", "CLAUDE.md", "AI.md", "GEMINI.md", ".claude", ".orchestra", ".agents", ".cursorrules", ".github/copilot-instructions.md"];

    private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "dist", "build", "target", "venv", "__pycache__", "packages",
        // Windows' legacy junction names under Documents.
        "My Music", "My Pictures", "My Videos",
    };

    /// <summary>Documents, or the home folder when there is none (common on Linux).</summary>
    public static string DefaultRoot()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return docs != "" && Directory.Exists(docs) ? docs : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// The folder "Find projects" starts in: the configured <paramref name="configured"/> root (trimmed), or
    /// <see cref="DefaultRoot"/> when it is empty. <paramref name="isDefault"/> tells which; a configured folder
    /// that does not exist is returned as is, so the caller can report it instead of silently scanning elsewhere.
    /// </summary>
    public static string ResolveRoot(string? configured, out bool isDefault)
    {
        var root = (configured ?? "").Trim();
        isDefault = root == "";
        return isDefault ? DefaultRoot() : root;
    }

    public static List<FoundProject> Scan(string root, int maxDepth = DefaultDepth, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        var directory = new DirectoryInfo(System.IO.Path.TrimEndingDirectorySeparator(root));
        FoundProject? project;
        try { project = Inspect(directory.FullName); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return []; }
        cancel.ThrowIfCancellationRequested();
        if (project is not null) return [project];
        var found = new List<FoundProject>();
        Walk(directory, 1, maxDepth, found, cancel);
        return [.. found.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.Path, StringComparer.Ordinal)];
    }

    /// <summary>Classifies one folder; null when it is not a project.</summary>
    public static FoundProject? Inspect(string path)
    {
        var git = Directory.Exists(System.IO.Path.Combine(path, ".git")) || File.Exists(System.IO.Path.Combine(path, ".git"));
        var ai = AiMarkers.Any(m => File.Exists(System.IO.Path.Combine(path, m)) || Directory.Exists(System.IO.Path.Combine(path, m)));
        if (!git && !ai) return null;
        var orchestra = PathMapping.HasOrkester(path);
        return new FoundProject(path, System.IO.Path.GetFileName(path.TrimEnd('\\', '/')), git, ai,
            orchestra, orchestra ? OrchestraVersion(path) : null);
    }

    /// <summary>The policy file whose bold pin (<c>Shared release: **v0.9.0**</c>) names the imported release.</summary>
    public const string PinFile = ".orchestra/project.md";

    /// <summary>The imported subtree's changelog; its first released heading (<c>## 0.9.0 — date</c>) is the installed content.</summary>
    public const string ChangelogFile = ".orchestra/shared/CHANGELOG.md";

    private const int MaxPinBytes = 512 * 1024, MaxChangelogLines = 80;

    private static readonly System.Text.RegularExpressions.Regex PinPattern =
        new(@"\*\*v(\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?)\*\*", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex ReleaseHeading =
        new(@"^##\s+\[?v?(\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?)\]?(?:\s|$)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The orchestra version the project itself has installed, read from local files only: the bold
    /// <c>**vX.Y.Z**</c> pin in <see cref="PinFile"/> and the first released heading of <see cref="ChangelogFile"/>.
    /// One source is enough; when both exist they must agree. Null (version unknown) when neither names a
    /// version, the pin names several, the two disagree, or a file that exists cannot be read. Never throws.
    /// </summary>
    public static string? OrchestraVersion(string path)
    {
        string? pin, changelog;
        try
        {
            pin = ReadPin(System.IO.Path.Combine(path, PinFile), out var pinOk);
            changelog = ReadChangelog(System.IO.Path.Combine(path, ChangelogFile), out var changelogOk);
            if (!pinOk || !changelogOk) return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
        if (pin is not null && changelog is not null) return pin == changelog ? pin : null;
        return pin ?? changelog;
    }

    /// <summary>The single bold version pin; <paramref name="ok"/> is false when it is contradictory or unreadable.</summary>
    private static string? ReadPin(string file, out bool ok)
    {
        ok = true;
        if (!File.Exists(file)) return null;
        string text;
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (stream.Length > MaxPinBytes) { ok = false; return null; }
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        var versions = PinPattern.Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
        if (versions.Count > 1) ok = false;
        return versions.Count == 1 ? versions[0] : null;
    }

    /// <summary>The first released (non-"Unreleased") version heading of the shared changelog.</summary>
    private static string? ReadChangelog(string file, out bool ok)
    {
        ok = true;
        if (!File.Exists(file)) return null;
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        for (var i = 0; i < MaxChangelogLines && reader.ReadLine() is { } line; i++)
            if (ReleaseHeading.Match(line) is { Success: true } m) return m.Groups[1].Value;
        return null;
    }

    private static readonly EnumerationOptions Children = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };

    private static void Walk(DirectoryInfo dir, int depth, int maxDepth, List<FoundProject> found, CancellationToken cancel)
    {
        if (depth > maxDepth) return;
        IEnumerable<DirectoryInfo> children;
        try { children = dir.EnumerateDirectories("*", Children).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return; }
        foreach (var child in children)
        {
            cancel.ThrowIfCancellationRequested();
            // Links (junctions, symlinks) can loop or leave Documents; OneDrive placeholders are not links and are walked.
            if (child.Name.StartsWith('.') || SkipNames.Contains(child.Name) || child.LinkTarget is not null) continue;
            FoundProject? project;
            try { project = Inspect(child.FullName); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (project is not null) found.Add(project);
            else Walk(child, depth + 1, maxDepth, found, cancel);
        }
    }

    /// <summary>A path normalised for "is this folder already a project?" comparisons.</summary>
    public static string Key(string localPath)
    {
        var p = System.IO.Path.TrimEndingDirectorySeparator(localPath.Trim().Replace('/', System.IO.Path.DirectorySeparatorChar));
        return OperatingSystem.IsWindows() ? p.ToUpperInvariant() : p;
    }
}
