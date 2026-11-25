using Xunit;
using System.Diagnostics;
using System.Text.Json;
using BoosterAI;

/// <summary>Plan limits per account (#199): the Codex and Claude readers, BoosterAI's Claude status line and the Accounts rows.</summary>
[Collection(ConfigFileCollection.Name)]
public class AccountLimitsTests
{
    private static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static async Task<(string Out, string Err, int Code)> Bash(string root, string script, string? stdin = null,
        Dictionary<string, string>? env = null, string? cwd = null, string? bin = null)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("bash")
        { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false } };
        TestShell.Configure(process.StartInfo, root, bin);
        if (bin is not null) process.StartInfo.Environment["PATH"] = bin;
        foreach (var (k, v) in env ?? []) process.StartInfo.Environment[k] = v;
        if (cwd is not null) process.StartInfo.WorkingDirectory = cwd;
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(script);
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(stdin ?? "");
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token);
            return (await output, await errors, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit();
            TestShell.StopLeftovers(root);
        }
    }

    private static string Temp(string name) => Directory.CreateTempSubdirectory("booster-limits-" + name + "-").FullName;

    private static string TokenCount(string at, string limits) =>
        $$$"""{"timestamp":"{{{at}}}","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":5}},"rate_limits":{{{limits}}}}}""";

    [Fact]
    public async Task CodexReadsTheLastTokenCountOfTheNewestRolloutAndProjectsOnlyLimits()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Temp("codex");
        try
        {
            var home = Path.Combine(root, ".codex-work");
            var old = Path.Combine(home, "sessions", "2026", "11", "20");
            var day = Path.Combine(home, "sessions", "2026", "11", "25");
            Directory.CreateDirectory(old);
            Directory.CreateDirectory(day);
            var good = $$$"""{"limit_id":"codex","plan_type":"SECRET_PLAN","primary":{"used_percent":12.5,"window_minutes":300,"resets_at":{{{Now + 3600}}}},"secondary":{"used_percent":140,"window_minutes":10080,"resets_at":{{{Now + 86400}}}},"credits":{"has_credits":true,"unlimited":false,"balance":"17.50"}}""";
            File.WriteAllLines(Path.Combine(day, "rollout-b.jsonl"),
            [
                """{"timestamp":"2026-11-25T08:00:00Z","type":"session_meta","payload":{"instructions":"SECRET_TEXT"}}""",
                TokenCount("2026-11-25T08:01:00.000Z", good),
                TokenCount("2026-11-25T08:02:00.000Z", "null"), // a turn without limits (an API key login) keeps the earlier one
                """not json but "token_count" anyway""",
                """{"timestamp":"2026-11-25T08:03:00Z","type":"response_item","payload":{"type":"message","content":"SECRET_TEXT"}}""",
                """{"timestamp":"2026-11-25T08:04:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"prim""",
            ]);
            // An older rollout touched earlier: not read.
            File.WriteAllText(Path.Combine(old, "rollout-a.jsonl"), TokenCount("2026-11-20T08:00:00Z",
                $$$"""{"primary":{"used_percent":99,"window_minutes":300,"resets_at":{{{Now + 60}}}}}""") + "\n");
            File.SetLastWriteTimeUtc(Path.Combine(old, "rollout-a.jsonl"), DateTime.UtcNow.AddDays(-5));

            var (output, errors, code) = await Bash(root, "printf 'login banner\\n'\n" + AccountLimitsProbe.Script("codex", home));
            Assert.Equal(0, code);
            Assert.Equal("", errors);
            Assert.DoesNotContain("SECRET", output);
            Assert.DoesNotContain("input_tokens", output);
            var limits = AccountLimitsProbe.Parse("codex", output);
            Assert.Equal(12.5, limits.Session!.UsedPercent);
            Assert.Equal(300, limits.Session.Minutes);
            Assert.Equal(Now + 3600, limits.Session.ResetsAt.ToUnixTimeSeconds());
            Assert.Equal(140, limits.Week!.UsedPercent); // Codex allows more than 100 %
            Assert.Equal(new LimitCredits(true, false, "17.50"), limits.Credits);
            Assert.Equal(DateTimeOffset.Parse("2026-11-25T08:01:00Z"), limits.At);

            // A resumed session appends to its old rollout: the newest file by time wins, wherever it lies.
            File.SetLastWriteTimeUtc(Path.Combine(old, "rollout-a.jsonl"), DateTime.UtcNow.AddMinutes(1));
            var resumed = AccountLimitsProbe.Parse("codex", (await Bash(root, AccountLimitsProbe.Script("codex", home))).Out);
            Assert.Equal(99, resumed.Session!.UsedPercent);
            Assert.Null(resumed.Week);
            Assert.Null(resumed.Credits);

            // No sessions at all (a new account): nothing known.
            var empty = AccountLimitsProbe.Parse("codex", (await Bash(root, AccountLimitsProbe.Script("codex", Path.Combine(root, "none")))).Out);
            Assert.Equal(AccountLimits.Unknown, empty);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CodexWindowsAreSortedByLengthAndBadFieldsAreUnknown()
    {
        // The weekly window can be the primary one (seen on business plans); no short window then.
        var weekly = AccountLimitsProbe.Parse("codex", $$$"""BOOSTER_LIMITS={"at":"2026-11-25T08:00:00Z","primary":{"used":30,"resets_at":{{{Now + 600}}},"minutes":10080},"secondary":null,"credits":{"has_credits":false,"unlimited":false,"balance":null}}""");
        Assert.Null(weekly.Session);
        Assert.Equal(30, weekly.Week!.UsedPercent);
        Assert.Equal(new LimitCredits(false, false, null), weekly.Credits);

        foreach (var bad in new[]
        {
            "", "no marker", "BOOSTER_LIMITS=", "BOOSTER_LIMITS={broken", "BOOSTER_LIMITS=[]", "BOOSTER_LIMITS={}",
            $$$"""BOOSTER_LIMITS={"primary":{"used":"12","resets_at":{{{Now}}},"minutes":300}}""",
            $$$"""BOOSTER_LIMITS={"primary":{"used":-1,"resets_at":{{{Now}}},"minutes":300}}""",
            """BOOSTER_LIMITS={"primary":{"used":12,"minutes":300}}""",
            $$$"""BOOSTER_LIMITS={"primary":{"used":12,"resets_at":{{{Now}}}}}""", // no length: neither window
            $$$"""BOOSTER_LIMITS={"primary":{"used":12,"resets_at":{{{Now}}},"minutes":3000}}""", // two days: neither window
        })
        {
            var limits = AccountLimitsProbe.Parse("codex", bad);
            Assert.Null(limits.Session);
            Assert.Null(limits.Week);
        }
        Assert.Equal("xyz", AccountLimitsProbe.Parse("codex", """BOOSTER_LIMITS={"credits":{"has_credits":true,"balance":"x\u0007y\nz"}}""").Credits!.Balance);
    }

    [Fact]
    public void ExpiredWindowsBecomeUnknown()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(Now);
        var limits = new AccountLimits(new LimitWindow(50, now.AddSeconds(-1), 300), new LimitWindow(20, now.AddDays(1)), null, now.AddHours(-6));
        var current = limits.AsOf(now);
        Assert.Null(current.Session);
        Assert.Equal(20, current.Week!.UsedPercent);
        Assert.Equal(limits.At, current.At);
        Assert.Null(limits.AsOf(now.AddDays(2)).Week);
    }

    [Fact]
    public async Task ClaudeReadsOnlyTheSnapshotFieldsFromTheAccountDirectory()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Temp("claude");
        try
        {
            var home = Path.Combine(root, ".claude-work");
            Directory.CreateDirectory(home);
            var snapshot = Path.Combine(home, ClaudeStatusLine.SnapshotFile);
            File.WriteAllText(snapshot, $$$"""{"captured_at":{{{Now - 60}}},"five_hour":{"used_percentage":37,"resets_at":{{{Now + 3600}}}},"seven_day":{"used_percentage":"62","resets_at":{{{Now + 86400}}}},"secret":"SECRET"}""");
            var (output, errors, _) = await Bash(root, AccountLimitsProbe.Script("claude", home));
            Assert.Equal("", errors);
            Assert.DoesNotContain("SECRET", output);
            var limits = AccountLimitsProbe.Parse("claude", output);
            Assert.Equal(37, limits.Session!.UsedPercent);
            Assert.Null(limits.Week); // a string percentage is not a number
            Assert.Null(limits.Credits); // Claude reports no credits
            Assert.Equal(Now - 60, limits.At!.Value.ToUnixTimeSeconds());

            // The default account: CLAUDE_CONFIG_DIR as the tab would have it, else ~/.claude.
            var viaEnv = AccountLimitsProbe.Parse("claude", (await Bash(root, AccountLimitsProbe.Script("claude", null), env: new() { ["CLAUDE_CONFIG_DIR"] = home })).Out);
            Assert.Equal(37, viaEnv.Session!.UsedPercent);

            File.WriteAllText(snapshot, "{not json");
            Assert.Equal(AccountLimits.Unknown, AccountLimitsProbe.Parse("claude", (await Bash(root, AccountLimitsProbe.Script("claude", home))).Out));
            File.Delete(snapshot);
            Assert.Equal(AccountLimits.Unknown, AccountLimitsProbe.Parse("claude", (await Bash(root, AccountLimitsProbe.Script("claude", home))).Out));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string StatusInput(string project, bool limits = true) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["session_id"] = "SECRET_SESSION",
        ["transcript_path"] = "/SECRET/path.jsonl",
        ["cwd"] = project,
        ["workspace"] = new { current_dir = project, project_dir = project },
        ["model"] = new { id = "sample", display_name = "Sample" },
        ["rate_limits"] = limits ? new { five_hour = new { used_percentage = 23.5, resets_at = Now + 7200 }, seven_day = new { used_percentage = 41, resets_at = Now + 400000 } } : null,
    });

    [Fact]
    public async Task TheStatusLineSavesTheLimitsAndChainsToTheUsersOwnStatusLine()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Temp("statusline");
        try
        {
            var script = Path.Combine(root, ClaudeStatusLine.ScriptFile);
            File.WriteAllText(script, ClaudeStatusLine.Script + "\n");
            var account = Path.Combine(root, ".claude-work");
            var project = Path.Combine(root, "project");
            Directory.CreateDirectory(account);
            Directory.CreateDirectory(Path.Combine(project, ".claude"));
            var env = new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = account };
            var command = ClaudeStatusLine.Command(root);
            var input = StatusInput(project);

            // No status line of the user's own: nothing printed, the snapshot saved with only the limit fields.
            var (output, errors, code) = await Bash(root, command, input, env, project);
            Assert.Equal(0, code);
            Assert.Equal("", output);
            Assert.Equal("", errors);
            var saved = File.ReadAllText(Path.Combine(account, ClaudeStatusLine.SnapshotFile));
            Assert.DoesNotContain("SECRET", saved);
            using (var doc = JsonDocument.Parse(saved))
            {
                Assert.Equal(["five_hour", "seven_day", "captured_at"], doc.RootElement.EnumerateObject().Select(p => p.Name));
                Assert.Equal(23.5, doc.RootElement.GetProperty("five_hour").GetProperty("used_percentage").GetDouble());
            }
            Assert.Empty(Directory.GetFiles(account, "*.tmp"));
            var read = AccountLimitsProbe.Parse("claude", (await Bash(root, AccountLimitsProbe.Script("claude", account))).Out);
            Assert.Equal(23.5, read.Session!.UsedPercent);
            Assert.Equal(41, read.Week!.UsedPercent);

            // The account's own status line: it gets the same input and its output is the status line.
            File.WriteAllText(Path.Combine(account, "settings.json"), JsonSerializer.Serialize(new
            {
                statusLine = new { type = "command", command = $"cat > {Launcher.Quote(Path.Combine(root, "got.json"))} && printf 'user line %s' \"$CLAUDE_CONFIG_DIR\"" },
            }));
            (output, errors, _) = await Bash(root, command, input, env, project);
            Assert.Equal("user line " + account, output);
            Assert.Equal("", errors);
            Assert.Equal(input, File.ReadAllText(Path.Combine(root, "got.json")));

            // A project's settings win over the account's, settings.local.json over settings.json, as in Claude.
            File.WriteAllText(Path.Combine(project, ".claude", "settings.json"), """{"statusLine":{"type":"command","command":"echo project"}}""");
            Assert.Equal("project\n", (await Bash(root, command, input, env, project)).Out);
            File.WriteAllText(Path.Combine(project, ".claude", "settings.local.json"), """{"statusLine":{"type":"command","command":"echo local"}}""");
            Assert.Equal("local\n", (await Bash(root, command, input, env, project)).Out);
            // A broken file is skipped; one that turns the status line off stops the search.
            File.WriteAllText(Path.Combine(project, ".claude", "settings.local.json"), "{broken");
            Assert.Equal("project\n", (await Bash(root, command, input, env, project)).Out);
            File.WriteAllText(Path.Combine(project, ".claude", "settings.local.json"), """{"statusLine":null}""");
            Assert.Equal("", (await Bash(root, command, input, env, project)).Out);
            File.Delete(Path.Combine(project, ".claude", "settings.local.json"));

            // Input without limits (or not JSON at all) keeps the last snapshot and still runs the user's status line.
            var before = File.ReadAllText(Path.Combine(account, ClaudeStatusLine.SnapshotFile));
            Assert.Equal("project\n", (await Bash(root, command, StatusInput(project, limits: false), env, project)).Out);
            Assert.Equal("project\n", (await Bash(root, command, "not json", env, project)).Out);
            Assert.Equal(before, File.ReadAllText(Path.Combine(account, ClaudeStatusLine.SnapshotFile)));

            // A failing user command prints what it printed and never fails the status line; no recursion into itself.
            File.WriteAllText(Path.Combine(project, ".claude", "settings.json"), """{"statusLine":{"type":"command","command":"echo partial && exit 7"}}""");
            (output, _, code) = await Bash(root, command, input, env, project);
            Assert.Equal(("partial\n", 0), (output, code));
            File.WriteAllText(Path.Combine(project, ".claude", "settings.json"), JsonSerializer.Serialize(new { statusLine = new { type = "command", command } }));
            (output, _, code) = await Bash(root, command, input, env, project);
            Assert.Equal(("", 0), (output, code));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ClaudeSettingsCarryTheStatusLineAndAPlainCopy()
    {
        var json = TabSession.HooksJson("/s/tab1.log", ClaudeStatusLine.Command("/s/it's here"));
        using var doc = JsonDocument.Parse(json);
        var line = doc.RootElement.GetProperty("statusLine");
        Assert.Equal("command", line.GetProperty("type").GetString());
        Assert.Equal("python3 '/s/it'\\''s here/statusline.py'", line.GetProperty("command").GetString());
        Assert.True(doc.RootElement.GetProperty("hooks").TryGetProperty("Stop", out _));
        Assert.False(JsonDocument.Parse(TabSession.HooksJson("/s/tab1.log")).RootElement.TryGetProperty("statusLine", out _));

        var root = Temp("track");
        try
        {
            var session = TabSession.Open(root);
            var track = session.NewTrack()!;
            Assert.Equal(ClaudeStatusLine.Script.Replace("\r\n", "\n") + "\n", File.ReadAllText(Path.Combine(root, ClaudeStatusLine.ScriptFile)));
            var full = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, track.Id + ".claude.json"))).RootElement;
            Assert.Equal(ClaudeStatusLine.Command(track.Dir), full.GetProperty("statusLine").GetProperty("command").GetString());
            var plain = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, track.Id + ".claude-plain.json"))).RootElement;
            Assert.False(plain.TryGetProperty("statusLine", out _));
            Assert.Equal(full.GetProperty("hooks").ToString(), plain.GetProperty("hooks").ToString());
            // The tab command line only names the file: the status line never adds a ';' for Windows Terminal.
            var script = Launcher.TabScript(new TabConfig { Name = "Atlas", Directory = "~", Llm = "claude" }, null, track);
            Assert.Contains("--settings ", script);
            Assert.Contains(track.Id + ".claude.json", script);
            Assert.DoesNotContain("statusline", script);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WithoutPython3TheTabUsesTheSettingsWithoutTheStatusLine()
    {
        if (OperatingSystem.IsWindows()) return;
        foreach (var python in new[] { true, false })
        {
            var root = Temp("nopython");
            try
            {
                var session = TabSession.Open(root);
                var track = session.NewTrack()!;
                var bin = Path.Combine(root, "bin");
                Directory.CreateDirectory(bin);
                foreach (var tool in new[] { "cut", "date", "cp", "cat" }.Concat(python ? ["python3"] : []))
                    File.CreateSymbolicLink(Path.Combine(bin, tool), FindOnPath(tool));
                var (_, errors, code) = await Bash(root, $". {Launcher.Quote(track.Lib)} {Launcher.Quote(track.Dir)} {track.Id} Atlas && echo ok", bin: bin);
                Assert.Equal(0, code);
                var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, track.Id + ".claude.json"))).RootElement;
                Assert.Equal(python, settings.TryGetProperty("statusLine", out _));
                Assert.True(settings.TryGetProperty("hooks", out _));
            }
            finally { Directory.Delete(root, true); }
        }
    }

    private static string FindOnPath(string tool) =>
        new[] { "/usr/bin", "/bin" }.Select(d => Path.Combine(d, tool)).First(File.Exists);

    [Fact]
    public void TimesAndTextsAreLocalizedAndUnknownIsNeverZero()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            var now = new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 25, 10, 0, 0)));
            Assert.Equal("14:20", LimitText.When(now.AddHours(4).AddMinutes(20), now));
            Assert.Equal("i morgon 09:00", LimitText.When(now.AddHours(23), now));
            Assert.Equal("i går 09:00", LimitText.When(now.AddHours(-25), now));
            Assert.Equal("fre 10:00", LimitText.When(now.AddDays(2), now)); // 2026-11-27 is a Friday
            Assert.Equal("2026-12-05 10:00", LimitText.When(now.AddDays(10), now));
            Assert.Equal("Okänd", LimitText.Used(null, now));
            Assert.Equal("38 % använt · återställs 14:20", LimitText.Used(new LimitWindow(37.6, now.AddHours(4).AddMinutes(20)), now));
            Assert.Equal("", LimitText.Credits(null));
            Assert.Equal("Krediter: obegränsade", LimitText.Credits(new LimitCredits(true, true, null)));
            Assert.Equal("Krediter: 17.50", LimitText.Credits(new LimitCredits(true, false, "17.50")));
            Assert.Equal("Krediter: inga", LimitText.Credits(new LimitCredits(false, false, null)));
            Loc.Use("en");
            Assert.Equal("38% used · resets Fri 10:00", LimitText.Used(new LimitWindow(37.6, now.AddDays(2)), now));
            Assert.Equal("Unknown", LimitText.Used(null, now));
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public async Task RowsReadLimitsOnlyWhenEnabledAndShowUnknownWithoutABar()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            var vm = new MainViewModel(new AppConfig { Accounts = [new() { Id = "codex-work", Agent = "codex", Name = "Work", Directory = "~/.codex-work" }] });
            var now = DateTimeOffset.Now;
            vm.LimitsClock = () => now;
            var calls = new List<string?>();
            vm.ReadAccountLimits = (_, _, dir) =>
            {
                lock (calls) calls.Add(dir);
                return Task.FromResult(dir is null
                    ? new AccountLimits(new LimitWindow(37, now.AddHours(2), 300), new LimitWindow(62, now.AddSeconds(-5), 10080), new LimitCredits(true, true, null), now.AddMinutes(-3))
                    : AccountLimits.Unknown);
            };
            var accounts = vm.AccountsEditor("codex")!;
            vm.AccountStatusEnabled = true;
            vm.CheckAccountLogin = (_, _, _) => Task.FromResult(AccountLoginStatus.Unknown);
            await accounts.RefreshStatusAsync();
            Assert.Empty(calls); // tests and screenshots never read an account's files

            vm.AccountLimitsEnabled = true;
            await accounts.RefreshStatusAsync();
            Assert.Equal([null, "~/.codex-work"], calls);
            var main = accounts.Rows[0];
            Assert.True(main.HasSession);
            Assert.Equal(37, main.SessionPercent);
            Assert.Equal("5 h", main.SessionLabel);
            Assert.False(main.HasWeek); // reset since it was reported
            Assert.Equal("Okänd", main.WeekText);
            Assert.Equal("Krediter: obegränsade", main.CreditsText);
            Assert.StartsWith("per ", main.LimitsAge);
            var work = accounts.Rows[1];
            Assert.False(work.HasSession);
            Assert.False(work.HasWeek);
            Assert.False(work.HasCredits);
            Assert.Equal("Okänd", work.SessionText);
            Assert.Equal("Ingen avläsning än", work.LimitsAge);
            Assert.Contains("Codex", work.LimitsTip);

            // A failing read and an invalid directory are Unknown; the invalid one is never run.
            vm.ReadAccountLimits = (_, _, dir) => { lock (calls) calls.Add(dir); throw new InvalidOperationException("boom"); };
            calls.Clear();
            await accounts.RefreshLimitsAsync();
            Assert.Equal(AccountLimits.Unknown, accounts.Rows[0].Limits);
            Assert.Equal(AccountLimits.Unknown, await vm.ReadAccountLimitsAsync("codex", "relative; rm"));
            Assert.Equal(2, calls.Count);

            accounts.ShowSampleLimits("codex");
            Assert.Equal(37, accounts.Rows[0].SessionPercent);
            Assert.Equal("Krediter: 120", accounts.Rows[0].CreditsText);
            Assert.False(accounts.Rows[1].HasSession);
        }
        finally { Loc.Use(previous); }
    }

    private static LimitWindow Win(double used, DateTimeOffset now, int? minutes = null) => new(used, now.AddHours(2), minutes);

    [Fact]
    public void IndicatorShowsTheHigherWindowWarnsFromEightyAndHidesUnknown()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            var now = DateTimeOffset.Now;
            Assert.Null(LimitIndicator.For(AccountLimits.Unknown, now));
            // A window that has reset is unknown, so it never shows an old percentage.
            Assert.Null(LimitIndicator.For(new(new LimitWindow(90, now.AddMinutes(-1)), null, null, now), now));

            var calm = LimitIndicator.For(new(Win(37, now, 300), Win(62, now, 10080), null, now.AddMinutes(-3)), now)!;
            Assert.Equal("62 %", calm.Caption);
            Assert.False(calm.Warn);
            Assert.Contains("5 h: 37 % använt", calm.Tip);
            Assert.Contains("Vecka: 62 % använt", calm.Tip);
            Assert.Contains("per ", calm.Tip);
            Assert.DoesNotContain("nära", calm.Name);

            var weekOnly = LimitIndicator.For(new(null, Win(8, now), null, null), now)!;
            Assert.Equal("8 %", weekOnly.Caption);
            Assert.Contains("5 h: Okänd", weekOnly.Tip);

            Assert.False(LimitIndicator.For(new(Win(79.4, now), null, null, null), now)!.Warn);
            var warn = LimitIndicator.For(new(Win(79.5, now), Win(20, now), null, null), now)!;
            Assert.True(warn.Warn);
            Assert.Equal("80 %", warn.Caption);
            Assert.Contains("nära gränsen", warn.Name); // text cue, not colour only
            Assert.Equal("140 %", LimitIndicator.For(new(null, Win(140, now), null, null), now)!.Caption);
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public void MeterFillsToTheHigherWindowChangesColourFromEightyAndIsEmptyWhileUnknown()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            var now = DateTimeOffset.Now;
            // The closed Agent box's meter (#202) fills to the higher of the two windows, also well below 80 %.
            var calm = new AgentChoice("claude", "", "Privat", true, null, LimitIndicator.For(new(Win(37, now, 300), Win(62, now, 10080), null, now), now));
            Assert.True(calm.HasLimit);
            Assert.Equal(62, calm.LimitPercent);
            Assert.False(calm.LimitWarn);
            Assert.Equal(8, new AgentChoice("codex", "", "", false, null, LimitIndicator.For(new(null, Win(8, now), null, null), now)).LimitPercent);
            Assert.Equal(3, new AgentChoice("codex", "", "", false, null, LimitIndicator.For(new(Win(2.6, now), Win(1, now), null, null), now)).LimitPercent);

            // From 80 % (after rounding) it takes the warning colour, with the warning icon and "nära gränsen" beside it.
            var below = new AgentChoice("claude", "", "", false, null, LimitIndicator.For(new(Win(79.4, now), Win(20, now), null, null), now));
            Assert.Equal(79, below.LimitPercent);
            Assert.False(below.LimitWarn);
            var warn = new AgentChoice("claude", "", "", false, null, LimitIndicator.For(new(Win(91, now, 300), Win(64, now, 10080), null, now), now));
            Assert.Equal(91, warn.LimitPercent);
            Assert.True(warn.LimitWarn);
            Assert.Contains("nära gränsen", warn.LimitName);
            // Its tooltip lists both windows with when they reset, and when the agent reported them.
            Assert.Contains("5 h: 91 % använt · återställs ", warn.LimitTip);
            Assert.Contains("Vecka: 64 % använt · återställs ", warn.LimitTip);
            Assert.Contains("per ", warn.LimitTip);
            // Over the plan (Codex can report it) the meter is simply full.
            Assert.Equal(100, new AgentChoice("codex", "", "", false, null, LimitIndicator.For(new(null, Win(140, now), null, null), now)).LimitPercent);

            // Unknown, or a window that has reset, shows no filled meter: hidden, never a guessed 0 %.
            foreach (var unknown in new[] { AccountLimits.Unknown, new AccountLimits(new LimitWindow(90, now.AddMinutes(-1)), null, null, now) })
            {
                var choice = new AgentChoice("claude", "", "", false, null, LimitIndicator.For(unknown, now));
                Assert.False(choice.HasLimit);
                Assert.Equal(0, choice.LimitPercent);
                Assert.False(choice.LimitWarn);
            }
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public async Task AgentBoxesShareTheAccountsReadingAndReadOncePerAccount()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            var vm = new MainViewModel(new AppConfig
            {
                Accounts = [new() { Id = "claude-work", Agent = "claude", Name = "Work", Directory = "~/.claude-work" }],
                Tabs = [new() { Name = "A", Directory = "/srv/a", Llm = "claude" }, new() { Name = "B", Directory = "/srv/b", Llm = "claude", Account = "claude-work" },
                    new() { Name = "C", Directory = "/srv/c", Llm = "agy" }, new() { Name = "D", Directory = "/srv/d", Llm = "aider" }],
            });
            var now = DateTimeOffset.Now;
            var clock = now;
            vm.LimitsClock = () => clock;
            var calls = new List<string?>();
            vm.ReadAccountLimits = (_, llm, dir) =>
            {
                lock (calls) calls.Add(llm + ":" + dir);
                if (llm == "agy") return Task.FromResult(new AccountLimits(Win(28, now, 300), Win(44, now, 10080), null, now));
                return Task.FromResult(dir is null ? new AccountLimits(Win(85, now, 300), Win(10, now, 10080), null, now) : AccountLimits.Unknown);
            };
            Assert.All(vm.Tabs, t => Assert.All(t.AgentChoices, c => Assert.False(c.HasLimit)));
            await vm.RefreshAccountLimitsAsync(); // disabled: tests never read a file
            Assert.Empty(calls);

            vm.AccountLimitsEnabled = true;
            await vm.RefreshAccountLimitsAsync();
            // Four tabs: one read per account of claude and once for agy, none for aider (#206).
            Assert.Equal(["agy:", "claude:", "claude:~/.claude-work"], calls.OrderBy(c => c));
            var choices = vm.Tabs[0].AgentChoices.Where(c => c.Llm == "claude").ToList();
            Assert.Equal("85 %", choices[0].LimitCaption);
            Assert.True(choices[0].LimitWarn);
            Assert.False(choices[1].HasLimit); // Unknown shows nothing
            Assert.Same(vm.AccountsEditor("claude")!.Rows[0].Limits.Session, vm.AccountsEditor("claude")!.LimitsOf("").Session); // the Accounts page's reading
            var agyChoice = vm.Tabs[2].AgentChoices.Single(c => c.Llm == "agy");
            Assert.Equal("44 %", agyChoice.LimitCaption);
            Assert.False(agyChoice.LimitWarn);
            Assert.False(vm.Tabs[3].AgentChoices.Single(c => c.Llm == "aider").HasLimit);

            // Activation within 5 seconds does not read again; after 5 seconds or a forced (timer) refresh does (#205).
            await vm.RefreshAccountLimitsAsync();
            Assert.Equal(3, calls.Count);
            clock = now.AddSeconds(6);
            await vm.RefreshAccountLimitsAsync();
            Assert.Equal(6, calls.Count);
            await vm.RefreshAccountLimitsAsync(force: true);
            Assert.Equal(9, calls.Count);

            // The closed box's meter follows the tab's selected account/agent (#202, #206), from the same reading.
            Assert.Equal(85, vm.Tabs[0].SelectedAgentChoice!.LimitPercent);
            Assert.True(vm.Tabs[0].SelectedAgentChoice!.LimitWarn);
            Assert.False(vm.Tabs[1].SelectedAgentChoice!.HasLimit);
            Assert.Equal(44, vm.Tabs[2].SelectedAgentChoice!.LimitPercent);
            Assert.False(vm.Tabs[2].SelectedAgentChoice!.LimitWarn);
            Assert.False(vm.Tabs[3].SelectedAgentChoice!.HasLimit); // aider has no meter

            // The status popup's line for the tab's account/agent, Unknown for a window nothing is known about.
            Assert.True(vm.Tabs[0].HasPeekLimit);
            Assert.True(vm.Tabs[0].PeekLimitWarn);
            Assert.StartsWith("5 h 85 % · Vecka 10 % · per ", vm.Tabs[0].PeekLimitLine);
            Assert.False(vm.Tabs[1].HasPeekLimit); // the work account is Unknown
            Assert.True(vm.Tabs[2].HasPeekLimit);
            Assert.StartsWith("5 h 28 % · Vecka 44 % · per ", vm.Tabs[2].PeekLimitLine);
            Assert.False(vm.Tabs[3].HasPeekLimit);

            // The window resets: the indicator goes away with it.
            clock = now.AddHours(3);
            Assert.False(vm.Tabs[0].AgentChoices.First(c => c.Llm == "claude").HasLimit);
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public void LimitUpdatesNotifyAllTabsAgentDropdownsAndDeduplicateSafely()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            var (notified, repeatNotified, codexEntry, agyEntry) = BoosterAI.Tests.UiSession.Run(() =>
            {
                Loc.Use("sv");
                var vm = new MainViewModel(new AppConfig
                {
                    Tabs =
                    [
                        new() { Name = "A", Directory = "/srv/a", Llm = "claude" },
                        new() { Name = "B", Directory = "/srv/b", Llm = "codex" },
                        new() { Name = "C", Directory = "/srv/c", Llm = "agy" },
                    ],
                });
                var now = DateTimeOffset.Now;
                vm.LimitsClock = () => now;
                var percent = 85.0;
                vm.AccountLimitsEnabled = true;
                vm.ReadAccountLimits = (_, llm, _) => Task.FromResult(
                    llm == "claude" ? new AccountLimits(Win(percent, now, 300), Win(10, now, 10080), null, now) : AccountLimits.Unknown);

                var raised = new List<string>();
                foreach (var tab in vm.Tabs)
                    tab.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TabViewModel.AgentChoices)) raised.Add(tab.Name); };

                var firstRead = vm.AccountsEditor("claude")!.RefreshLimitsAsync();
                Assert.True(firstRead.IsCompletedSuccessfully);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var first = raised.ToList();
                raised.Clear();

                // Same reading again: no redundant AgentChoices notifications.
                var secondRead = vm.AccountsEditor("claude")!.RefreshLimitsAsync();
                Assert.True(secondRead.IsCompletedSuccessfully);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var repeat = raised.ToList();

                // Concurrent signature updates across agents never race on _limitSignature.
                Parallel.For(0, 20, i => vm.AccountLimitsChanged(i % 2 == 0 ? "claude" : "codex"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                return (first, repeat,
                    vm.Tabs[1].AgentChoices.Single(c => c.Llm == "claude"),
                    vm.Tabs[2].AgentChoices.Single(c => c.Llm == "claude"));
            });

            Assert.Equal(["A", "B", "C"], notified.OrderBy(n => n));
            Assert.Empty(repeatNotified);
            Assert.Equal("85 %", codexEntry.LimitCaption);
            Assert.True(codexEntry.LimitWarn);
            Assert.Equal("85 %", agyEntry.LimitCaption);
            Assert.True(agyEntry.LimitWarn);
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public void MainWindowMeterUpdatesLiveAfterResetFromFullToZero()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            var (before, after) = BoosterAI.Tests.UiSession.Run(() =>
            {
                Loc.Use("sv");
                var now = DateTimeOffset.Now;
                var used = 100.0;
                var vm = new MainViewModel(new AppConfig
                {
                    Tabs = [new() { Name = "Atlas", Directory = "/srv/atlas", Llm = "codex" }],
                });
                vm.LimitsClock = () => now;
                vm.AccountLimitsEnabled = true;
                vm.ReadAccountLimits = (_, _, _) => Task.FromResult(
                    new AccountLimits(null, new LimitWindow(used, now.AddDays(5), 10080), null, now));

                var main = new MainWindow(vm);
                main.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                try
                {
                    (bool Visible, double Value, bool WarnClass, bool ClosedWarnVisible) ReadMeter()
                    {
                        var panel = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(main)
                            .OfType<Avalonia.Controls.Panel>().Single(p => p.Classes.Contains("limit-meter"));
                        var bar = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(panel)
                            .OfType<Avalonia.Controls.ProgressBar>().Single();
                        var closed = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(main)
                            .OfType<Avalonia.Controls.Panel>().First(p => p.Classes.Contains("limit-closed"));
                        var warnIcon = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(closed)
                            .OfType<Avalonia.Controls.Border>().First();
                        return (panel.IsVisible, bar.Value, bar.Classes.Contains("warn"), warnIcon.IsVisible);
                    }

                    Assert.True(vm.RefreshAccountLimitsAsync(force: true).IsCompletedSuccessfully);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    var first = ReadMeter();

                    // After a Codex reset + next turn, the rollout reports 0 % with a new reset time.
                    used = 0.0;
                    now = now.AddSeconds(10);
                    Assert.True(vm.RefreshAccountLimitsAsync().IsCompletedSuccessfully);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    var second = ReadMeter();
                    return (first, second);
                }
                finally { main.Close(); }
            });

            Assert.Equal((true, 100.0, true, true), before);
            Assert.Equal((true, 0.0, false, false), after);
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public async Task AgyStatusLineSavesQuotaBucketsAndChainsOwnStatusLine()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Temp("agy-statusline");
        try
        {
            var script = Path.Combine(root, AgyStatusLine.ScriptFile);
            File.WriteAllText(script, AgyStatusLine.Script + "\n");
            var agyHome = Path.Combine(root, ".gemini", "antigravity-cli");
            Directory.CreateDirectory(agyHome);
            var env = new Dictionary<string, string> { ["BOOSTERAI_AGY_HOME"] = agyHome };
            var command = AgyStatusLine.Command(root);
            var weekReset = DateTimeOffset.FromUnixTimeSeconds(Now + 400000).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var shortReset = DateTimeOffset.FromUnixTimeSeconds(Now + 7200).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var input = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["session_id"] = "SECRET_AGY_SESSION",
                ["email"] = "SECRET@example.com",
                ["transcript_path"] = "/SECRET/transcript.jsonl",
                ["product"] = "antigravity",
                ["quota"] = new Dictionary<string, object?>
                {
                    ["gemini-weekly"] = new { remaining_fraction = 0.72, reset_time = weekReset, reset_in_seconds = 400000 },
                    ["claude-weekly"] = new { remaining_fraction = 0.40, reset_time = weekReset, reset_in_seconds = 400000 },
                    ["disabled-weekly"] = new { remaining_fraction = 0.0, reset_time = weekReset, reset_in_seconds = 400000, disabled = true },
                    ["gemini-5h"] = new { remaining_fraction = 0.65, reset_time = shortReset, reset_in_seconds = 7200 },
                },
            });

            // No user statusLine: prints nothing (stack_with_default keeps AGY's built-in status line) and saves highest active used %.
            var (output, errors, code) = await Bash(root, command, input, env);
            Assert.Equal(0, code);
            Assert.Equal("", output);
            Assert.Equal("", errors);
            var saved = File.ReadAllText(Path.Combine(agyHome, AgyStatusLine.SnapshotFile));
            Assert.DoesNotContain("SECRET", saved);
            Assert.Empty(Directory.GetFiles(agyHome, "*.tmp"));

            var read = AccountLimitsProbe.Parse("agy", (await Bash(root, AccountLimitsProbe.Script("agy", null), env: env)).Out);
            Assert.Equal(35, read.Session!.UsedPercent); // 1 - 0.65
            Assert.Equal(60, read.Week!.UsedPercent); // highest active weekly (claude-weekly 1 - 0.40, disabled ignored)
            Assert.Null(read.Credits);

            // User's own statusLine in ~/.gemini/antigravity-cli/settings.json is chained with the same stdin.
            File.WriteAllText(Path.Combine(agyHome, "settings.json"), JsonSerializer.Serialize(new
            {
                statusLine = new { type = "command", command = $"cat > {Launcher.Quote(Path.Combine(root, "got.json"))} && printf 'my agy bar'" },
            }));
            (output, errors, code) = await Bash(root, command, input, env);
            Assert.Equal(("my agy bar", "", 0), (output, errors, code));
            Assert.Equal(input, File.ReadAllText(Path.Combine(root, "got.json")));

            // Disabled user statusLine is skipped; self-recursion is blocked.
            File.WriteAllText(Path.Combine(agyHome, "settings.json"), """{"statusLine":{"type":"command","enabled":false,"command":"echo nope"}}""");
            Assert.Equal("", (await Bash(root, command, input, env)).Out);
            File.WriteAllText(Path.Combine(agyHome, "settings.json"), JsonSerializer.Serialize(new { statusLine = new { type = "command", command } }));
            Assert.Equal(("", 0), ((await Bash(root, command, input, env)).Out, (await Bash(root, command, input, env)).Code));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AgySettingsCarryStatusLineStackWithDefaultAndFallBackWithoutPython3()
    {
        var root = Temp("agy-track");
        try
        {
            var session = TabSession.Open(root);
            var track = session.NewTrack()!;
            Assert.Equal(AgyStatusLine.Script.Replace("\r\n", "\n") + "\n", File.ReadAllText(Path.Combine(root, AgyStatusLine.ScriptFile)));
            var full = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, track.Id + ".agy.json"))).RootElement;
            Assert.Equal(AgyStatusLine.Command(track.Dir), full.GetProperty("statusLine").GetProperty("command").GetString());
            Assert.True(full.GetProperty("statusLine").GetProperty("stack_with_default").GetBoolean());
            var replace = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, track.Id + ".agy-replace.json"))).RootElement;
            Assert.False(replace.GetProperty("statusLine").TryGetProperty("stack_with_default", out _));
            var plain = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, track.Id + ".agy-plain.json"))).RootElement;
            Assert.False(plain.TryGetProperty("statusLine", out _));

            // Interactive agy gets --settings without ';'; one-shot agy -p omits --settings.
            var interactive = Launcher.TabScript(new TabConfig { Name = "Cirrus", Directory = "~", Llm = "agy" }, null, track);
            Assert.Contains("--settings ", interactive);
            Assert.Contains(track.Id + ".agy.json", interactive);
            Assert.DoesNotContain(';', interactive);
            var once = Launcher.TabScript(new TabConfig { Name = "Cirrus", Directory = "~", Llm = "agy", Orkester = true, NonInteractive = true }, null, track);
            Assert.DoesNotContain("--settings", once);

            if (OperatingSystem.IsWindows()) return;
            // Without python3, tab-lib.sh replaces tabN.agy.json with empty plain settings.
            var bin = Path.Combine(root, "bin");
            Directory.CreateDirectory(bin);
            foreach (var tool in new[] { "cut", "date", "cp", "cat" })
                File.CreateSymbolicLink(Path.Combine(bin, tool), FindOnPath(tool));
            var (_, _, code) = await Bash(root, $". {Launcher.Quote(track.Lib)} {Launcher.Quote(track.Dir)} {track.Id} Cirrus && echo ok", bin: bin);
            Assert.Equal(0, code);
            Assert.False(JsonDocument.Parse(File.ReadAllText(Path.Combine(root, track.Id + ".agy.json"))).RootElement.TryGetProperty("statusLine", out _));

            // With python3 and a user replacing statusLine in settings.json, tab-lib.sh selects tabN.agy-replace.json (no stack_with_default).
            var track2 = session.NewTrack()!;
            var agyHome = Path.Combine(root, "agy-home");
            Directory.CreateDirectory(agyHome);
            File.WriteAllText(Path.Combine(agyHome, "settings.json"), """{"statusLine":{"type":"command","command":"echo custom"}}""");
            (_, _, code) = await Bash(root, $". {Launcher.Quote(track2.Lib)} {Launcher.Quote(track2.Dir)} {track2.Id} Cirrus && echo ok",
                env: new() { ["BOOSTERAI_AGY_HOME"] = agyHome });
            Assert.Equal(0, code);
            var chosen = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, track2.Id + ".agy.json"))).RootElement;
            Assert.True(chosen.TryGetProperty("statusLine", out var chosenLine));
            Assert.False(chosenLine.TryGetProperty("stack_with_default", out _));
        }
        finally { Directory.Delete(root, true); }
    }
}
