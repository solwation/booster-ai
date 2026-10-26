namespace BoosterAI;

/// <summary>A folder that looks like a project: a git checkout and/or an AI instruction file in its root.</summary>
public sealed record FoundProject(string Path, string Name, bool Git, bool Ai);

/// <summary>
/// "Hitta projekt": walks a few levels below Documents looking for project folders. A folder
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

    public static List<FoundProject> Scan(string root, int maxDepth = DefaultDepth, CancellationToken cancel = default)
    {
        var found = new List<FoundProject>();
        Walk(new DirectoryInfo(root), 1, maxDepth, found, cancel);
        return [.. found.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.Path, StringComparer.Ordinal)];
    }

    /// <summary>Classifies one folder; null when it is not a project.</summary>
    public static FoundProject? Inspect(string path)
    {
        var git = Directory.Exists(System.IO.Path.Combine(path, ".git")) || File.Exists(System.IO.Path.Combine(path, ".git"));
        var ai = AiMarkers.Any(m => File.Exists(System.IO.Path.Combine(path, m)) || Directory.Exists(System.IO.Path.Combine(path, m)));
        return git || ai ? new FoundProject(path, System.IO.Path.GetFileName(path.TrimEnd('\\', '/')), git, ai) : null;
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
