using BoosterAI;
using Xunit;

public sealed class ProjectScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("boosterai-scan-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Dir(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    private void Touch(params string[] parts)
    {
        Dir(parts[..^1]);
        File.WriteAllText(Path.Combine([_root, .. parts]), "");
    }

    [Fact]
    public void FindsGitAndAiProjectsSorted()
    {
        Dir("webshop", ".git");
        Touch("notes", "research", "CLAUDE.md");
        Touch("both", "AGENTS.md");
        Dir("both", ".git");
        Dir("plain", "docs");

        var found = ProjectScanner.Scan(_root);

        Assert.Equal(["both", "research", "webshop"], found.Select(f => f.Name));
        Assert.Equal(new FoundProject(Path.Combine(_root, "both"), "both", Git: true, Ai: true), found[0]);
        Assert.False(found[1].Git);
        Assert.True(found[1].Ai);
        Assert.False(found[2].Ai);
    }

    [Fact]
    public void GitFileCountsForWorktrees()
    {
        Touch("wt", ".git");
        Assert.True(Assert.Single(ProjectScanner.Scan(_root)).Git);
    }

    [Fact]
    public void DoesNotDescendIntoProjectsHiddenOrDependencyFolders()
    {
        Dir("app", ".git");
        Dir("app", "vendor", "lib", ".git");
        Dir(".cache", "x", ".git");
        Dir("node_modules", "pkg", ".git");

        Assert.Equal(["app"], ProjectScanner.Scan(_root).Select(f => f.Name));
    }

    [Fact]
    public void StopsAtMaxDepth()
    {
        Dir("a", "b", "c", ".git");
        Dir("a", "b", "c2", "d", ".git");

        Assert.Equal(["c"], ProjectScanner.Scan(_root, maxDepth: 3).Select(f => f.Name));
        Assert.Empty(ProjectScanner.Scan(_root, maxDepth: 2));
    }

    [Fact]
    public void ResolveRootUsesConfiguredFolderOrTheDefault()
    {
        Assert.Equal(_root, ProjectScanner.ResolveRoot("  " + _root + " ", out var isDefault));
        Assert.False(isDefault);

        foreach (var empty in new string?[] { "", "   ", null })
        {
            Assert.Equal(ProjectScanner.DefaultRoot(), ProjectScanner.ResolveRoot(empty, out isDefault));
            Assert.True(isDefault);
        }

        // A missing folder is kept as configured, never swapped for the default.
        var missing = Path.Combine(_root, "nope");
        Assert.Equal(missing, ProjectScanner.ResolveRoot(missing, out isDefault));
        Assert.False(isDefault);
        Assert.Equal("", new AppConfig().ProjectSearchRoot);
    }

    [Fact]
    public async Task ScanFromConfiguredRootFindsItsProjects()
    {
        Dir("shop", ".git");
        var vm = new ImportViewModel([]);

        await vm.ScanAsync(ProjectScanner.ResolveRoot(_root, out _));

        Assert.Equal(["shop"], vm.Items.Select(i => i.Name));
        Assert.False(vm.MessageIsError);
    }

    [Fact]
    public async Task NonexistentRootShowsAnErrorInsteadOfAnEmptyList()
    {
        var missing = Path.Combine(_root, "nope");
        var vm = new ImportViewModel([]);

        await vm.ScanAsync(missing);

        Assert.Empty(vm.Items);
        Assert.True(vm.MessageIsError);
        Assert.False(vm.IsScanning);
        Assert.Equal(Loc.Format("import.rootMissing", missing), vm.Message);
        Assert.Contains(missing, vm.Message);
    }

    [Fact]
    public void SearchRootSurvivesConfigRoundTripAndMerge()
    {
        var cfg = new AppConfig { ProjectSearchRoot = _root };
        var back = ConfigStore.Copy(cfg);
        Assert.Equal(_root, back.ProjectSearchRoot);

        var mine = new AppConfig();
        Assert.True(ConfigMerge.TakeOthers(mine, new AppConfig(), cfg));
        Assert.Equal(_root, mine.ProjectSearchRoot);
    }

    [Fact]
    public async Task ImportListLeavesOutExistingProjects()
    {
        Dir("old", ".git");
        Dir("new", ".git");
        var vm = new ImportViewModel([Path.Combine(_root, "old") + Path.DirectorySeparatorChar]);

        await vm.ScanAsync(_root);

        Assert.Equal(["new"], vm.Items.Select(i => i.Name));
        Assert.False(vm.AllSelected);
        Assert.False(vm.CanImport);
        vm.AllSelected = true;
        Assert.True(vm.CanImport);
        vm.AllSelected = false;
        Assert.False(vm.CanImport);
    }

    private void Write(string text, params string[] parts)
    {
        Dir(parts[..^1]);
        File.WriteAllText(Path.Combine([_root, .. parts]), text);
    }

    private const string Changelog = "# Changelog\n\n## Unreleased\n\n- next\n\n## 0.9.0 — 2026-11-06\n\n## 0.8.2 — 2026-11-05\n";

    [Fact]
    public async Task FoundProjectsStartUntickedOnFirstAndNewSearch()
    {
        Dir("a", "one", ".git");
        Dir("a", "two", ".git");
        Dir("b", "three", ".git");
        var vm = new ImportViewModel([]);

        await vm.ScanAsync(Path.Combine(_root, "a"));
        Assert.Equal(["one", "two"], vm.Items.Select(i => i.Name));
        Assert.All(vm.Items, i => Assert.False(i.Selected));
        Assert.False(vm.AllSelected);
        Assert.False(vm.CanImport);
        Assert.Equal(Loc.Get("import.none"), vm.ImportLabel);
        Assert.Empty(vm.Selected);

        // A manual choice enables Import and makes the header mixed.
        vm.Items[1].Selected = true;
        Assert.Null(vm.AllSelected);
        Assert.True(vm.CanImport);
        Assert.Equal(["two"], vm.Selected.Select(p => p.Name));

        // The same root searched again and a new root both start unticked.
        await vm.ScanAsync(Path.Combine(_root, "a"));
        Assert.All(vm.Items, i => Assert.False(i.Selected));
        Assert.False(vm.CanImport);
        vm.AllSelected = true;
        await vm.ScanAsync(Path.Combine(_root, "b"));
        Assert.Equal(["three"], vm.Items.Select(i => i.Name));
        Assert.False(Assert.Single(vm.Items).Selected);
        Assert.False(vm.CanImport);
        Assert.Empty(vm.Selected);
    }

    [Fact]
    public async Task EmptySearchHasNothingToImport()
    {
        Dir("plain", "docs");
        var vm = new ImportViewModel([]);
        await vm.ScanAsync(_root);
        Assert.False(vm.HasItems);
        Assert.False(vm.AllSelected);
        Assert.False(vm.CanImport);
    }

    [Fact]
    public void ProjectWithoutOrchestra()
    {
        Dir("plain", ".git");
        Touch("plain", "AGENTS.md");
        var p = Assert.Single(ProjectScanner.Scan(_root));
        Assert.False(p.Orchestra);
        Assert.Null(p.OrchestraVersion);
        Assert.Equal(Loc.Get("import.orchestra.none"), new ImportItem(p, () => { }).Orchestra);
        Assert.Equal("git · AI", new ImportItem(p, () => { }).Kind);
    }

    [Fact]
    public void PinAndChangelogThatAgreeGiveTheVersion()
    {
        Dir("app", ".git");
        Write("Shared release: **v0.9.0**, commit **070ebf43e25c2bc7885e387712edd18a2e97da1c**, imported as a\n", "app", ".orchestra", "project.md");
        Write(Changelog, "app", ".orchestra", "shared", "CHANGELOG.md");

        var p = Assert.Single(ProjectScanner.Scan(_root));
        Assert.True(p.Orchestra);
        Assert.Equal("0.9.0", p.OrchestraVersion);
        Assert.Equal(Loc.Format("import.orchestra.version", "0.9.0"), new ImportItem(p, () => { }).Orchestra);
    }

    [Fact]
    public void EitherSourceAloneGivesTheVersion()
    {
        Write("- Version: **v0.8.2**\n", "pin", ".orchestra", "project.md");
        Write(Changelog, "log", ".orchestra", "shared", "CHANGELOG.md");
        Assert.Equal("0.8.2", ProjectScanner.OrchestraVersion(Path.Combine(_root, "pin")));
        Assert.Equal("0.9.0", ProjectScanner.OrchestraVersion(Path.Combine(_root, "log")));
    }

    [Fact]
    public void OrchestraWithoutVersionMetadataIsUnknown()
    {
        Touch("cmd", ".claude", "commands", "orkester.md");
        Dir("skill", ".agents", "skills", "orkester");
        Write("# Policy\nWe use the shared orchestra (see v0.7.0 notes).\n", "nopin", ".orchestra", "project.md");
        Write("# Changelog\n\n## Unreleased\n", "nopin", ".orchestra", "shared", "CHANGELOG.md");

        var found = ProjectScanner.Scan(_root);
        Assert.Equal(["cmd", "nopin", "skill"], found.Select(f => f.Name));
        Assert.All(found, p => Assert.True(p.Orchestra));
        Assert.All(found, p => Assert.Null(p.OrchestraVersion));
        Assert.Equal(Loc.Get("import.orchestra.unknown"), new ImportItem(found[0], () => { }).Orchestra);
    }

    [Fact]
    public void ContradictoryMetadataIsUnknown()
    {
        Write("Shared release: **v0.9.0**\n", "mismatch", ".orchestra", "project.md");
        Write("# Changelog\n\n## 0.8.2 — 2026-11-05\n", "mismatch", ".orchestra", "shared", "CHANGELOG.md");
        Write("Shared release: **v0.9.0**, upgraded from **v0.8.2**\n", "twopins", ".orchestra", "project.md");

        Assert.Null(ProjectScanner.OrchestraVersion(Path.Combine(_root, "mismatch")));
        Assert.Null(ProjectScanner.OrchestraVersion(Path.Combine(_root, "twopins")));
    }

    [Fact]
    public void UnreadableMetadataIsUnknownAndDoesNotFailTheScan()
    {
        Dir("locked", ".git");
        Write(Changelog, "locked", ".orchestra", "shared", "CHANGELOG.md");
        // A directory where the pin file should be cannot be read as a file.
        Dir("locked", ".orchestra", "project.md", "x");
        Dir("other", ".git");

        var found = ProjectScanner.Scan(_root);
        Assert.Equal(["locked", "other"], found.Select(f => f.Name));
        Assert.True(found[0].Orchestra);
        Assert.Equal("0.9.0", found[0].OrchestraVersion);

        if (OperatingSystem.IsWindows()) return;
        var pin = Path.Combine(_root, "locked", ".orchestra", "project.md");
        Directory.Delete(pin, recursive: true);
        File.WriteAllText(pin, "Shared release: **v0.9.0**\n");
        File.SetUnixFileMode(pin, UnixFileMode.None);
        try
        {
            if (CanRead(pin)) return; // running as root: permissions are not enforced
            Assert.Null(ProjectScanner.OrchestraVersion(Path.Combine(_root, "locked")));
            Assert.Equal(["locked", "other"], ProjectScanner.Scan(_root).Select(f => f.Name));
        }
        finally { File.SetUnixFileMode(pin, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    private static bool CanRead(string file)
    {
        try { File.ReadAllBytes(file); return true; }
        catch (UnauthorizedAccessException) { return false; }
    }

    [Fact]
    public void DetectionDoesNotChangeTheProject()
    {
        Dir("app", ".git");
        Write("Shared release: **v0.9.0**\n", "app", ".orchestra", "project.md");
        Write(Changelog, "app", ".orchestra", "shared", "CHANGELOG.md");
        string Snapshot() => string.Join("\n", Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(f => f + "|" + (File.Exists(f) ? File.GetLastWriteTimeUtc(f).Ticks + ":" + new FileInfo(f).Length : "d")));
        var before = Snapshot();

        ProjectScanner.Scan(_root);

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void ScreenshotSampleShowsEveryOrchestraStateUnticked()
    {
        var vm = new ImportViewModel([]);
        vm.ShowSample("/home/me/Documents");
        Assert.All(vm.Items, i => Assert.False(i.Selected));
        Assert.Equal(
            [Loc.Format("import.orchestra.version", "0.9.0"), Loc.Get("import.orchestra.none"), Loc.Get("import.orchestra.unknown"), Loc.Format("import.orchestra.version", "0.8.2")],
            vm.Items.Select(i => i.Orchestra));
    }
}
