using BoosterAI;
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

    private static TabConfig Once(string llm, bool orkester = true, string model = "x") =>
        new() { Directory = "~/proj", Llm = llm, Model = model, Orkester = orkester, NonInteractive = true };

    [Fact]
    public void OneShotClaudeUsesPrint() => Assert.Equal(
        "cd ~/'proj' && claude --model 'x' -p '/orkester' || true && exec bash",
        Launcher.TabScript(Once("claude")));

    [Fact]
    public void OneShotAgyUsesPrintInsteadOfInteractive() => Assert.Equal(
        "cd ~/'proj' && agy --model 'x' -p '/orkester' || true && exec bash",
        Launcher.TabScript(Once("agy")));

    [Fact]
    public void OneShotCodexUsesExec() => Assert.Equal(
        "cd ~/'proj' && codex exec --model 'x' $(test -d .agents/skills/orkester && echo '$orkester' || echo /orkester) || true && exec bash",
        Launcher.TabScript(Once("codex")));

    [Fact]
    public void OneShotWithoutOrkesterIsIgnored()
    {
        foreach (var llm in new[] { "claude", "codex", "agy" })
            Assert.Equal(Launcher.TabScript(new TabConfig { Directory = "~/proj", Llm = llm, Model = "x" }),
                Launcher.TabScript(Once(llm, orkester: false)));
    }

    [Fact]
    public void OneShotStaysInteractiveForTheInstallPrompt()
    {
        var script = Launcher.TabScript(Once("claude"), "it's a test");
        Assert.DoesNotContain("-p ", script);
        Assert.DoesNotContain("exec ", script.Replace("exec bash", ""));
    }

    [Fact]
    public void OneShotWithTrackingKeepsHooksAndShell()
    {
        var track = new TabTrack("/s/lib.sh", "/s", "tab1");
        foreach (var llm in new[] { "claude", "codex", "agy" })
        {
            var script = Launcher.TabScript(Once(llm), null, track);
            Assert.DoesNotContain(';', script);
            Assert.Contains("(bs_cli 2>/dev/null || true)", script);
            Assert.Contains("(bs_done 0 2>/dev/null || true)", script);
            Assert.Contains("(bs_done $? 2>/dev/null || true)", script);
            Assert.EndsWith(" && bash", script);
        }
    }

    [Theory]
    [InlineData("claude", "bs_stream claude --model 'x' -p '/orkester' --output-format stream-json --verbose && ")]
    [InlineData("agy", "bs_stream agy --model 'x' -p '/orkester' --output-format stream-json && ")]
    [InlineData("codex", "codex exec --model 'x' $(test -d .agents/skills/orkester && echo '$orkester' || echo /orkester) && ")]
    public void OneShotWithTrackingShowsLiveProgress(string llm, string expected)
    {
        var script = Launcher.TabScript(Once(llm), null, new TabTrack("/s/lib.sh", "/s", "tab1"));
        Assert.Contains(expected, script);
        Assert.DoesNotContain(';', script);
    }

    [Fact]
    public void OneShotWithoutTrackingStaysPlain()
    {
        Assert.DoesNotContain("bs_stream", Launcher.TabScript(Once("claude")));
        Assert.DoesNotContain("stream-json", Launcher.TabScript(Once("agy")));
    }

    [Fact]
    public void RejectsUnsafeModelAndDirectory()
    {
        Assert.NotEqual("", Launcher.Validate([new TabConfig { Model = "x; rm -rf ~" }]));
        Assert.NotEqual("", Launcher.Validate([new TabConfig { Directory = "~/a;b" }]));
        Assert.Equal("", Launcher.Validate([new TabConfig { Directory = "~/a b" }]));
    }
}

public class ZoomTests
{
    private static readonly TabConfig[] Tabs = [new() { Name = "a", Directory = "~/a" }, new() { Name = "b", Directory = "~/b" }];

    [Fact]
    public void DefaultZoomLeavesWindowsTerminalArgsUnchanged()
    {
        var args = Launcher.WindowsTerminalArgs(Tabs, "Ubuntu");
        Assert.DoesNotContain("-p", args);
        Assert.Equal(args, Launcher.WindowsTerminalArgs(Tabs, "Ubuntu", null, zoomPercent: 0));
    }

    [Fact]
    public void ZoomSelectsTheBoosterAIProfileOnEveryTab()
    {
        var args = Launcher.WindowsTerminalArgs(Tabs, "Ubuntu", null, zoomPercent: 125);
        Assert.Equal(2, args.Count(a => a == "-p"));
        for (var i = 0; i < args.Count; i++)
            if (args[i] == "new-tab") Assert.Equal(["new-tab", "-p", "BoosterAI", "--title"], args.Skip(i).Take(4));
        Assert.DoesNotContain(args, a => a.Contains("125"));
    }

    [Fact]
    public void FragmentSetsOnlyTheFontSize()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Launcher.ZoomFragmentJson(125));
        var profile = doc.RootElement.GetProperty("profiles")[0];
        Assert.Equal("BoosterAI", profile.GetProperty("name").GetString());
        Assert.True(profile.GetProperty("hidden").GetBoolean());
        Assert.Equal(15, profile.GetProperty("font").GetProperty("size").GetDouble());
        Assert.Equal(["name", "guid", "hidden", "font"], profile.EnumerateObject().Select(p => p.Name));
        Assert.Equal(9.6, System.Text.Json.JsonDocument.Parse(Launcher.ZoomFragmentJson(80))
            .RootElement.GetProperty("profiles")[0].GetProperty("font").GetProperty("size").GetDouble());
    }

    [Fact]
    public void NudgeTogglesOneTrailingNewline()
    {
        var original = "{\n  \"profiles\": {}\n}"u8.ToArray();
        var once = Launcher.NudgedSettings(original);
        Assert.Equal([.. original, (byte)'\n'], once);
        var twice = Launcher.NudgedSettings(once);
        Assert.Equal([.. once, (byte)'\n'], twice);
        Assert.Equal(once, Launcher.NudgedSettings(twice));
    }

    [Fact]
    public void LinuxZoomArgsPerTerminal()
    {
        Assert.Empty(Launcher.LinuxZoomArgs("gnome-terminal", 0));
        Assert.Equal(["--zoom=1.25"], Launcher.LinuxZoomArgs("gnome-terminal", 125));
        Assert.Equal(["-o", "font_size=13.75"], Launcher.LinuxZoomArgs("kitty", 125));
        Assert.Equal(["--config", "font_size=15"], Launcher.LinuxZoomArgs("wezterm", 125));
        Assert.Empty(Launcher.LinuxZoomArgs("konsole", 125));
    }
}

public class PathMappingTests
{
    [Fact]
    public void WindowsFoldersBecomeWslPaths()
    {
        Assert.Equal("/mnt/c/Users/me/Documents/web shop", PathMapping.ToWslPath(@"C:\Users\me\Documents\web shop"));
        Assert.Equal("/mnt/d", PathMapping.ToWslPath(@"D:\"));
        Assert.Equal("/home/me/proj", PathMapping.ToWslPath(@"\\wsl.localhost\Ubuntu\home\me\proj"));
        Assert.Equal("/home/me", PathMapping.ToWslPath(@"\\wsl$\Ubuntu\home\me\"));
        Assert.Equal("~/proj", PathMapping.ToWslPath("~/proj"));
        Assert.Equal("/srv/x", PathMapping.ToWslPath("/srv/x"));
        Assert.True(PathMapping.IsWindowsPath(@"C:\x"));
        Assert.False(PathMapping.IsWindowsPath("~/x"));
    }

    [Fact]
    public void WindowsTerminalTabCdsIntoTheWslForm()
    {
        var args = Launcher.WindowsTerminalArgs([new TabConfig { Directory = @"C:\Users\me\Documents\web shop", Llm = "claude" }], "Ubuntu");
        Assert.StartsWith("cd '/mnt/c/Users/me/Documents/web shop' && claude", args[^5]);
    }

    [Fact]
    public void WindowsKeepsPickedFolders()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(@"C:\Users\me\Documents", PathMapping.ToTabPath(@"C:\Users\me\Documents\"));
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\me\proj", PathMapping.ToTabPath(@"\\wsl.localhost\Ubuntu\home\me\proj"));
        Assert.Equal(@"C:\Users\me\x", PathMapping.ToLocalPath(@"C:\Users\me\x", "Ubuntu", null));
        Assert.Equal(@"C:\Users\me\x", PathMapping.StartFolder(@"C:\Users\me\x", "Ubuntu"));
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), PathMapping.StartFolder("", "Ubuntu"));
        // Linux paths still resolve through the WSL share.
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\me\proj", PathMapping.ToLocalPath("~/proj", "Ubuntu", "/home/me"));
        Assert.Equal(@"D:\work", PathMapping.ToLocalPath("/mnt/d/work", "Ubuntu", "/home/me"));
        Assert.Null(PathMapping.ToLocalPath("~/proj", "Ubuntu", null));
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\me\proj", PathMapping.StartFolder("/home/me/proj", "Ubuntu"));
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
        Assert.Equal(home + "/proj", PathMapping.ToLocalPath("~/proj", "Ubuntu", null));
    }
}

public class RunOnceToggleTests
{
    private static TabViewModel Tab(bool orkester, bool once, bool? hasOrkester)
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig { Llm = "claude", Orkester = orkester, NonInteractive = once }] });
        var tab = vm.Tabs[0];
        tab.HasOrkester = hasOrkester;
        return tab;
    }

    [Fact]
    public void EnabledOnlyWithOrchestraAvailableAndOn()
    {
        Assert.True(Tab(true, true, true).CanNonInteractive);
        Assert.False(Tab(false, true, true).CanNonInteractive);
        Assert.False(Tab(true, true, false).CanNonInteractive);
        Assert.False(Tab(true, true, null).CanNonInteractive);
    }

    [Fact]
    public void StoredChoiceLooksOffWhileItCannotApply()
    {
        Assert.True(Tab(true, true, true).OnceChecked);
        Assert.False(Tab(false, true, true).OnceChecked);
        var t = Tab(false, true, true);
        t.Orkester = true;
        Assert.True(t.OnceChecked);
    }

    [Fact]
    public void OnlyShownWhereTheOrchestraToggleIs()
    {
        Assert.True(Tab(false, false, true).CanOrkester);
        Assert.False(Tab(false, false, false).CanOrkester);
    }

    [Fact]
    public void LaunchIgnoresOneShotWithoutOrchestra()
    {
        var cfg = Tab(true, true, false).ToConfig(forLaunch: true);
        Assert.False(cfg.Orkester);
        Assert.DoesNotContain("-p ", Launcher.TabScript(cfg));
    }
}

public class LinuxCommandsTests
{
    private static readonly TabConfig[] Tabs =
    [
        new() { Name = "Alfa", Directory = "~/alfa", Llm = Catalog.ShellOnly },
        new() { Name = "Beta test", Directory = "~/beta", Llm = Catalog.ShellOnly },
        new() { Name = "Gamma", Directory = "/srv/gamma", Llm = Catalog.ShellOnly },
    ];
    private static readonly TabTrack?[] NoTracks = [null, null, null];

    private const string A = "bash -lic 'cd ~/'\\''alfa'\\'' || true && exec bash'";
    private const string B = "bash -lic 'cd ~/'\\''beta'\\'' || true && exec bash'";
    private const string G = "bash -lic 'cd '\\''/srv/gamma'\\'' || true && exec bash'";

    [Fact]
    public void GnomeTerminalIsOneCallWithOneWindowAndThreeTabs()
    {
        var calls = Launcher.LinuxCommands(Tabs, "gnome-terminal", null, NoTracks, 125);
        var call = Assert.Single(calls);
        Assert.Equal("gnome-terminal", call.Exe);
        Assert.Equal(
        [
            "--zoom=1.25",
            "--window", "--title=Alfa", "--command=" + A,
            "--tab", "--title=Beta test", "--command=" + B,
            "--tab", "--title=Gamma", "--command=" + G,
        ], call.Args);
    }

    [Fact]
    public void GnomeTerminalWithoutZoomHasNoZoomArg()
    {
        var call = Assert.Single(Launcher.LinuxCommands(Tabs, "gnome-terminal", null, NoTracks, 0));
        Assert.Equal("--window", call.Args[0]);
    }

    [Fact]
    public void PtyxisOpensWindowThenTabsInTheActiveWindow()
    {
        var calls = Launcher.LinuxCommands(Tabs, "ptyxis", null, NoTracks, 125);
        Assert.Equal(3, calls.Count);
        Assert.All(calls, c => Assert.Equal("ptyxis", c.Exe));
        Assert.Equal(["--new-window", "-T", "Alfa", "-x", A], calls[0].Args);
        Assert.Equal(["--tab", "-T", "Beta test", "-x", B], calls[1].Args);
        Assert.Equal(["--tab", "-T", "Gamma", "-x", G], calls[2].Args);
        Assert.True(calls[0].SettleAfter);
        Assert.False(calls[1].SettleAfter);
        Assert.Equal(Launcher.PtyxisBusName, calls[0].BusName);
        Assert.Null(calls[1].BusName);
    }

    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200), Tick = TimeSpan.FromMilliseconds(1);

    [Fact]
    public void ColdStartIsReadyOnceTheNewInstanceOwnsTheBusName()
    {
        var polls = 0;
        Assert.True(Launcher.WaitForInstance(() => false, () => ++polls >= 3, ownedBefore: false, Short, Tick));
        Assert.Equal(3, polls);
    }

    [Fact]
    public void RunningInstanceIsReadyOnlyWhenTheRemoteCallExits()
    {
        var polls = 0;
        // The name is owned all along (the running instance), so only the remote process exiting counts.
        Assert.True(Launcher.WaitForInstance(() => ++polls >= 4, () => true, ownedBefore: true, Short, Tick));
        Assert.Equal(4, polls);
    }

    [Fact]
    public void WaitForInstanceGivesUpAfterTheTimeout() =>
        Assert.False(Launcher.WaitForInstance(() => false, () => false, ownedBefore: false, TimeSpan.FromMilliseconds(30), Tick));

    [Fact]
    public void Xfce4TerminalIsOneCallToo()
    {
        var call = Assert.Single(Launcher.LinuxCommands(Tabs, "xfce4-terminal", null, NoTracks, 0));
        Assert.Equal(["--window", "--title=Alfa", "--command=" + A, "--tab", "--title=Beta test", "--command=" + B,
            "--tab", "--title=Gamma", "--command=" + G], call.Args);
    }

    [Fact]
    public void ApostropheInDirectoryStaysQuoted()
    {
        var call = Assert.Single(Launcher.LinuxCommands([new() { Name = "x", Directory = "/srv/it's", Llm = Catalog.ShellOnly }],
            "gnome-terminal", null, [null], 0));
        Assert.Equal("--command=bash -lic 'cd '\\''/srv/it'\\''\\'\\'''\\''s'\\'' || true && exec bash'", call.Args[2]);
    }

    [Fact]
    public void PtyxisIsTriedFirstByAuto() => Assert.Equal("ptyxis", Launcher.LinuxTerminals[1]);
}
