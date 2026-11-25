using BoosterAI;
using Xunit;

[Collection(ConfigFileCollection.Name)]
public class ClaudeFableUsageTests
{
    private const string Success = """{"type":"result","subtype":"success","num_turns":0,"is_error":false}""";
    private const string Fable = """
        {"kind":"weekly_scoped","percent":82.5,"resets_at":"2026-12-01T12:34:00Z","scope":{"model":{"display_name":"Claude Fable 5.2"}}}
        """;

    private static string Report(string rows) =>
        $$$$"""{"type":"assistant","usage_report":{"rate_limits":{"limits":[{{{{rows.ReplaceLineEndings("")}}}}]}}}""";

    [Fact]
    public void ReadsScopedFableWithItsOwnResetTime()
    {
        var output = "login shell banner\n{banner\n" + Report("""
            {"kind":"weekly_all","percent":99,"scope":{"model":{"display_name":"Fable"}}},
            {"kind":"weekly_scoped","percent":94,"scope":{"model":{"display_name":"Opus"}}},
            """ + Fable) + "\n" + Success;
        var fable = Assert.IsType<LimitWindow>(ClaudeFableUsage.Parse(output));
        Assert.Equal(82.5, fable.UsedPercent);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 12, 34, 0, TimeSpan.Zero), fable.ResetsAt);
    }

    [Fact]
    public void IgnoresMissingInvalidAndFailedReports()
    {
        Assert.Null(ClaudeFableUsage.Parse(Report("") + "\n" + Success));
        Assert.Null(ClaudeFableUsage.Parse(Report(Fable)));
        Assert.Null(ClaudeFableUsage.Parse(Report(Fable) + "\n" + Success.Replace(":0", ":1")));
        Assert.Null(ClaudeFableUsage.Parse(Report(Fable.Replace("82.5", "101")) + "\n" + Success));
        Assert.Null(ClaudeFableUsage.Parse(Report(Fable.Replace("82.5", "\"82.5\"")) + "\n" + Success));
    }

    [Fact]
    public void ShowsFableOutsideTheSharedWindowsAndDropsItAtReset()
    {
        var fable = ClaudeFableUsage.Parse(Report(Fable.Replace("\"2026-12-01T12:34:00Z\"", "null")) + "\n" + Success);
        Assert.NotNull(fable);
        var now = DateTimeOffset.UtcNow;
        var limits = AccountLimits.Unknown with { Fable = fable };
        Assert.Null(LimitIndicator.For(limits, now));
        var detail = Assert.IsType<LimitIndicator>(LimitIndicator.For(limits, now, includeFable: true));
        Assert.False(detail.HasSharedWindows);
        var row = Assert.Single(detail.Rows, r => r.Known);
        Assert.Equal("Fable", row.Label);
        Assert.Empty(row.ResetText);
        Assert.Equal(17, row.Remaining);
        limits = limits with { Fable = fable with { ResetsAt = now } };
        Assert.Null(LimitIndicator.For(limits, now, includeFable: true));
    }

    [Fact]
    public async Task ReusesPendingAndCompletedReadsOnlyForTheSameAccountAndDistribution()
    {
        var calls = 0;
        var usage = new ClaudeFableUsage((distro, directory) =>
        {
            Interlocked.Increment(ref calls);
            return directory is null ? null : new LimitWindow(distro == "Ubuntu" ? 11 : 22, null);
        });
        var now = DateTimeOffset.UtcNow;
        var first = usage.ReadAsync("Ubuntu", "/accounts/team", now);
        Assert.Same(first, usage.ReadAsync("Ubuntu", "/accounts/team", now.AddSeconds(30)));
        Assert.Equal(11, (await first)?.UsedPercent);
        Assert.Equal(22, (await usage.ReadAsync("Debian", "/accounts/team", now))?.UsedPercent);
        Assert.Null(await usage.ReadAsync("Ubuntu", null, now));
        Assert.Equal(11, (await usage.ReadAsync("Ubuntu", "/accounts/team", now.Add(ClaudeFableUsage.Interval)))?.UsedPercent);
        Assert.Equal(4, calls);
    }

    [Fact]
    public void ScopeUsesTheSelectedAccountAndThePlatformShell()
    {
        var command = ClaudeFableUsage.Command("ExampleDistro", "~/Claude's Team");
        var script = command.ArgumentList[^1];
        Assert.Contains("export CLAUDE_CONFIG_DIR=~/", script);
        Assert.Contains("test -d \"$CLAUDE_CONFIG_DIR\"", script);
        Assert.Contains(" /usage", script);
        Assert.DoesNotContain("CLAUDE_CONFIG_DIR", ClaudeFableUsage.Script(null));
        Assert.Throws<ArgumentException>(() => ClaudeFableUsage.Script("relative"));
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("wsl.exe", command.FileName);
            Assert.Equal("ExampleDistro", command.ArgumentList[1]);
        }
        else Assert.Equal("bash", command.FileName);
    }
}
