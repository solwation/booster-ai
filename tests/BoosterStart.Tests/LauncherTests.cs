using BoosterStart;
using Xunit;

public class LauncherTests
{
    [Fact]
    public void ClaudeWithModelAndOrkester() => Assert.Equal(
        "cd ~/'proj' && claude --model 'claude-opus-5-5' '/orkester' || true && exec bash",
        Launcher.TabScript(new TabConfig { Directory = "~/proj", Llm = "claude", Model = "claude-opus-5-5", Orkester = true }));

    [Fact]
    public void ShellOnlyQuotesApostrophes() => Assert.Equal(
        @"cd '/srv/it'\''s' || true && exec bash",
        Launcher.TabScript(new TabConfig { Directory = "/srv/it's", Llm = Catalog.ShellOnly }));

    [Fact]
    public void ScriptsNeverContainSemicolons()
    {
        foreach (var llm in Catalog.Llms)
            Assert.DoesNotContain(';', Launcher.TabScript(new TabConfig { Llm = llm, Orkester = true, Model = "x" }));
    }

    [Fact]
    public void InstallPromptSurvivesWindowsTerminal()
    {
        Assert.DoesNotContain(';', Launcher.InstallOrkesterPrompt);
        Assert.Contains("-i '", Launcher.TabScript(new TabConfig { Llm = "agy" }, "hej"));
        Assert.EndsWith("claude 'hej' || true && exec bash", Launcher.TabScript(new TabConfig { Llm = "claude", Orkester = true }, "hej"));
    }

    [Fact]
    public void RejectsUnsafeModelAndDirectory()
    {
        Assert.NotEqual("", Launcher.Validate([new TabConfig { Model = "x; rm -rf ~" }]));
        Assert.NotEqual("", Launcher.Validate([new TabConfig { Directory = "~/a;b" }]));
        Assert.Equal("", Launcher.Validate([new TabConfig { Directory = "~/a b" }]));
    }
}

public class PathMappingTests
{
    [Fact]
    public void WindowsWslAndDrivePaths()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal("/home/me/proj", PathMapping.ToTabPath(@"\\wsl.localhost\Ubuntu\home\me\proj"));
        Assert.Equal("/home/me", PathMapping.ToTabPath(@"\\wsl$\Ubuntu\home\me\"));
        Assert.Equal("/mnt/c/Users/me/Documents", PathMapping.ToTabPath(@"C:\Users\me\Documents"));
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\me\proj", PathMapping.StartFolder("/home/me/proj", "Ubuntu"));
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home", PathMapping.StartFolder("~/proj", "Ubuntu"));
        Assert.Equal(@"C:\Users\me", PathMapping.StartFolder("/mnt/c/Users/me", "Ubuntu"));
    }

    [Fact]
    public void LinuxHomeBecomesTilde()
    {
        if (OperatingSystem.IsWindows()) return;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal("~/proj", PathMapping.ToTabPath(home + "/proj"));
        Assert.Equal("~", PathMapping.ToTabPath(home));
        Assert.Equal("/srv/x", PathMapping.ToTabPath("/srv/x/"));
        Assert.Equal(home + "/proj", PathMapping.StartFolder("~/proj", "Ubuntu"));
    }
}
