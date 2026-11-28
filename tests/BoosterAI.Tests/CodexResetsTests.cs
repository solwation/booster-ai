using System.Diagnostics;
using Avalonia.Threading;
using BoosterAI;
using Xunit;

/// <summary>
/// Codex's earned rate-limit resets (#274): the app-server driver against a fake <c>codex</c>, the parsers, and the Accounts row's
/// "N resets kvar" and "Använd reset" with fakes for reading and redeeming, so no real reset is ever used.
/// </summary>
[Collection(ConfigFileCollection.Name)]
public class CodexResetsTests
{
    private static string Read(string result) => "banner\n" + CodexResets.Marker + "{\"result\":" + result + "}\n";

    [Fact]
    public void ReadsTheAvailableCountAndWhetherUsageIsUsedUp()
    {
        var plain = CodexResets.ParseRead(Read("""
            {"ordinaryUsageAllowed":true,"rateLimits":{"primary":{"usedPercent":48},"secondary":null,"rateLimitReachedType":null},
             "rateLimitResetCredits":{"availableCount":2,"credits":null}}
            """.ReplaceLineEndings("")));
        Assert.Equal(new ResetCredits(2, false), plain);
        Assert.Equal(new ResetCredits(1, true), CodexResets.ParseRead(Read("""{"ordinaryUsageAllowed":false,"rateLimitResetCredits":{"availableCount":1}}""")));
        Assert.Equal(new ResetCredits(1, true), CodexResets.ParseRead(Read("""{"rateLimits":{"secondary":{"usedPercent":100}},"rateLimitResetCredits":{"availableCount":1}}""")));
        Assert.Equal(new ResetCredits(0, true), CodexResets.ParseRead(Read("""{"rateLimits":{"rateLimitReachedType":"rate_limit_reached"},"rateLimitResetCredits":{"availableCount":0}}""")));
    }

    [Fact]
    public void AnUnknownCountIsNullNeverZero()
    {
        Assert.Null(CodexResets.ParseRead(null));
        Assert.Null(CodexResets.ParseRead("no marker"));
        Assert.Null(CodexResets.ParseRead(Read("""{"rateLimitResetCredits":null}""")));
        Assert.Null(CodexResets.ParseRead(Read("""{"rateLimitResetCredits":{"availableCount":"2"}}""")));
        Assert.Null(CodexResets.ParseRead(Read("""{"rateLimitResetCredits":{"availableCount":-1}}""")));
        Assert.Null(CodexResets.ParseRead(CodexResets.Marker + """{"error":"not logged in"}"""));
        Assert.Null(CodexResets.ParseRead(CodexResets.Marker + "{broken"));
    }

    [Fact]
    public void OnlyResetAndAlreadyRedeemedCountAsSuccess()
    {
        Assert.Equal(ResetOutcome.Reset, CodexResets.ParseConsume(Read("""{"outcome":"reset"}""")));
        Assert.Equal(ResetOutcome.AlreadyRedeemed, CodexResets.ParseConsume(Read("""{"outcome":"alreadyRedeemed"}""")));
        Assert.Equal(ResetOutcome.NothingToReset, CodexResets.ParseConsume(Read("""{"outcome":"nothingToReset"}""")));
        Assert.Equal(ResetOutcome.NoCredit, CodexResets.ParseConsume(Read("""{"outcome":"noCredit"}""")));
        Assert.Equal(ResetOutcome.Failed, CodexResets.ParseConsume(Read("""{"outcome":"somethingNew"}""")));
        Assert.Equal(ResetOutcome.Failed, CodexResets.ParseConsume(CodexResets.Marker + """{"error":"boom"}"""));
        Assert.Equal(ResetOutcome.Failed, CodexResets.ParseConsume(null));
        Assert.True(CodexResets.Succeeded(ResetOutcome.Reset));
        Assert.True(CodexResets.Succeeded(ResetOutcome.AlreadyRedeemed));
        Assert.False(CodexResets.Succeeded(ResetOutcome.NothingToReset));
        Assert.False(CodexResets.Succeeded(ResetOutcome.NoCredit));
        Assert.False(CodexResets.Succeeded(ResetOutcome.Failed));
    }

    [Fact]
    public void TheScriptQuotesTheAccountAndAcceptsOnlyAGuidKey()
    {
        var key = Guid.NewGuid().ToString("D");
        var script = CodexResets.Script("consume", "~/.codex-work", key);
        Assert.Contains("python3 - consume ~/'.codex-work' " + key + " <<'PY'", script);
        Assert.Contains("timeout -k 2 25", script);
        Assert.Contains("python3 - read '' '' <<'PY'", CodexResets.Script("read", null));
        Assert.Throws<ArgumentException>(() => CodexResets.Script("consume", null, "x; rm -rf ~"));
        Assert.Throws<ArgumentException>(() => CodexResets.Script("consume", null));
        Assert.Throws<ArgumentException>(() => CodexResets.Script("read", "relative; rm"));
        Assert.Throws<ArgumentException>(() => CodexResets.Script("other", null));
    }

    /// <summary>A fake codex app-server: answers initialize and the request, logs what it got and its pid, and then waits (never exits by itself).</summary>
    private const string FakeCodex = """
#!/usr/bin/env python3
import json, os, sys, time
root = os.environ['FAKE_ROOT']
open(os.path.join(root, 'codex.pid'), 'w').write(str(os.getpid()))
log = open(os.path.join(root, 'codex.log'), 'a')
log.write('home=' + os.environ.get('CODEX_HOME', '') + ' args=' + ' '.join(sys.argv[1:]) + '\n'); log.flush()
for line in sys.stdin:
    msg = json.loads(line)
    log.write(json.dumps(msg) + '\n'); log.flush()
    if msg.get('method') == 'initialize':
        print(json.dumps({'id': msg['id'], 'result': {'userAgent': 'fake'}}), flush=True)
    elif msg.get('method') == 'account/rateLimits/read':
        print(json.dumps({'method': 'account/rateLimits/updated', 'params': {}}), flush=True)
        print(json.dumps({'id': msg['id'], 'result': {'ordinaryUsageAllowed': False, 'rateLimitResetCredits': {'availableCount': 3, 'credits': None}}}), flush=True)
    elif msg.get('method') == 'account/rateLimitResetCredit/consume':
        print(json.dumps({'id': msg['id'], 'result': {'outcome': 'reset'}}), flush=True)
time.sleep(600)
""";

    private static async Task<(string Output, string Log, int? Pid)> RunDriver(string mode, string? directory, string? key = null)
    {
        var root = Directory.CreateTempSubdirectory("booster-resets-").FullName;
        using var process = new Process { StartInfo = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
        var bin = Path.Combine(root, "bin");
        Directory.CreateDirectory(bin);
        var codex = Path.Combine(bin, "codex");
        File.WriteAllText(codex, FakeCodex.ReplaceLineEndings("\n"));
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(codex, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        TestShell.Configure(process.StartInfo, root, bin);
        process.StartInfo.Environment["FAKE_ROOT"] = root;
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(CodexResets.Script(mode, directory, key));
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            var log = File.Exists(Path.Combine(root, "codex.log")) ? File.ReadAllText(Path.Combine(root, "codex.log")) : "";
            int? pid = int.TryParse(File.Exists(Path.Combine(root, "codex.pid")) ? File.ReadAllText(Path.Combine(root, "codex.pid")) : "", out var p) ? p : null;
            return (await output, log, pid);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit();
            TestShell.StopLeftovers(root);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertGone(int? pid)
    {
        Assert.NotNull(pid);
        // The driver killed the server's process group in its finally: nothing is left behind (a zombie has no cmdline).
        var cmdline = $"/proc/{pid}/cmdline";
        Assert.True(!File.Exists(cmdline) || File.ReadAllText(cmdline).Length == 0, "the fake app-server is still running");
    }

    [Fact]
    public async Task TheDriverReadsTheCountWithTheAccountsHomeAndStopsTheServer()
    {
        if (!OperatingSystem.IsLinux()) return;
        var (output, log, pid) = await RunDriver("read", "~/.codex-work");
        Assert.Equal(new ResetCredits(3, true), CodexResets.ParseRead(output));
        Assert.Contains("args=app-server", log);
        Assert.Contains("/shell-home/.codex-work args=", log);
        Assert.Contains("\"excludeResetCreditDetails\": true", log);
        Assert.DoesNotContain("consume", log);
        AssertGone(pid);
    }

    [Fact]
    public async Task TheDriverRedeemsExactlyOnceWithTheGivenKey()
    {
        if (!OperatingSystem.IsLinux()) return;
        var key = Guid.NewGuid().ToString("D");
        var (output, log, pid) = await RunDriver("consume", null, key);
        Assert.Equal(ResetOutcome.Reset, CodexResets.ParseConsume(output));
        Assert.Single(log.Split('\n'), l => l.Contains("account/rateLimitResetCredit/consume"));
        Assert.Contains("\"idempotencyKey\": \"" + key + "\"", log);
        Assert.Contains("home= args=app-server", log); // the default account: no CODEX_HOME of its own
        AssertGone(pid);
    }

    private sealed class Fakes
    {
        public readonly List<string?> Reads = [];
        public readonly List<(string? Dir, Guid Key)> Consumes = [];
        public ResetCredits? Count = new(2, true);
        public ResetOutcome Outcome = ResetOutcome.Reset;
    }

    private static (MainViewModel Vm, AgentAccountsViewModel Accounts, Fakes Fakes) Setup(DateTimeOffset now)
    {
        var vm = new MainViewModel(new AppConfig { Accounts = [new() { Id = "codex-work", Agent = "codex", Name = "Work", Directory = "~/.codex-work" }] });
        var fakes = new Fakes();
        vm.LimitsClock = () => now;
        vm.ReadAccountLimits = (_, _, _) => Task.FromResult(AccountLimits.Unknown);
        vm.ReadCodexResets = (_, dir) => { lock (fakes) fakes.Reads.Add(dir); return Task.FromResult(fakes.Count); };
        vm.ConsumeCodexReset = (_, dir, key) => { lock (fakes) fakes.Consumes.Add((dir, key)); return Task.FromResult(fakes.Outcome); };
        return (vm, vm.AccountsEditor("codex")!, fakes);
    }

    [Fact]
    public async Task AccountsShowTheCountAndReadItAtMostEveryFiveMinutes()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            var now = DateTimeOffset.Now;
            var (vm, accounts, fakes) = Setup(now);
            await accounts.RefreshLimitsAsync();
            Assert.Empty(fakes.Reads); // tests and screenshots never start an app-server
            vm.AccountLimitsEnabled = true;
            await accounts.RefreshLimitsAsync();
            Assert.Empty(fakes.Reads); // only the real app's background checks enable the app-server reads

            vm.CodexResetsEnabled = true;
            fakes.Count = new ResetCredits(2, false);
            await accounts.RefreshLimitsAsync();
            Assert.Equal([null, "~/.codex-work"], fakes.Reads);
            Assert.Equal("2 resets kvar", accounts.Rows[0].ResetsText);
            Assert.False(accounts.Rows[0].CanUseReset); // the quota is not used up

            await accounts.RefreshLimitsAsync();
            Assert.Equal(2, fakes.Reads.Count);
            vm.LimitsClock = () => now + CodexResets.Interval;
            fakes.Count = null;
            await accounts.RefreshLimitsAsync();
            Assert.Equal(4, fakes.Reads.Count);
            Assert.Equal("", accounts.Rows[0].ResetsText); // unknown: nothing, never "0"
            Assert.False(accounts.Rows[0].HasResets);

            Loc.Use("en");
            vm.LimitsClock = () => now + 2 * CodexResets.Interval;
            fakes.Count = new ResetCredits(1, true);
            await accounts.RefreshLimitsAsync();
            Assert.Equal("1 reset left", accounts.Rows[1].ResetsText);
            Assert.True(accounts.Rows[1].CanUseReset);

            // Claude's accounts never start a Codex app-server.
            await vm.AccountsEditor("claude")!.RefreshLimitsAsync();
            Assert.Equal(6, fakes.Reads.Count);
            Assert.Null(vm.AccountsEditor("claude")!.Rows[0].Resets);
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public async Task TheStatusPopupShowsTheTabsResetsFromTheAccountsReading()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            var now = DateTimeOffset.Now;
            var vm = new MainViewModel(new AppConfig
            {
                Accounts = [new() { Id = "codex-work", Agent = "codex", Name = "Work", Directory = "~/.codex-work" }],
                Tabs =
                [
                    new() { Name = "Atlas", Directory = "/srv/atlas", Llm = "codex" },
                    new() { Name = "Harbor", Directory = "/srv/harbor", Llm = "codex", Account = "codex-work" },
                    new() { Name = "Cirrus", Directory = "/srv/cirrus", Llm = "claude" },
                ],
            });
            var counts = new Dictionary<string, ResetCredits?> { [""] = new(2, false), ["~/.codex-work"] = null };
            vm.LimitsClock = () => now;
            vm.AccountLimitsEnabled = vm.CodexResetsEnabled = true;
            vm.ReadAccountLimits = (_, _, _) => Task.FromResult(new AccountLimits(new LimitWindow(100, now.AddHours(2), 300), null, null, now));
            vm.ReadCodexResets = (_, dir) => Task.FromResult(counts[dir ?? ""]);
            var (atlas, harbor, cirrus) = (vm.Tabs[0], vm.Tabs[1], vm.Tabs[2]);
            var changed = new List<string?>();
            atlas.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            Assert.False(atlas.HasPeekResets);

            await vm.AccountsEditor("codex")!.RefreshLimitsAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("2 resets kvar", atlas.PeekResetsText); // known: the default account
            Assert.Contains(nameof(TabViewModel.PeekResetsText), changed);
            Assert.True(atlas.HasPeekBlock);
            Assert.Contains("2 resets kvar", atlas.PeekLimitName);
            Assert.Contains("2 resets kvar", atlas.StatusActionName);
            Assert.Equal(Loc.Get("peek.reset.hint"), atlas.PeekResetHint); // the 5 h window is used up and a reset is left
            Assert.True(atlas.HasPeekLimit);

            Assert.Equal("", harbor.PeekResetsText); // unknown: nothing, never "0"
            Assert.DoesNotContain("reset", harbor.StatusActionName);
            Assert.Equal("", cirrus.PeekResetsText); // other agents are unaffected
            Assert.False(cirrus.HasPeekResets);

            counts[""] = new ResetCredits(1, true);
            counts["~/.codex-work"] = new ResetCredits(0, false);
            await vm.AccountsEditor("codex")!.RefreshResetsAsync(force: true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("1 reset kvar", atlas.PeekResetsText);
            Assert.Equal(Loc.Get("peek.reset.hint"), atlas.PeekResetHint); // exhausted with a reset left: it can be used from Konton
            Assert.Equal("0 resets kvar", harbor.PeekResetsText); // explicitly zero
            Assert.Equal("", harbor.PeekResetHint);
            Assert.Equal("", cirrus.PeekResetsText);
            Loc.Use("en");
            Assert.Equal("1 reset left", atlas.PeekResetsText);
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public async Task UsingAResetAsksFirstAndNoChangesNothing()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            var (vm, accounts, fakes) = Setup(DateTimeOffset.Now);
            vm.AccountLimitsEnabled = vm.CodexResetsEnabled = true;
            await accounts.RefreshLimitsAsync();
            var row = accounts.Rows[1];
            Assert.True(row.CanUseReset);

            (string Name, int Count)? asked = null;
            Assert.Null(await row.UseResetAsync((name, n) => { asked = (name, n); return Task.FromResult(false); }));
            Assert.Equal(("Work", 2), asked);
            Assert.Empty(fakes.Consumes);
            Assert.False(row.HasResetMessage);
            Assert.Equal("Kontot Work har 2 resets kvar. Vill du använda en nu?",
                Loc.Format("account.reset.confirm.body", "Work", Loc.FormatCount("account.resets", 2)));

            // Not offered while the quota is not used up: nothing is asked or sent.
            fakes.Count = new ResetCredits(2, false);
            await accounts.RefreshResetsAsync(force: true);
            var calls = 0;
            Assert.Null(await row.UseResetAsync((_, _) => { calls++; return Task.FromResult(true); }));
            Assert.Equal(0, calls);
            Assert.Empty(fakes.Consumes);
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public async Task YesRedeemsExactlyOneAndReadsTheCountAgain()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            var (vm, accounts, fakes) = Setup(DateTimeOffset.Now);
            vm.AccountLimitsEnabled = vm.CodexResetsEnabled = true;
            await accounts.RefreshLimitsAsync();
            var reads = fakes.Reads.Count;
            fakes.Count = new ResetCredits(1, false);
            var outcome = await accounts.Rows[1].UseResetAsync((_, _) => Task.FromResult(true));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ResetOutcome.Reset, outcome);
            var consume = Assert.Single(fakes.Consumes);
            Assert.Equal("~/.codex-work", consume.Dir);
            Assert.NotEqual(Guid.Empty, consume.Key);
            Assert.True(fakes.Reads.Count > reads); // read again at once, not after five minutes
            var row = accounts.Rows[1];
            Assert.Equal("1 reset kvar", row.ResetsText);
            Assert.False(row.CanUseReset);
            Assert.Equal("En reset användes för kontot Work.", row.ResetMessage);
            Assert.False(row.ResetFailed);
            Assert.StartsWith("En reset användes för kontot Work. Skriv continue", vm.Status);
            Assert.False(vm.Error.IsVisible);
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public async Task AFailureShowsTheBannerAndRetryRepeatsTheSameAttempt()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            var (vm, accounts, fakes) = Setup(DateTimeOffset.Now);
            vm.AccountLimitsEnabled = vm.CodexResetsEnabled = true;
            await accounts.RefreshLimitsAsync();
            fakes.Outcome = ResetOutcome.Failed;
            var reads = fakes.Reads.Count;
            Assert.Equal(ResetOutcome.Failed, await accounts.Rows[1].UseResetAsync((_, _) => Task.FromResult(true)));
            Dispatcher.UIThread.RunJobs();

            var row = accounts.Rows[1];
            Assert.True(row.ResetFailed);
            Assert.Contains("bekräftade inte", row.ResetMessage);
            Assert.True(vm.Error.IsVisible);
            Assert.Equal(ErrorKind.LimitReset, vm.Error.Kind);
            Assert.Equal(reads, fakes.Reads.Count); // no success, so no "continue" hint and no extra read
            Assert.True(row.CanUseReset); // still offered: nothing was confirmed

            fakes.Outcome = ResetOutcome.AlreadyRedeemed;
            vm.Error.Retry();
            for (var i = 0; i < 50 && fakes.Consumes.Count < 2; i++) await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, fakes.Consumes.Count);
            Assert.Equal(fakes.Consumes[0].Key, fakes.Consumes[1].Key); // the same attempt: never a second reset
            Assert.False(accounts.Rows[1].ResetFailed);

            // A refusal has nothing to retry: only the agent settings.
            fakes.Outcome = ResetOutcome.NoCredit;
            fakes.Count = new ResetCredits(1, true);
            await accounts.RefreshResetsAsync(force: true);
            Assert.Equal(ResetOutcome.NoCredit, await accounts.Rows[1].UseResetAsync((_, _) => Task.FromResult(true)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ErrorKind.LimitResetRefused, vm.Error.Kind);
            Assert.Contains("inte har några resets kvar", accounts.Rows[1].ResetMessage);
            Assert.NotEqual(fakes.Consumes[1].Key, fakes.Consumes[2].Key); // a new yes is a new attempt
        }
        finally { Loc.Use(previous); }
    }
}
