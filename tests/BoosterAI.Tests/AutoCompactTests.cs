using System.Diagnostics;
using BoosterAI;
using Xunit;

public class AutoCompactTests
{
    [Theory]
    [InlineData("questions", 900000, 100000)]
    [InlineData("code", 750000, 250000)]
    [InlineData("orchestra", 600000, 400000)]
    public void RecommendationExplainsReserve(string profile, int tokens, int reserve)
    {
        var value = AutoCompact.Recommend("claude", 1_000_000, profile)!;
        Assert.Equal(tokens, value.Tokens);
        Assert.Equal(reserve, value.ReserveTokens);
        Assert.Equal(0, value.FileBudgetTokens);
    }

    [Fact]
    public void AiderReservesFilesAndInstructionsBeforeHistory()
    {
        var value = AutoCompact.Recommend("aider", 128_000, "code")!;
        Assert.Equal(32_000, value.Tokens);
        Assert.Equal(32_000, value.ReserveTokens);
        Assert.Equal(64_000, value.FileBudgetTokens);
    }

    [Theory]
    [InlineData("claude", 100000, "questions")]
    [InlineData("codex", 0, "code")]
    [InlineData("codex", -1, "code")]
    [InlineData("codex", 10000001, "code")]
    [InlineData("codex", 200000, "unknown")]
    [InlineData("agy", 1000000, "code")]
    [InlineData("openhands", 1000000, "code")]

    public void UnknownUnsupportedOrInsufficientContextHasNoRecommendation(string cli, int context, string profile) =>
        Assert.Null(AutoCompact.Recommend(cli, context, profile));

    [Theory]
    [InlineData("claude", 99999, false)]
    [InlineData("claude", 100000, true)]
    [InlineData("claude", 1000000, true)]
    [InlineData("claude", 1000001, false)]
    [InlineData("codex", 0, false)]
    [InlineData("codex", 1, true)]
    [InlineData("codex", 1000, true)]
    [InlineData("aider", 10000001, false)]
    public void BoundsAreCliSpecific(string cli, int value, bool valid) => Assert.Equal(valid, AutoCompact.ValidValue(cli, value));

    private static AutoCompactSetting Setting(string cli = "codex") => new()
    { Cli = cli, Model = "example-model", Mode = "recommended", Profile = "code", ContextTokens = 200000 };

    [Fact]
    public void ConfigRoundtripAndMergePreserveExactModelAndProfile()
    {
        var original = new AppConfig { AutoCompactSettings = [Setting()] };
        var copy = ConfigStore.Copy(original);
        Assert.Equal(150000, AutoCompact.ForTab(copy.AutoCompactSettings, "codex", "example-model"));
        Assert.Null(AutoCompact.ForTab(copy.AutoCompactSettings, "claude", "example-model"));
        Assert.Null(AutoCompact.ForTab(copy.AutoCompactSettings, "codex", "other-model"));
        var mine = ConfigStore.Copy(original);
        copy.AutoCompactSettings[0].Profile = "orchestra";
        Assert.True(ConfigMerge.TakeOthers(mine, original, copy));
        Assert.Equal(120000, AutoCompact.ForTab(mine.AutoCompactSettings, "codex", "example-model"));
        Assert.Equal("code", original.AutoCompactSettings[0].Profile);
    }

    [Fact]
    public void DefaultInactiveAndUnresolvedModelsDoNotOverride()
    {
        var setting = Setting();
        setting.Mode = "default";
        Assert.Null(AutoCompact.Resolve(setting));
        setting.Mode = "custom";
        setting.CustomTokens = 100000;
        setting.Model = Catalog.StandardModel;
        Assert.Null(AutoCompact.Resolve(setting));
        setting.Model = "example-model";
        setting.Active = false;
        Assert.Null(AutoCompact.Resolve(setting));
        setting.Active = true;
        setting.CustomTokens = 300000;
        Assert.Null(AutoCompact.Resolve(setting));
        setting.ContextTokens = 0; // Manual threshold is allowed without an invented capacity.
        Assert.Equal(300000, AutoCompact.Resolve(setting));
    }

    [Theory]
    [InlineData("codex", "-c model_auto_compact_token_limit=150000 -c model_auto_compact_token_limit_scope=total")]
    [InlineData("aider", "--max-chat-history-tokens 150000")]
    [InlineData("claude", "--autocompact 150000")]
    [InlineData("interpreter", "-c model_auto_compact_token_limit=150000")]
    public void ArgumentsReachNewOnceAndRestartWithoutChangingOrchestra(string cli, string expected)
    {
        var tab = new TabConfig { Llm = cli, Model = "example-model", Orkester = true, CompactTokens = 150000 };
        var track = new TabTrack("/s/lib.sh", "/s", "tab1");
        Assert.Contains(expected, Launcher.TabScript(tab));
        Assert.Contains(expected, Launcher.RestartScript(tab, track, resume: true));
        tab.NonInteractive = true;
        Assert.Contains(expected, Launcher.TabScript(tab, track: track));
        Assert.DoesNotContain(';', Launcher.TabScript(tab, track: track));
        if (cli == "codex") Assert.Contains("codex exec", Launcher.TabScript(tab));
    }

    [Fact]
    public void InvalidConfigNeverReachesShell()
    {
        foreach (var cli in Catalog.Llms)
        {
            Assert.Equal("", AutoCompact.Arguments(cli, -1));
            Assert.Equal("", AutoCompact.Arguments(cli, null));
        }
        Assert.Equal("", AutoCompact.Arguments("openhands", 200000));
    }

    [Fact]
    public void CodexMetadataIsBoundedValidatedAndUsesEffectiveCapacity()
    {
        var models = AutoCompactModels.ParseCodex("""
        {"models":[{"slug":"example","context_window":500000,"effective_context_window_percent":95},
        {"slug":"bad","context_window":null,"effective_context_window_percent":95},
        {"slug":"missing","context_window":200000},
        {"slug":"invalid","context_window":200000,"effective_context_window_percent":101}]}
        """);
        Assert.Single(models);
        Assert.Equal(475000, models["example"]);
        Assert.Empty(AutoCompactModels.ParseCodex("broken"));
        Assert.Empty(AutoCompactModels.ParseCodex(new string(' ', 2_000_001)));
        Assert.Empty(AutoCompactModels.ParseCodex("[]"));
        Assert.Empty(AutoCompactModels.ParseCodex("{\"models\":[1,null]}"));
        Assert.Null(AutoCompactModels.ClaudeContext("gateway-alias"));
        Assert.Null(AutoCompactModels.ClaudeContext(Catalog.StandardModel));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ClaudeProbesInstalledHelpAndWarnsAboutEnvironment(bool supported, bool overridden)
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Path.GetTempPath(), "booster-compact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var executable = Path.Combine(dir, "claude");
            await File.WriteAllTextAsync(executable, "#!/bin/bash\nif [[ $1 == --help ]]; then\n echo " + (supported ? "--autocompact" : "--model") + "\n exit 0\nfi\nprintf '%s\\n' \"$@\"\nexit 7\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var process = new Process { StartInfo = new ProcessStartInfo("bash")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
            process.StartInfo.Environment["PATH"] = dir + ":" + Environment.GetEnvironmentVariable("PATH");
            process.StartInfo.Environment["CLAUDE_CODE_AUTO_COMPACT_WINDOW"] = overridden ? "200000" : "";
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("IFS=$'\\n'\n" + AutoCompact.GuardClaude("claude " + AutoCompact.Arguments("claude", 150000), "claude"));
            try
            {
                process.Start();
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(timeout.Token);
                Assert.Equal(7, process.ExitCode);
                var stdout = await output;
                var stderr = await error;
                Assert.Equal(supported, stdout.Contains("--autocompact\n150000"));
                Assert.Equal(!supported, stderr.Contains("could not be verified"));
                Assert.Equal(overridden, stderr.Contains("overrides the selected"));
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); process.WaitForExit(); }
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public void EditorKeepsProfilesSeparateAndOnlyChangesOnSave()
    {
        var settings = new List<AutoCompactSetting>();
        var saves = 0;
        var editor = new AutoCompactViewModel("claude", settings, () => saves++, "claude-sonnet-5-5");
        Assert.Equal("1000000", editor.Context);
        editor.Mode = editor.Modes.Single(m => m.Id == "recommended");
        Assert.True(editor.CanSave);
        Assert.Empty(settings);
        editor.Save();
        Assert.Equal(1, saves);
        Assert.Equal(750000, AutoCompact.ForTab(settings, "claude", "claude-sonnet-5-5"));
        editor.Profile = editor.Profiles.Single(p => p.Id == "orchestra");
        Assert.Equal("default", editor.Mode.Id);
        editor.Mode = editor.Modes.Single(m => m.Id == "recommended");
        editor.Save();
        Assert.Equal(2, settings.Count);
        Assert.Single(settings, s => s.Active);
        Assert.Equal(600000, AutoCompact.ForTab(settings, "claude", "claude-sonnet-5-5"));
        editor.Profile = editor.Profiles.Single(p => p.Id == "code");
        Assert.Equal("recommended", editor.Mode.Id);
        editor.Mode = editor.Modes.Single(m => m.Id == "default");
        editor.Save();
        Assert.Null(AutoCompact.ForTab(settings, "claude", "claude-sonnet-5-5"));
    }

    [Fact]
    public void EditorRejectsInvalidInputAndPreservesManualCapacity()
    {
        var editor = new AutoCompactViewModel("codex", [], () => { }, "custom-model");
        Assert.Equal("", editor.Context);
        editor.Mode = editor.Modes.Single(m => m.Id == "recommended");
        Assert.False(editor.CanSave);
        editor.UpdateMetadata(new Dictionary<string, int> { ["custom-model"] = 190000 });
        Assert.Equal("190000", editor.Context);
        Assert.True(editor.CanSave);
        editor.Context = "128000";
        editor.UpdateMetadata(new Dictionary<string, int> { ["custom-model"] = 475000 });
        Assert.Equal("128000", editor.Context);
        editor.Mode = editor.Modes.Single(m => m.Id == "custom");
        foreach (var invalid in new[] { "-1", "1;touch x", "200000", "99999999999999" })
        {
            editor.Custom = invalid;
            Assert.False(editor.CanSave);
        }
        editor.Custom = "64000";
        Assert.True(editor.CanSave);
        editor.Model = Catalog.StandardModel;
        Assert.False(editor.CanSave);
    }

    [Fact]
    public void NewPreferenceOffersExplicitRestartWithoutChangingRunningSnapshot()
    {
        var config = new AppConfig { Tabs = [new TabConfig { Llm = "codex", Model = "example-model" }] };
        var vm = new MainViewModel(config);
        var row = Assert.Single(vm.Tabs);
        row.LaunchedAs = row.ToConfig(forLaunch: true);
        row.SetState(TabState.Running, null, activity: BoosterAI.Activity.Idle);
        Assert.False(row.IsChanged);
        config.AutoCompactSettings.Add(Setting());
        Assert.True(row.IsChanged);
        Assert.True(row.CanRestart);
        Assert.Null(row.LaunchedAs.CompactTokens);
        Assert.Equal(150000, row.ToConfig(forLaunch: true).CompactTokens);
        Assert.Null(row.ToConfig(forLaunch: false).CompactTokens);
        Assert.False(row.PendingRestart);
        var recovered = ConfigStore.Copy(new AppConfig { Tabs = [row.ToConfig(forLaunch: true)] }).Tabs[0];
        Assert.Equal(150000, recovered.CompactTokens);
    }

    [Fact]
    public void InterpreterControlsRequireCurrentExecutableAndVerifiedCapacity()
    {
        var editor = new AutoCompactViewModel("interpreter", [], () => { }, "ollama/example:7b");
        editor.UpdateCliSupport(new CliStatus("interpreter", "file", "/example/interpreter") { Help = "--local --context_window" });
        Assert.False(editor.Supported);
        editor.UpdateCliSupport(new CliStatus("interpreter", "file", "/example/interpreter") { Help = "--oss --config" });
        Assert.True(editor.Supported);
        Assert.Equal("", editor.Context);
        editor.Mode = editor.Modes.Single(m => m.Id == "recommended");
        Assert.False(editor.CanSave);
        editor.Context = "128000";
        Assert.True(editor.CanSave);
        var script = Launcher.TabScript(new TabConfig { Llm = "interpreter", Model = "example-model", CompactTokens = 64000 });
        Assert.Contains("--oss", script);
        Assert.Contains("-c model_auto_compact_token_limit=64000", script);
        Assert.Contains("Using legacy defaults", script);
    }

    [Fact]
    public async Task MetadataProbeProjectsOnlyContextFieldsAndIgnoresLoginBanners()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("booster-compact-metadata-").FullName;
        using var process = new Process { StartInfo = new ProcessStartInfo("bash")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "models_cache.json"), """
                {"private_field":"DO_NOT_EMIT","models":[{"slug":"sample-model","context_window":200000,"effective_context_window_percent":95,"private_field":"DO_NOT_EMIT"}]}
                """);
            process.StartInfo.Environment["CODEX_HOME"] = dir;
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("printf 'login banner\\n'\n" + AutoCompactModels.Probe);
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
            var text = await output;
            Assert.Equal("", await errors);
            Assert.DoesNotContain("DO_NOT_EMIT", text);
            Assert.Equal(190000, AutoCompactModels.ParseProbeOutput(text)["sample-model"]);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Directory.Delete(dir, true);
        }
    }

}
