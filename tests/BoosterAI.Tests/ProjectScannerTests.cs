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
    public void ImportListLeavesOutExistingProjects()
    {
        Dir("old", ".git");
        Dir("new", ".git");
        var vm = new ImportViewModel([Path.Combine(_root, "old") + Path.DirectorySeparatorChar]);

        vm.ScanAsync(_root).GetAwaiter().GetResult();

        Assert.Equal(["new"], vm.Items.Select(i => i.Name));
        Assert.True(vm.AllSelected);
        vm.AllSelected = false;
        Assert.False(vm.CanImport);
    }
}
