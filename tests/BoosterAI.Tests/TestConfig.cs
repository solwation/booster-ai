using BoosterAI;

/// <summary>
/// Keeps the test run off the user's real config folder (#282): ConfigStore and every file next to it point at a
/// per-run temporary folder, and any read or write in the real folder throws. A view-model test that reached Launch
/// without its own config once overwrote the real config.json with a fail-terminal tab and wrote limit-levels.json.
/// </summary>
internal static class TestConfig
{
    public static readonly string Root =
        Path.Combine(Path.GetTempPath(), "boosterai-test-config-" + Environment.ProcessId);

    /// <summary>The real config folder, from the app's own resolver (%APPDATA%\BoosterAI, ~/.config/BoosterAI).</summary>
    public static string RealFolder => Path.GetDirectoryName(ConfigStore.RealPath)!;

    /// <summary>The real folder's entries and write times when the run started; null when it did not exist.</summary>
    public static IReadOnlyDictionary<string, DateTime>? RealFolderAtStart { get; private set; }

    /// <summary>Called first by the test assembly's module initializer, before any test can touch ConfigStore.</summary>
    internal static void Install()
    {
        RealFolderAtStart = Snapshot(RealFolder);
        ConfigStore.UseFile(Path.Combine(Root, "config.json"));
        ConfigStore.ForbiddenFolder = RealFolder;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        };
    }

    /// <summary>Every file and folder below <paramref name="folder"/> with its last write time, or null if it does not exist.</summary>
    public static IReadOnlyDictionary<string, DateTime>? Snapshot(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        var entries = new SortedDictionary<string, DateTime>(StringComparer.Ordinal) { [""] = Directory.GetLastWriteTimeUtc(folder) };
        foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
            entries[Path.GetRelativePath(folder, path)] = File.GetLastWriteTimeUtc(path);
        return entries;
    }
}
