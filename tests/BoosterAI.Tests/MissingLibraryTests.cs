using System.Diagnostics;
using BoosterAI;
using Xunit;

public class MissingLibraryTests
{
    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    [Theory]
    [InlineData("claude", "interactive")]
    [InlineData("codex", "interactive")]
    [InlineData("agy", "interactive")]
    [InlineData("claude", "prompt")]
    [InlineData("codex", "prompt")]
    [InlineData("agy", "prompt")]
    [InlineData("claude", "once")]
    [InlineData("codex", "once")]
    [InlineData("agy", "once")]
    [InlineData("claude", "resume")]
    [InlineData("codex", "resume")]
    [InlineData("agy", "resume")]
    [InlineData("codex", "source-failed")]
    [InlineData(Catalog.ShellOnly, "shell")]
    public void MissingLibraryRunsOriginalArgumentsAndLeavesAnUntrackedShell(string llm, string mode)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script")) return;
        var root = Directory.CreateTempSubdirectory("booster-missing-lib").FullName;
        var project = Directory.CreateDirectory(Path.Combine(root, "project's folder")).FullName;
        const string model = "model's \"choice\" $literal $(touch injected)";
        const string prompt = "review 'quotes' \"$HOME\" $(touch injected)";
        try
        {
            var tab = new TabConfig { Llm = llm, Directory = project, Model = model, Orkester = mode is "once" or "resume", NonInteractive = mode == "once" };
            var track = new TabTrack(Path.Combine(root, "missing-lib.sh"), root, "tab1");
            if (mode == "source-failed") File.WriteAllText(track.Lib, "return 1\n");
            var script = Launcher.TabScript(tab, mode == "prompt" ? prompt : null, track, resume: mode == "resume");
            Assert.DoesNotContain(';', script);
            var body = "fake_cli() { printf '%s\\0' \"$@\" > args.bin; pwd > cli-pwd; touch cli-ran; echo run >> cli-runs; return 7; }\n"
                + "shopt -s expand_aliases\nalias " + (llm == Catalog.ShellOnly ? "codex" : llm) + "='fake_cli --from-alias'\n" + script;
            var psi = new ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("-qefc");
            psi.ArgumentList.Add("bash --noprofile --norc -ic " + Quote(body));
            psi.ArgumentList.Add("/dev/null");
            using var process = Process.Start(psi)!;
            try
            {
                if (mode != "shell") Assert.True(SpinWait.SpinUntil(() => File.Exists(Path.Combine(project, "cli-ran")), 5000));
                process.StandardInput.WriteLine("pwd > " + Quote(Path.Combine(project, "shell-pwd")) + " && exit 3");
                process.StandardInput.Flush();
                Assert.True(process.WaitForExit(5000));
                Assert.Equal(3, process.ExitCode); // A nonzero shell exit must not run the fallback twice.
                Assert.Equal(project, File.ReadAllText(Path.Combine(project, "shell-pwd")).Trim());
                if (mode != "shell")
                {
                    var expected = new List<string> { "--from-alias" };
                    if (mode == "resume") expected.AddRange(llm == "codex" ? ["resume", "--last"] : ["--continue"]);
                    if (mode == "once" && llm == "codex") expected.Add("exec");
                    expected.AddRange(["--model", model]);
                    if (mode == "prompt")
                    {
                        if (llm == "agy") expected.Add("-i");
                        expected.Add(prompt);
                    }
                    if (mode == "once")
                    {
                        if (llm != "codex") expected.Add("-p");
                        expected.Add(llm == "claude" ? "/orkester" : "/orkester " + Launcher.ForegroundWatchNote);
                    }
                    Assert.Single(File.ReadAllLines(Path.Combine(project, "cli-runs")));
                    Assert.Equal(expected, File.ReadAllText(Path.Combine(project, "args.bin")).Split('\0')[..^1]);
                    Assert.Equal(project, File.ReadAllText(Path.Combine(project, "cli-pwd")).Trim());
                }
                Assert.False(File.Exists(Path.Combine(project, "injected")));
                Assert.Empty(Directory.GetFiles(root, "tab1.*")); // No fabricated log, timing, hooks or output.
                Assert.DoesNotContain("command not found", process.StandardError.ReadToEnd());
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        }
        finally { Directory.Delete(root, true); }
    }
}
