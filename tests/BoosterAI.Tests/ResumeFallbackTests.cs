using Xunit;
using System.Diagnostics;

namespace BoosterAI.Tests;

/// <summary>A resumed start that does not get going falls back once to a fresh start (#235).</summary>
public class ResumeFallbackTests
{
    private static readonly TabTrack Track = new("/s/tab-lib.sh", "/s", "tab1");

    private static TabConfig Tab(string llm, bool orkester) => new() { Directory = "~/p", Llm = llm, Model = "x", Orkester = orkester };

    private static string Split(string script, bool after) =>
        after ? script[script.IndexOf("bs_rc=$?", StringComparison.Ordinal)..][..script[script.IndexOf("bs_rc=$?", StringComparison.Ordinal)..].IndexOf("\nelse (exit", StringComparison.Ordinal)]
            : script[..script.IndexOf("bs_rc=$?", StringComparison.Ordinal)];

    [Theory]
    [InlineData("claude", true, "claude --continue", "bs_run 'claude --model")]
    [InlineData("claude", false, "claude --continue", "bs_run 'claude --model")]
    [InlineData("agy", true, "agy --continue", "bs_run 'agy --model")]
    [InlineData("codex", true, "codex resume --last", "bs_run 'codex --model")]
    [InlineData("codex", false, "codex resume --last", "bs_run 'codex --model")]
    [InlineData("aider", true, "aider --restore-chat-history", "bs_run 'aider --model")]
    public void ResumedStartIsFollowedByOneFreshStart(string llm, bool orkester, string resumed, string fresh)
    {
        foreach (var script in new[] { Launcher.RestartScript(Tab(llm, orkester), Track, resume: true), Launcher.TabScript(Tab(llm, orkester), null, Track, resume: true) })
        {
            Assert.DoesNotContain(';', script);
            Assert.Contains(resumed, Split(script, false));
            Assert.DoesNotContain("orkester", Split(script, false));
            Assert.Contains("[ $bs_rc -lt 128 ]", script);
            Assert.InRange(script.Split("[ $bs_rc -lt 128 ]").Length - 1, 1, 2); // a tab script also holds the plain branch for a missing lib
            Assert.Contains(fresh, Split(script, true));
            Assert.DoesNotContain("--continue", Split(script, true));
            Assert.DoesNotContain("resume --last", Split(script, true));
            Assert.Equal(orkester, Split(script, true).Contains("orkester"));
        }
    }

    [Fact]
    public void FreshStartsAndShellsAreNotWrapped()
    {
        Assert.DoesNotContain("bs_rc", Launcher.RestartScript(Tab("claude", true), Track, resume: false));
        Assert.DoesNotContain("bs_rc", Launcher.TabScript(Tab("claude", true), null, Track));
        Assert.DoesNotContain("bs_rc", Launcher.TabScript(new TabConfig { Llm = Catalog.ShellOnly }, null, Track, resume: true));
    }

    /// <summary>Runs the generated script on a tty with a stub CLI; returns the stub's argument lines, one per run.</summary>
    private static string[] Run(string llm, bool conversation, int resumedExit, bool orkester = true)
    {
        var root = Directory.CreateTempSubdirectory("booster-resume-fallback").FullName;
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
            var bin = Directory.CreateDirectory(Path.Combine(root, "shell-bin")).FullName;
            File.WriteAllText(Path.Combine(root, "tab-lib.sh"), TabSession.Lib);
            var runs = Path.Combine(project, "runs");
            var stub = Path.Combine(bin, llm);
            File.WriteAllText(stub, "#!/bin/bash\nprintf '%s ' \"$@\" >> " + Launcher.Quote(runs) + "\necho >> " + Launcher.Quote(runs) + "\n"
                + "case \"$*\" in\n*--continue*|*resume*)\n" + (conversation ? "exit 0" : "echo 'No conversation found to continue'\nexit " + resumedExit) + "\n;;\nesac\nexit 0\n");
            File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var tab = new TabConfig { Directory = project, Llm = llm, Model = "x", Orkester = orkester };
            var script = Launcher.TabScript(tab, null, new TabTrack(Path.Combine(root, "tab-lib.sh"), root, "tab1"), resume: true);
            var psi = new ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("-qefc");
            psi.ArgumentList.Add("bash --noprofile --norc -c " + Launcher.Quote(script));
            psi.ArgumentList.Add("/dev/null");
            TestShell.Configure(psi, root, bin, blockAgents: false);
            using var process = Process.Start(psi)!;
            try
            {
                Assert.True(SpinWait.SpinUntil(() => File.Exists(runs), 30000));
                Thread.Sleep(1500); // time for a second run if there were one; the idle shell then waits for input
                process.StandardInput.WriteLine("exit 3");
                process.StandardInput.Flush();
                Assert.True(process.WaitForExit(30000));
                return File.ReadAllLines(runs);
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        }
        finally { TestShell.StopLeftovers(root); Directory.Delete(root, true); }
    }

    private static bool Available => OperatingSystem.IsLinux() && File.Exists("/usr/bin/script");

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("agy")]
    public void WithoutAConversationTheTabStartsFreshOnceWithTheOrchestra(string llm)
    {
        if (!Available) return;
        var runs = Run(llm, conversation: false, resumedExit: 1);
        Assert.Equal(2, runs.Length);
        Assert.Matches("--continue|resume --last", runs[0]);
        Assert.DoesNotMatch("--continue|resume --last", runs[1]);
        Assert.Contains("/orkester", runs[1]);
    }

    [Fact]
    public void WithoutAConversationAndWithoutTheOrchestraTheFreshStartHasNoPrompt()
    {
        if (!Available) return;
        var runs = Run("claude", conversation: false, resumedExit: 1, orkester: false);
        Assert.Equal(2, runs.Length);
        Assert.DoesNotContain("orkester", runs[1]);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void AWorkingContinuationRunsOnceWithoutAnotherPrompt(string llm)
    {
        if (!Available) return;
        var runs = Run(llm, conversation: true, resumedExit: 0);
        Assert.Single(runs);
        Assert.DoesNotContain("orkester", runs[0]);
    }

    [Fact]
    public void AnExitBySignalIsNotAFailedContinuation()
    {
        if (!Available) return;
        Assert.Single(Run("claude", conversation: false, resumedExit: 130));
    }
}
