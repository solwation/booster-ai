using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

[Collection(ConfigFileCollection.Name)]
public class InstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "boosterai-installer-" + Guid.NewGuid().ToString("N"));

    public InstallerTests() => Loc.Use("en");

    public void Dispose()
    {
        Loc.Use("en");
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static InstallPlan SamplePlan(string source = @"C:\Users\me\Downloads\BoosterAI-Setup-1.2.3-win-x64.exe") =>
        Installer.Plan(@"C:\Users\me\AppData\Local", @"C:\Users\me\AppData\Roaming\Microsoft\Windows\Start Menu\Programs",
            @"C:\Users\me\Desktop", source, @"C:\Users\me\AppData\Roaming\BoosterAI\config.json", new AppVersion(1, 2, 3, ""), 5 * 1024 * 1024);

    // ---- Setup mode detection ----

    [Theory]
    [InlineData(@"C:\Users\me\Downloads\BoosterAI-Setup-0.34.0-win-x64.exe")]
    [InlineData(@"C:\Users\me\Downloads\boosterai-setup-0.34.0-win-x64 (1).exe")]
    [InlineData(@"C:\Users\me\Downloads\BOOSTERAI-SETUP.exe")]
    [InlineData("/home/me/Downloads/BoosterAI-Setup-0.34.0-win-x64.exe")]
    public void SetupNamedExeStartsTheWizard(string path) =>
        Assert.Equal(SetupMode.Install, Installer.Detect([], path));

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Programs\BoosterAI\BoosterAI.exe")]
    [InlineData(@"C:\Users\me\Desktop\BoosterAI.exe")]
    [InlineData(@"C:\Users\me\Downloads\BoosterAI-0.34.0-win-x64\BoosterAI.exe")]
    [InlineData(@"C:\Users\me\BoosterAI-Setup-files\BoosterAI.exe")]
    [InlineData("/home/me/.local/bin/BoosterAI")]
    [InlineData(null)]
    [InlineData("")]
    public void OrdinaryCopiesKeepTheirBehaviour(string? path)
    {
        Assert.Equal(SetupMode.None, Installer.Detect([], path));
        Assert.Equal(SetupMode.None, Installer.Detect(["--background"], path));
    }

    [Fact]
    public void FlagsChooseTheModeAndUninstallWins()
    {
        Assert.Equal(SetupMode.Install, Installer.Detect(["--install"], @"C:\x\BoosterAI.exe"));
        Assert.Equal(SetupMode.Uninstall, Installer.Detect(["--uninstall"], @"C:\x\BoosterAI.exe"));
        Assert.Equal(SetupMode.Uninstall, Installer.Detect(["--uninstall"], @"C:\x\BoosterAI-Setup-1.0.0-win-x64.exe"));
        Assert.Equal(SetupMode.Uninstall, Installer.Detect(["--install", "--uninstall"], null));
    }

    // ---- Quiet mode ----

    [Theory]
    [InlineData(new[] { "--install", "--quiet" }, @"C:\x\BoosterAI.exe", true)]
    [InlineData(new[] { "--quiet", "--uninstall" }, @"C:\x\BoosterAI.exe", true)]
    [InlineData(new[] { "--quiet" }, @"C:\Users\me\Downloads\BoosterAI-Setup-1.0.0-win-x64.exe", true)]
    [InlineData(new[] { "--install" }, @"C:\x\BoosterAI.exe", false)]
    [InlineData(new[] { "--quiet" }, @"C:\x\BoosterAI.exe", false)]
    [InlineData(new[] { "--quiet", "--background" }, null, false)]
    public void QuietOnlyAppliesToASetupMode(string[] args, string? path, bool quiet)
    {
        Assert.Equal(quiet, Installer.IsQuiet(args, path));
        // --quiet never changes which mode runs, and never turns an ordinary start into one.
        Assert.Equal(Installer.Detect(args.Where(a => a != "--quiet").ToArray(), path), Installer.Detect(args, path));
    }

    private string LogPath => Path.Combine(_root, "BoosterAI", Installer.LogName);

    [Fact]
    public void QuietInstallRunsTheInstallAndLogsSuccess()
    {
        var installed = 0;
        Assert.Equal(0, Installer.RunQuiet(SetupMode.Install, () => { installed++; return null; }, () => throw new InvalidOperationException(), LogPath));
        Assert.Equal(1, installed);
        Assert.Contains("install ok", File.ReadAllText(LogPath));
    }

    [Fact]
    public void QuietInstallFailureExitsNonzeroWithALogLine()
    {
        Assert.Equal(1, Installer.RunQuiet(SetupMode.Install, () => throw new IOException("file in use"), () => [], LogPath));
        Assert.Equal(0, Installer.RunQuiet(SetupMode.Install, () => "other copy", () => [], LogPath));
        var lines = File.ReadAllLines(LogPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("install failed: file in use", lines[0]);
        Assert.Contains("acknowledged", lines[1]);
    }

    [Fact]
    public void QuietUninstallReportsWhatWasLeft()
    {
        Assert.Equal(0, Installer.RunQuiet(SetupMode.Uninstall, () => throw new InvalidOperationException(), () => [], LogPath));
        Assert.Equal(1, Installer.RunQuiet(SetupMode.Uninstall, () => null, () => [@"C:\x\BoosterAI.lnk: denied"], LogPath));
        var lines = File.ReadAllLines(LogPath);
        Assert.Contains("uninstall ok", lines[0]);
        Assert.Contains(@"uninstall failed: C:\x\BoosterAI.lnk: denied", lines[1]);
    }

    [Fact]
    public void QuietWithoutAModeDoesNothing() =>
        Assert.NotEqual(0, Installer.RunQuiet(SetupMode.None, () => throw new InvalidOperationException(), () => throw new InvalidOperationException(), LogPath));

    // ---- Install plan ----

    [Fact]
    public void PlanPutsTheExeWhereTheScriptAndTheUpdaterExpectIt()
    {
        var p = SamplePlan();
        Assert.Equal(@"C:\Users\me\AppData\Local\Programs\BoosterAI", p.InstallDir);
        Assert.Equal(@"C:\Users\me\AppData\Local\Programs\BoosterAI\BoosterAI.exe", p.Exe);
        Assert.Equal(@"C:\Users\me\AppData\Local\Programs\BoosterStart", p.LegacyDir);
        Assert.Equal([@"C:\Users\me\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\BoosterStart.lnk", @"C:\Users\me\Desktop\BoosterStart.lnk"],
            p.LegacyShortcuts);
        Assert.False(p.RunsFromInstall);
        if (OperatingSystem.IsWindows())
            Assert.Equal(UpdateLogic.InstallPath(true, @"C:\Users\me\AppData\Local", @"C:\Users\me"), p.Exe);
    }

    [Fact]
    public void PlanShortcutsStartTheInstalledCopy()
    {
        var p = SamplePlan();
        Assert.Equal([@"C:\Users\me\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\BoosterAI.lnk", @"C:\Users\me\Desktop\BoosterAI.lnk"],
            p.Shortcuts.Select(s => s.Path));
        Assert.All(p.Shortcuts, s =>
        {
            Assert.Equal(p.Exe, s.Target);
            Assert.Equal(p.InstallDir, s.WorkingDirectory);
            Assert.Equal("Start work tabs with an AI CLI", s.Description);
        });
    }

    [Fact]
    public void PlanRegistryEntryUninstallsTheInstalledCopy()
    {
        var p = SamplePlan();
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\BoosterAI", p.UninstallKey);
        var v = p.RegistryValues.ToDictionary(r => r.Name, r => r.Value);
        Assert.Equal("BoosterAI", v["DisplayName"]);
        Assert.Equal("1.2.3", v["DisplayVersion"]);
        Assert.Equal("Solwation", v["Publisher"]);
        Assert.Equal(p.InstallDir, v["InstallLocation"]);
        Assert.Equal(p.Exe + ",0", v["DisplayIcon"]);
        Assert.Equal("\"C:\\Users\\me\\AppData\\Local\\Programs\\BoosterAI\\BoosterAI.exe\" --uninstall", v["UninstallString"]);
        Assert.Equal(5120, v["EstimatedSize"]);
        Assert.Equal(1, v["NoModify"]);
        Assert.Equal(1, v["NoRepair"]);
        Assert.All(p.RegistryValues, r => Assert.Equal(r.Value is int, r.Dword));
    }

    [Fact]
    public void RunningTheInstalledCopyWithInstallSkipsTheCopy()
    {
        var p = SamplePlan(@"c:\users\me\appdata\local\programs\boosterai\BOOSTERAI.EXE");
        Assert.True(p.RunsFromInstall);
        Assert.Contains("copy: skipped", Installer.DryRun(p));
    }

    [Fact]
    public void DryRunListsEveryInstallAndUninstallAction()
    {
        var text = Installer.DryRun(SamplePlan());
        Assert.Contains(@"copy: C:\Users\me\Downloads\BoosterAI-Setup-1.2.3-win-x64.exe -> C:\Users\me\AppData\Local\Programs\BoosterAI\BoosterAI.exe", text);
        Assert.Contains(@"shortcut: C:\Users\me\Desktop\BoosterAI.lnk -> C:\Users\me\AppData\Local\Programs\BoosterAI\BoosterAI.exe", text);
        Assert.Contains(@"registry: HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\BoosterAI", text);
        Assert.Contains("UninstallString = \"C:\\Users\\me\\AppData\\Local\\Programs\\BoosterAI\\BoosterAI.exe\" --uninstall (REG_SZ)", text);
        Assert.Contains("NoModify = 1 (REG_DWORD)", text);
        Assert.Contains(@"legacy: delete C:\Users\me\Desktop\BoosterStart.lnk", text);
        Assert.Contains("--background", text);
        Assert.Contains(@"delete: C:\Users\me\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\BoosterAI.lnk", text);
        Assert.Contains(@"keep: C:\Users\me\AppData\Roaming\BoosterAI\config.json", text);
        Assert.Contains("quiet (--quiet", text);
    }

    [Fact]
    public void RemoveHelperWaitsForTheProcessAndUsesALiteralPath()
    {
        var script = Installer.RemoveFolderHelper(4242, @"C:\Users\o'me\AppData\Local\Programs\BoosterAI");
        Assert.Contains("Get-Process -Id 4242", script);
        Assert.Contains(@"$dir = 'C:\Users\o''me\AppData\Local\Programs\BoosterAI'", script);
        Assert.Contains("Remove-Item -LiteralPath $dir -Recurse -Force", script);
    }

    // ---- Config merge ----

    private static FoundProject Found(string name) =>
        new(OperatingSystem.IsWindows() ? @"C:\work\" + name : "/work/" + name, name, Git: true, Ai: false);

    [Fact]
    public void FreshInstallGetsTheChosenSettingsAndProjects()
    {
        var cfg = Installer.MergeConfig(null, new("sv", "dark", "codex"), [Found("alpha"), Found("beta")]);
        Assert.Equal("sv", cfg.Language);
        Assert.Equal("dark", cfg.AppTheme);
        Assert.Equal(["alpha", "beta"], cfg.Tabs.Select(t => t.Name));
        Assert.All(cfg.Tabs, t => Assert.Equal("codex", t.Llm));
        Assert.Equal([0, 1], cfg.Tabs.Select(t => t.ColorIndex));
        Assert.Equal(PathMapping.ToTabPath(Found("alpha").Path), cfg.Tabs[0].Directory);
        Assert.All(cfg.Tabs, t => Assert.Equal(32, t.Id.Length));
    }

    private static AppConfig Existing() => new()
    {
        Language = "en", AppTheme = "light", ZoomPercent = 120, WslDistro = "Debian", AutoClearIdle = true, SkippedVersion = "0.9.0",
        Tabs =
        [
            new() { Id = "one", Name = "keep-me", Directory = PathMapping.ToTabPath(Found("alpha").Path), Llm = "agy", ColorIndex = 4,
                SubTabs = [new() { Id = "child", Name = "second", Llm = "claude" }] },
        ],
    };

    [Fact]
    public void UpgradeKeepsProjectsAndSettingsAndAppendsOnlyNewFolders()
    {
        var before = Existing();
        var cfg = Installer.MergeConfig(before, new("sv", "auto", "claude"), [Found("alpha"), Found("beta")]);
        Assert.Equal("sv", cfg.Language);
        Assert.Equal("auto", cfg.AppTheme);
        Assert.Equal(120, cfg.ZoomPercent);
        Assert.Equal("Debian", cfg.WslDistro);
        Assert.True(cfg.AutoClearIdle);
        Assert.Equal("0.9.0", cfg.SkippedVersion);
        Assert.Equal(["keep-me", "beta"], cfg.Tabs.Select(t => t.Name));
        Assert.Equal("agy", cfg.Tabs[0].Llm);
        Assert.Equal("second", Assert.Single(cfg.Tabs[0].SubTabs).Name);
        Assert.Equal(1, cfg.Tabs[1].ColorIndex);
        // The input is not modified.
        Assert.Equal("en", before.Language);
        Assert.Single(before.Tabs);
    }

    [Fact]
    public void SkippedStepsChangeNothing()
    {
        var before = Existing();
        var cfg = Installer.MergeConfig(before, null, []);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(cfg));
    }

    [Fact]
    public void SkippedSettingsStillAddProjectsWithTheDefaultCli()
    {
        var cfg = Installer.MergeConfig(Existing(), null, [Found("beta")]);
        Assert.Equal("en", cfg.Language);
        Assert.Equal(Catalog.DefaultLlm, cfg.Tabs[1].Llm);
    }

    [Fact]
    public void UnknownChoicesFallBack()
    {
        var cfg = Installer.MergeConfig(Existing(), new("xx", "neon", "nosuchcli"), [Found("beta")]);
        Assert.Equal("en", cfg.Language);
        Assert.Equal("light", cfg.AppTheme);
        Assert.Equal(Catalog.DefaultLlm, cfg.Tabs[1].Llm);
    }

    [Fact]
    public void TheBlankFirstRowIsReplacedByFoundProjects()
    {
        var blank = new AppConfig { Tabs = [new TabConfig { Id = "x", Name = Loc.Table("sv")["tab.newName"], Directory = "~" }] };
        var cfg = Installer.MergeConfig(blank, null, [Found("alpha")]);
        Assert.Equal("alpha", Assert.Single(cfg.Tabs).Name);
    }

    [Fact]
    public void PreferredCliIsTheOneMostProjectsUse()
    {
        Assert.Equal(Catalog.DefaultLlm, Installer.PreferredLlm(null));
        var cfg = new AppConfig { Tabs = [new() { Llm = "codex" }, new() { Llm = "codex" }, new() { Llm = "agy" }, new() { Llm = Catalog.ShellOnly }] };
        Assert.Equal("codex", Installer.PreferredLlm(cfg));
    }

    private string ConfigPath => Path.Combine(_root, "BoosterAI", "config.json");

    [Fact]
    public void WriteConfigCreatesNothingWhenEverythingWasSkipped()
    {
        Installer.WriteConfig(ConfigPath, null, []);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public void WriteConfigMergesIntoTheFileOnDisk()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, System.Text.Json.JsonSerializer.Serialize(Existing()));
        Installer.WriteConfig(ConfigPath, new("sv", "dark", "codex"), [Found("beta")]);
        var cfg = ConfigStore.Parse(File.ReadAllText(ConfigPath))!;
        Assert.Equal("sv", cfg.Language);
        Assert.Equal(120, cfg.ZoomPercent);
        Assert.Equal(["keep-me", "beta"], cfg.Tabs.Select(t => t.Name));
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void WriteConfigLeavesAnUnchangedFileAlone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        const string text = """{ "Language": "sv", "AppTheme": "dark" }""";
        File.WriteAllText(ConfigPath, text);
        Installer.WriteConfig(ConfigPath, new("sv", "dark", "claude"), []);
        Assert.Equal(text, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void WriteConfigKeepsABrokenFileAsBackup()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, "{ not json");
        Installer.WriteConfig(ConfigPath, new("en", "light", "claude"), []);
        Assert.Equal("{ not json", File.ReadAllText(ConfigPath + ".bak"));
        Assert.Equal("light", ConfigStore.Parse(File.ReadAllText(ConfigPath))!.AppTheme);
    }

    // ---- Copy ----

    [Fact]
    public void CopyReplacesTheTarget()
    {
        Directory.CreateDirectory(_root);
        var src = Path.Combine(_root, "src.exe");
        var dst = Path.Combine(_root, "dst.exe");
        File.WriteAllText(src, "new");
        File.WriteAllText(dst, "old");
        Installer.CopyWithRetry(src, dst);
        Assert.Equal("new", File.ReadAllText(dst));
    }

    [Fact]
    public void CopyGivesUpWithAClearMessageAfterTheAttempts()
    {
        Directory.CreateDirectory(_root);
        var src = Path.Combine(_root, "src.exe");
        File.WriteAllText(src, "new");
        var dst = Path.Combine(_root, "missing-folder", "dst.exe");
        var started = DateTime.UtcNow;
        var e = Assert.Throws<IOException>(() => Installer.CopyWithRetry(src, dst, attempts: 3, delay: TimeSpan.FromMilliseconds(20)));
        Assert.Contains("after 3 attempts", e.Message);
        Assert.Contains(dst, e.Message);
        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(35));
    }

    // ---- Release assets and the updater ----

    [Fact]
    public void UpdaterIgnoresTheSetupExe()
    {
        var v = new AppVersion(1, 2, 3, "");
        ReleaseAsset[] assets =
        [
            new("BoosterAI-Setup-1.2.3-win-x64.exe", "u1"), new("BoosterAI-1.2.3-win-x64.zip", "u2"),
            new("BoosterAI-1.2.3-linux-x64.tar.gz", "u3"), new("SHA256SUMS", "u4"),
        ];
        Assert.Equal("BoosterAI-1.2.3-win-x64.zip", UpdateLogic.SelectAsset(assets, v, "win-x64")?.Name);
        Assert.Equal("BoosterAI-1.2.3-linux-x64.tar.gz", UpdateLogic.SelectAsset(assets, v, "linux-x64")?.Name);
    }

    // ---- Wizard ----

    private static InstallWizardViewModel Wizard(AppConfig? existing = null) =>
        new(SamplePlan(), existing, installed: false, _ => { });

    [Fact]
    public void WizardPrefillsFromTheExistingConfig()
    {
        var vm = Wizard(new AppConfig { Language = "sv", AppTheme = "dark", Tabs = [new() { Llm = "codex" }] });
        Assert.Equal("sv", vm.SelectedLanguage.Id);
        Assert.Equal("dark", vm.SelectedAppearance.Id);
        Assert.Equal("codex", vm.SelectedLlm.Id);
        Assert.DoesNotContain(vm.LlmChoices, c => c.Id == Catalog.ShellOnly);
    }

    [Fact]
    public void WizardWalksForwardAndBack()
    {
        var vm = Wizard();
        Assert.True(vm.IsWelcome);
        Assert.False(vm.CanBack);
        vm.Next();
        Assert.True(vm.IsSettings);
        vm.Next();
        Assert.True(vm.IsProjects);
        vm.Next();
        Assert.True(vm.IsInstall);
        Assert.False(vm.CanSkip);
        Assert.True(vm.ShowInstall);
        vm.Back();
        Assert.True(vm.IsProjects);
        Assert.NotNull(vm.Settings);
    }

    [Fact]
    public void WelcomeSkipInstallsWithNothingChanged()
    {
        var vm = Wizard();
        vm.Skip();
        Assert.True(vm.IsInstall);
        Assert.Null(vm.Settings);
        Assert.Empty(vm.Projects);
        Assert.Contains(Loc.Get("setup.install.settingsKept"), vm.InstallLines);
    }

    [Fact]
    public void SettingsSkipKeepsTheConfigAndProjectsSkipAddsNone()
    {
        var vm = Wizard();
        vm.Next();
        vm.SelectedAppearance = vm.AppearanceChoices.Single(c => c.Id == "dark");
        vm.Skip();
        Assert.True(vm.IsProjects);
        Assert.Null(vm.Settings);
        vm.AddProjects([Found("alpha")]);
        vm.Skip();
        Assert.True(vm.IsInstall);
        Assert.Empty(vm.Projects);
        // Going back and choosing Next applies the settings again.
        vm.Back();
        vm.Back();
        vm.Next();
        Assert.Equal("dark", vm.Settings?.AppTheme);
    }

    [Fact]
    public void PickedProjectsAreNotListedTwice()
    {
        var vm = Wizard();
        vm.AddProjects([Found("alpha"), Found("beta")]);
        vm.AddProjects([Found("beta"), Found("gamma")]);
        Assert.Equal(["alpha", "beta", "gamma"], vm.Projects.Select(p => p.Name));
        Assert.Equal(Loc.Format("setup.projects.many", 3), vm.ProjectsText);
        Assert.Contains(Found("gamma").Path, vm.KnownPaths);
    }

    [Fact]
    public void ChangingTheLanguageSwitchesTheTexts()
    {
        var vm = Wizard();
        var changed = 0;
        vm.LanguageChanged += () => changed++;
        vm.SelectedLanguage = vm.LanguageChoices.Single(c => c.Id == "sv");
        Assert.Equal(1, changed);
        Assert.Equal("Steg 1 av 4", vm.StepText);
    }

    [Fact]
    public async Task InstallShowsTheDonePageOrTheError()
    {
        var vm = Wizard();
        vm.Next();
        vm.Next();
        vm.AddProjects([Found("alpha")]);
        vm.Next();
        SetupSettings? gotSettings = null;
        IReadOnlyList<FoundProject>? gotProjects = null;
        await vm.InstallAsync((_, _, _, _, _) => throw new IOException("locked"));
        Assert.True(vm.IsInstall);
        Assert.Contains("locked", vm.Error);
        Assert.False(vm.IsWorking);
        string? gotRoot = "unset";
        await vm.InstallAsync((plan, s, projects, root, progress) =>
        {
            gotRoot = root;
            gotSettings = s;
            gotProjects = projects;
            progress.Report("copying");
            return "note";
        });
        Assert.True(vm.IsDone);
        Assert.Equal("", vm.Error);
        Assert.Equal("note", vm.DoneNote);
        Assert.Equal("claude", gotSettings?.Llm);
        Assert.Equal("alpha", Assert.Single(gotProjects!).Name);
        Assert.Null(gotRoot);
    }

    [Fact]
    public void SearchRootIsPrefilledResolvedAndSavedOnlyWhenChanged()
    {
        var vm = Wizard(new AppConfig { ProjectSearchRoot = "/srv/code" });
        Assert.Equal("/srv/code", vm.SearchRoot);
        Assert.Equal(ProjectScanner.ResolveRoot("/srv/code", out _), vm.ScanRoot);
        Assert.Null(vm.SearchRootToSave);
        vm.SearchRoot = "  /srv/other  ";
        Assert.Equal("/srv/other", vm.SearchRootToSave);
        vm.SearchRoot = "";
        Assert.Equal(ProjectScanner.DefaultRoot(), vm.ScanRoot);
        Assert.Equal("", vm.SearchRootToSave);
        // A skipped project step leaves the setting alone.
        vm.Next();
        vm.Next();
        vm.Skip();
        Assert.Null(vm.SearchRootToSave);
    }

    [Fact]
    public void MergeSetsTheSearchRootOnlyWhenGiven()
    {
        Assert.Equal("/srv/code", Installer.MergeConfig(new AppConfig { ProjectSearchRoot = "/srv/code" }, null, []).ProjectSearchRoot);
        Assert.Equal("/srv/new", Installer.MergeConfig(new AppConfig { ProjectSearchRoot = "/srv/code" }, null, [], " /srv/new ").ProjectSearchRoot);
        Assert.Equal("", Installer.MergeConfig(new AppConfig { ProjectSearchRoot = "/srv/code" }, null, [], "").ProjectSearchRoot);
    }

    [Fact]
    public void WriteConfigSavesAChosenSearchRootOnAFreshInstall()
    {
        Installer.WriteConfig(ConfigPath, null, [], "/srv/code");
        Assert.Equal("/srv/code", ConfigStore.Parse(File.ReadAllText(ConfigPath))!.ProjectSearchRoot);
    }

    [Fact]
    public void ScreenshotParsesTheWizardSection()
    {
        var shot = Screenshot.Parse(["--screenshot", "out.png", "--window", "setup", "--section", "projects"]);
        Assert.Equal("setup", shot!.Window);
        Assert.Equal("projects", shot.Section);
    }
}
