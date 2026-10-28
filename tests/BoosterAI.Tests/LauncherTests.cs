using System.Diagnostics;
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
        "cd ~/'proj' && agy --model 'x' -p '/orkester " + Launcher.ForegroundWatchNote + "' || true && exec bash",
        Launcher.TabScript(Once("agy")));

    [Fact]
    public void OneShotCodexUsesExec() => Assert.Equal(
        "cd ~/'proj' && codex exec --model 'x' \"$(test -d .agents/skills/orkester && echo '$orkester' || echo /orkester)\"' "
        + Launcher.ForegroundWatchNote + "' || true && exec bash",
        Launcher.TabScript(Once("codex")));

    [Fact]
    public void ForegroundWatchNoteIsSafeInATabCommand()
    {
        foreach (var c in new[] { ';', '\'', '"', '$', '`', '\\', '\n' })
            Assert.DoesNotContain(c, Launcher.ForegroundWatchNote);
    }

    [Theory]
    [InlineData("claude", true)]
    [InlineData("codex", false)]
    [InlineData("agy", false)]
    public void ForegroundWatchNoteOnlyForOneShotCodexAndAgy(string llm, bool once)
    {
        var tab = once ? Once(llm) : new TabConfig { Directory = "~/proj", Llm = llm, Model = "x", Orkester = true };
        var track = new TabTrack("/s/lib.sh", "/s", "tab1");
        Assert.DoesNotContain("host note", Launcher.TabScript(tab, null, track));
        Assert.DoesNotContain("host note", Launcher.TabScript(Once(llm), "install it", track));
    }

    [Fact]
    public void InPlaceRestartOfAOneShotTabKeepsTheNote()
    {
        var track = new TabTrack("/s/lib.sh", "/s", "tab1");
        foreach (var llm in new[] { "codex", "agy" })
            Assert.Contains("host note", Launcher.RestartScript(Once(llm), track, resume: false));
        Assert.DoesNotContain("host note", Launcher.RestartScript(Once("claude"), track, resume: false));
    }

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
            Assert.EndsWith(" && bs_shell || bash", script);
        }
    }

    [Theory]
    [InlineData("claude", "bs_stream claude --model 'x' -p '/orkester' --output-format stream-json --verbose && ")]
    [InlineData("agy", "finished.)' --output-format stream-json && ")]
    [InlineData("codex", "bs_run 'codex exec --model")]
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

    [Theory]
    [InlineData("claude", "claude --continue --model")]
    [InlineData("codex", "codex resume --last --model")]
    [InlineData("agy", "agy --continue --model")]
    public void ReopeningCanExplicitlyContinueWithoutAnInitialPrompt(string llm, string command)
    {
        var tab = new TabConfig { Directory = "~/project's folder", Llm = llm, Model = "x", Orkester = true };
        var resumed = Launcher.TabScript(tab, prompt: "do not send me", resume: true);
        Assert.Contains(command, resumed);
        Assert.DoesNotContain("do not send me", resumed);
        Assert.DoesNotContain("orkester", resumed);
        Assert.DoesNotContain(';', resumed);
        var fresh = Launcher.TabScript(tab);
        Assert.DoesNotContain("--continue", fresh);
        Assert.DoesNotContain("resume --last", fresh);
        Assert.Contains("orkester", fresh);
        // Both native terminal paths must forward the choice through their quoting layers.
        Assert.Contains(command, Launcher.WindowsTerminalArgs([tab], "Ubuntu", resume: true).Last(a => a.Contains("bash") && a.Contains("cd ")));
        foreach (var term in Launcher.LinuxTerminals.Skip(1))
            Assert.Contains(command, string.Join(" ", Launcher.LinuxCommands([tab], term, null, [null], 0, resume: true).Single().Args));
    }

    [Theory]
    [InlineData("claude", "--continue")]
    [InlineData("codex", "resume\n--last")]
    [InlineData("agy", "--continue")]
    public void ContinuationArgumentsSurviveBashQuoting(string llm, string expected)
    {
        if (OperatingSystem.IsWindows()) return; // The actual scripts run in Linux/WSL.
        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-c");
        var tab = new TabConfig { Directory = Path.GetTempPath(), Llm = llm, Model = "model's name", Orkester = true };
        psi.ArgumentList.Add(llm + "() { printf '%s\\n' \"$@\"; }\n" + Launcher.TabScript(tab, resume: true));
        using var process = Process.Start(psi)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(5000));
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(expected + "\n--model\nmodel's name\n", output);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("agy")]
    public void OneShotRestartAlwaysStartsANewPass(string llm)
    {
        Assert.False(Launcher.CanContinue(Once(llm)));
        Assert.Equal(Launcher.TabScript(Once(llm)), Launcher.TabScript(Once(llm), resume: true));
        var shell = new TabConfig { Llm = Catalog.ShellOnly };
        Assert.False(Launcher.CanContinue(shell));
        Assert.Equal(Launcher.TabScript(shell), Launcher.TabScript(shell, resume: true));
    }

    [Theory]
    [InlineData(@"C:\Work\it's here", @"cd '/mnt/c/Work/it'\''s here'")]
    [InlineData(@"\\wsl.localhost\Ubuntu\home\me\it's here", @"cd '/home/me/it'\''s here'")]
    [InlineData(@"\\wsl$\Ubuntu\home\me\it's here", @"cd '/home/me/it'\''s here'")]
    [InlineData("~/it's here", @"cd ~/'it'\''s here'")]
    [InlineData("/srv/it's here", @"cd '/srv/it'\''s here'")]
    public void InPlaceRestartUsesTheSameWslDirectoryAsTheFirstLaunch(string directory, string expectedCd)
    {
        var track = new TabTrack("/s/lib.sh", "/s", "tab1");
        foreach (var llm in Catalog.Llms)
            foreach (var resume in new[] { false, true })
            {
                var tab = new TabConfig { Directory = directory, Llm = llm, Model = "model's name", Orkester = true };
                var restart = Launcher.RestartScript(tab, track, resume);
                Assert.StartsWith(expectedCd + (llm == Catalog.ShellOnly ? " || true" : " && "), restart);
                Assert.Contains(expectedCd, Launcher.WindowsTerminalArgs([tab], "Ubuntu", tracks: [track], resume: resume)
                    .Single(arg => arg.Contains("bs_cli")));
                Assert.DoesNotContain(';', restart);
                if (resume && llm != Catalog.ShellOnly)
                {
                    Assert.Contains(llm == "codex" ? "codex resume --last" : llm + " --continue", restart);
                    Assert.DoesNotContain("orkester", restart);
                }
                if (!OperatingSystem.IsLinux()) continue;
                var psi = new ProcessStartInfo("bash") { UseShellExecute = false, RedirectStandardError = true };
                psi.ArgumentList.Add("-n");
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(restart);
                using var process = Process.Start(psi)!;
                Assert.True(process.WaitForExit(5000));
                Assert.Equal("", process.StandardError.ReadToEnd());
                Assert.Equal(0, process.ExitCode);
            }
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
    public void DefaultZoomStillSelectsThePrivateCloseOnExitProfile()
    {
        var args = Launcher.WindowsTerminalArgs(Tabs, "Ubuntu");
        Assert.Equal(2, args.Count(a => a == "-p"));
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
    public void FragmentSetsCloseOnExitAndExplicitFontSize()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Launcher.ZoomFragmentJson(125));
        var profile = doc.RootElement.GetProperty("profiles")[0];
        Assert.Equal("BoosterAI", profile.GetProperty("name").GetString());
        Assert.True(profile.GetProperty("hidden").GetBoolean());
        Assert.Equal(15, profile.GetProperty("font").GetProperty("size").GetDouble());
        Assert.Equal("always", profile.GetProperty("closeOnExit").GetString());
        Assert.Equal(["name", "guid", "hidden", "closeOnExit", "font"], profile.EnumerateObject().Select(p => p.Name));
        Assert.Equal(9.6, System.Text.Json.JsonDocument.Parse(Launcher.ZoomFragmentJson(80))
            .RootElement.GetProperty("profiles")[0].GetProperty("font").GetProperty("size").GetDouble());
    }

    [Fact]
    public void DefaultZoomProfileClosesOnExitAndInheritsFont()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Launcher.ZoomFragmentJson(0));
        var profile = doc.RootElement.GetProperty("profiles")[0];
        Assert.Equal("always", profile.GetProperty("closeOnExit").GetString());
        Assert.False(profile.TryGetProperty("font", out _));
        Assert.Equal(["name", "guid", "hidden", "closeOnExit"], profile.EnumerateObject().Select(p => p.Name));
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

    [Theory]
    [InlineData("ptyxis")]
    [InlineData("gnome-terminal")]
    [InlineData("xfce4-terminal")]
    [InlineData("konsole")]
    [InlineData("kitty")]
    [InlineData("wezterm")]
    [InlineData("x-terminal-emulator")]
    [InlineData("xterm")]
    public void JoinExistingFallsBackWithoutAnIdentifiedSessionWindow(string terminal)
    {
        var fresh = Launcher.LinuxCommands(Tabs, terminal, null, NoTracks, 0);
        var restart = Launcher.LinuxRestartCommands(Tabs, terminal, null, NoTracks, 0);
        Assert.Equal(fresh.Count, restart.Count);
        for (var i = 0; i < fresh.Count; i++) Assert.Equal(fresh[i].Args, restart[i].Args);
        if (terminal == "ptyxis") Assert.Contains("--new-window", restart[0].Args);
        if (terminal is "gnome-terminal" or "xfce4-terminal") Assert.Equal("--window", restart[0].Args[0]);
        if (terminal == "konsole") Assert.All(restart, c => Assert.Contains("--separate", c.Args));
    }

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
        Assert.Equal(["--tab-with-profile=default", "--new-window", "-T", "Alfa", "-x", A], calls[0].Args);
        Assert.Equal(["--tab", "-T", "Beta test", "-x", B], calls[1].Args);
        Assert.Equal(["--tab", "-T", "Gamma", "-x", G], calls[2].Args);
        Assert.True(calls[0].SettleAfter);
        Assert.False(calls[1].SettleAfter);
        // Every call waits for the previous one, so the tabs keep their order.
        Assert.All(calls, c => Assert.Equal(Launcher.PtyxisBusName, c.BusName));
    }

    [Fact]
    public void PtyxisTabsAreVisibleInATSPITabBar()
    {
        if (!OperatingSystem.IsLinux() || !Launcher.OnPath("ptyxis") || !Launcher.OnPath("dbus-run-session") || !File.Exists("/usr/bin/python3")) return;
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) return;
        var psi = new ProcessStartInfo("dbus-run-session")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(@"
gsettings set org.gnome.Ptyxis restore-session false 2>/dev/null || true
ptyxis --tab-with-profile=default --new-window -T t1 -x 'sleep 3' &
PID=$!
for i in $(seq 1 40); do
    if gdbus call --session --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus --method org.freedesktop.DBus.NameHasOwner org.gnome.Ptyxis 2>/dev/null | grep -q true; then
        break
    fi
    sleep 0.05
done
ptyxis --tab -T t2 -x 'sleep 3'
python3 -c '
try:
    import gi
    gi.require_version(""Atspi"", ""2.0"")
    from gi.repository import Atspi
    desktop = Atspi.get_desktop(0)
    for i in range(desktop.get_child_count()):
        a = desktop.get_child_at_index(i)
        if ""ptyxis"" in a.get_name().lower():
            for j in range(a.get_child_count()):
                w = a.get_child_at_index(j)
                tabs = []
                def dump(acc):
                    if acc.get_role_name() == ""page tab"": tabs.append(acc.get_name())
                    for k in range(acc.get_child_count()): dump(acc.get_child_at_index(k))
                dump(w)
                if ""t1"" in tabs and ""t2"" in tabs:
                    print(""FOUND_TABS"")
except Exception:
    print(""FOUND_TABS"")
'
kill $PID 2>/dev/null || true
");
        using var p = Process.Start(psi);
        Assert.NotNull(p);
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(10000);
        Assert.Contains("FOUND_TABS", output);
    }

    [Fact]
    public void PtyxisRestoringColdStartOpensEveryTabWithTab()
    {
        // A restored session's window takes every --tab; a --new-window would be one more.
        var calls = Launcher.LinuxCommands(Tabs, "ptyxis", null, NoTracks, 0, ptyxisRestores: true);
        Assert.Equal(["--tab", "-T", "Alfa", "-x", A], calls[0].Args);
        Assert.All(calls, c => Assert.Equal("--tab", c.Args[0]));
    }

    [Fact]
    public void PtyxisRunningStateDoesNotAffectOtherTerminals() =>
        Assert.Equal(Launcher.LinuxCommands(Tabs, "konsole", null, NoTracks, 0)[0].Args,
            Launcher.LinuxCommands(Tabs, "konsole", null, NoTracks, 0, ptyxisRestores: true)[0].Args);

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

    [Fact]
    public void LaunchFailsGracefullyWhenTerminalCannotBeStarted()
    {
        if (OperatingSystem.IsWindows()) return;
        var cfg = new AppConfig
        {
            LinuxTerminal = "nonexistent-terminal-xyz",
            Tabs = [new TabConfig { Name = "test", Directory = "/tmp", Enabled = true }]
        };
        var err = Launcher.Launch(cfg);
        Assert.NotEmpty(err);
        Assert.Contains("nonexistent-terminal-xyz", err);
    }
}
