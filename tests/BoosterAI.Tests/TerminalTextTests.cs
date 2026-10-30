using System.Text;
using BoosterAI;
using Xunit;

public class TerminalTextTests
{
    private static IReadOnlyList<string> Render(string s, int rows = 10, int cols = 40, int max = 20) => TerminalText.Render(s, rows, cols, max);

    [Fact]
    public void PlainLinesAndColoursReadAsText() =>
        Assert.Equal(["first", "second in red", "third"], Render("first\r\n\x1b[31msecond\x1b[0m in red\r\n\x1b[1;32mthird\x1b[m\r\n"));

    [Fact]
    public void CarriageReturnOverwritesAProgressLine() =>
        Assert.Equal(["done 100%"], Render("working 10%\rworking 50%\rdone 100%   \x1b[K"));

    /// <summary>Ink (claude) redraws its live area by erasing the previous lines and writing the new frame.</summary>
    [Fact]
    public void InkStyleRedrawShowsOnlyTheLastFrame()
    {
        var sb = new StringBuilder("● static message\r\n");
        var prev = 0;
        for (var i = 0; i < 5; i++)
        {
            sb.Append(string.Concat(Enumerable.Repeat("\x1b[2K\x1b[1A", prev))).Append("\x1b[2K\r");
            sb.Append($"✻ Working… ({i})\r\n> prompt {i}\r\n");
            prev = 2;
        }
        Assert.Equal(["● static message", "✻ Working… (4)", "> prompt 4"], Render(sb.ToString()));
    }

    [Fact]
    public void AbsoluteMovesEraseAndClearScreen()
    {
        Assert.Equal(["ab", "  X"], Render("ab\r\n\x1b[2;3HX"));
        Assert.Equal(["  top"], Render("\r\n\r\n\x1b[3;3Htop"));
        Assert.Equal(["new"], Render("old screen\r\nmore\x1b[H\x1b[2Jnew", max: 1));
        Assert.Equal(["abc"], Render("abcdef\x1b[4G\x1b[K"));
        Assert.Equal(["ab", "", "cd"], Render("ab\x1b[3;1Hcd"));
    }

    [Fact]
    public void BlankRunsCollapseToOneLine() =>
        Assert.Equal(["a", "", "b"], Render("\r\n\r\na\r\n\r\n\r\n\r\nb\r\n\r\n"));

    [Fact]
    public void FullClearKeepsTheOldScreenAsScrollback() =>
        Assert.Equal(["old", "new"], Render("old\x1b[2J\x1b[Hnew"));

    [Fact]
    public void AlternateScreenShowsItsContentAndLeavingRestoresTheMainScreen()
    {
        Assert.Equal(["full-screen app"], Render("shell line\r\n\x1b[?1049h\x1b[H\x1b[2Jfull-screen app"));
        Assert.Equal(["shell line", "after"], Render("shell line\r\n\x1b[?1049h\x1b[2Jtui stuff\x1b[?1049lafter"));
    }

    [Fact]
    public void LongLinesWrapAtTheTerminalWidth() =>
        Assert.Equal(["0123456789", "abc"], Render("0123456789abc", cols: 10));

    [Fact]
    public void OutputBeyondTheScreenScrollsAndOnlyTheLastLinesAreReturned()
    {
        var text = string.Concat(Enumerable.Range(1, 50).Select(i => $"line {i}\r\n"));
        var lines = Render(text, rows: 10, max: 5);
        Assert.Equal(["line 46", "line 47", "line 48", "line 49", "line 50"], lines);
    }

    /// <summary>codex's inline viewport inserts history above it through a scroll region and reverse index.</summary>
    [Fact]
    public void ScrollRegionsInsertLinesAboveAFixedArea()
    {
        // Rows 1-3 scroll, row 4-5 is the fixed prompt area.
        var s = "\x1b[4;1H> prompt\x1b[5;1Hstatus" + "\x1b[1;3r\x1b[3;1H\r\nhistory 1\r\nhistory 2\x1b[r";
        Assert.Equal(["history 1", "history 2", "> prompt", "status"], Render(s, rows: 5));
    }

    [Fact]
    public void InsertAndDeleteCharactersAndLines()
    {
        Assert.Equal(["aXbc"], Render("abc\x1b[2G\x1b[@X"));
        Assert.Equal(["ac"], Render("abc\x1b[2G\x1b[P"));
        Assert.Equal(["one", "", "two"], Render("one\r\ntwo\x1b[2;1H\x1b[L"));
        Assert.Equal(["two"], Render("one\r\ntwo\x1b[1;1H\x1b[M"));
    }

    [Fact]
    public void OscTitlesAndUnknownSequencesAreSkipped() =>
        Assert.Equal(["text here"], Render("\x1b]0;window title\atext\x1b]8;;http://x\x1b\\ here\x1b[?25l\x1b[>4;2m\x1b[2 q\x1b(B"));

    [Fact]
    public void WideCharactersTakeTwoCells() =>
        Assert.Equal(["日本x"], Render("日本\x1b[5Gx"));

    [Fact]
    public void ACutTailStartsAfterTheFirstLineFeed()
    {
        var bytes = Encoding.UTF8.GetBytes("ä\x1b[31mcut\nrest");
        Assert.Equal("rest", TerminalText.Decode(bytes.AsSpan(1), fromStart: false));
        Assert.Equal("ok\n", TerminalText.Decode(Encoding.UTF8.GetBytes("Script started on 2026-10-28 [COMMAND=\"x\"]\nok\n"), fromStart: true));
    }
}

public class TabOutputTests
{
    [Fact]
    public void ReadsBothCapturePartsAtTheNotedSizeAndDeletesThem()
    {
        var root = Path.Combine(Path.GetTempPath(), "bs-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = new TabSession(root, DateTime.UtcNow);
            Assert.Null(session.ReadOutput("tab1"));
            var dir = session.Directory;
            File.WriteAllText(Path.Combine(dir, "tab1.out.1"), "partial\x1b[3\nolder line\r\n");
            File.WriteAllText(Path.Combine(dir, "tab1.out"), "0123456789newer\r\n");
            File.WriteAllText(Path.Combine(dir, "tab1.size"), "24 10\n");
            Assert.Equal(["older line", "0123456789", "newer"], session.ReadOutput("tab1"));
            Assert.Equal((24, 10), TabSession.ReadSize(Path.Combine(dir, "tab1.size")));
            Assert.Equal((40, 120), TabSession.ReadSize(Path.Combine(dir, "missing.size")));

            session.DeleteOutput("tab1");
            Assert.Empty(Directory.GetFiles(dir, "tab1.*"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PopupLinesAreCutToTheirWidthAndNotesFollowTheState()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig { Name = "demo", Directory = "~" }] });
        var tab = vm.Tabs[0];
        tab.SetState(TabState.Running, null, activity: Activity.Busy);
        Assert.True(tab.ShowsOutput);
        Assert.True(tab.HasOutputNote);
        Assert.False(tab.HasOutput);
        tab.SetOutput(["short", new string('x', 300)]);
        Assert.False(tab.HasOutputNote);
        Assert.True(tab.HasOutput);
        var lines = tab.OutputText.Split('\n');
        Assert.Equal("short", lines[0]);
        Assert.Equal(TabViewModel.OutputColumns, lines[1].Length);
        Assert.EndsWith("…", lines[1]);

        tab.SetState(TabState.Closed, null);
        Assert.False(tab.ShowsOutput);
        Assert.False(tab.HasOutput);
        Assert.True(tab.HasOutputNote);
    }

    /// <summary>Run once: bs_stream shows the formatter's lines and the exit line, and captures the same lines.</summary>
    [Fact]
    public void StreamRunsCaptureTheFormattedLines()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/python3")) return;
        var dir = Directory.CreateTempSubdirectory("bs-capstream").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "tab-lib.sh"), TabSession.Lib);
            File.WriteAllText(Path.Combine(dir, "stream.jsonl"), """
                {"type":"assistant","message":{"content":[{"type":"text","text":"Looking at the queue"}]}}
                {"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"gh issue list"}}]}}
                {"type":"result","is_error":false,"num_turns":2,"duration_ms":4000}
                """);
            var psi = new System.Diagnostics.ProcessStartInfo("bash") { RedirectStandardOutput = true, WorkingDirectory = dir };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(". ./tab-lib.sh . t && bs_stream cat stream.jsonl && mkdir keep && cp t.out keep/");
            using var p = System.Diagnostics.Process.Start(psi)!;
            var shown = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            var lines = TabSession.Open(Path.Combine(dir, "keep")).ReadOutput("t")!;
            Assert.Equal(["Looking at the queue", "> Bash gh issue list", "== turn done: 2 turns, 4 s"], lines.Take(3));
            Assert.Matches(@"^== exit 0, total \d+ s$", lines[3]);
            Assert.Equal(shown.TrimEnd('\n').Split('\n'), lines);
            Assert.False(File.Exists(Path.Combine(dir, "t.out")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// The real tab-lib.sh: a redrawing TUI under bs_run (inside a terminal from script -qec, as a tab) is captured,
    /// kept near bs_cap_max per part, replayed as its last frame, and removed when the tab's shell exits.
    /// </summary>
    [Fact]
    public void TabLibCapturesBoundedOutputAndRemovesItOnExit()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script") || !File.Exists("/usr/bin/python3")) return;
        var root = Path.Combine(Path.GetTempPath(), "bs-cap-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = new TabSession(root, DateTime.UtcNow);
            var dir = session.Directory;
            File.WriteAllText(Path.Combine(dir, "tui.py"), """
                import sys, time
                w = sys.stdout.write
                prev = 0
                for i in range(3000):
                    frame = ["\x1b[36m* Working (%d)\x1b[0m" % i, "+" + "-" * 40 + "+", "| > prompt line %-25d|" % i, "+" + "-" * 40 + "+"]
                    w("\x1b[2K\x1b[1A" * prev + "\x1b[2K\r" + "\r\n".join(frame) + "\r\n")
                    prev = len(frame)
                    sys.stdout.flush()
                    if i % 300 == 0: time.sleep(0.25)
                """);
            var keep = Path.Combine(dir, "keep");
            Directory.CreateDirectory(keep);
            // The tab chain's shape (Launcher.TabScript), with a copy of the capture taken before the shell exits.
            var chain = $". ./tab-lib.sh . tab1 && (bs_cli 2>/dev/null || true) && bs_run 'python3 tui.py' && (bs_done 0 2>/dev/null || true) "
                + "&& sleep 1.5 && cp tab1.out tab1.out.1 tab1.size keep/ && exit";
            var psi = new System.Diagnostics.ProcessStartInfo("script") { RedirectStandardOutput = true, WorkingDirectory = dir };
            foreach (var a in new[] { "-qec", $"stty rows 30 cols 90 && bash -c \"{chain}\"", "/dev/null" }) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            Assert.Equal("30 90", File.ReadAllText(Path.Combine(keep, "tab1.size")).Trim());
            Assert.True(new FileInfo(Path.Combine(keep, "tab1.out.1")).Length <= 131072);
            Assert.False(File.Exists(Path.Combine(dir, "tab1.out")));
            Assert.False(File.Exists(Path.Combine(dir, "tab1.out.1")));

            var copy = TabSession.Open(keep);
            var lines = copy.ReadOutput("tab1")!;
            Assert.Equal(["* Working (2999)", "+" + new string('-', 40) + "+", "| > prompt line 2999" + new string(' ', 21) + "|", "+" + new string('-', 40) + "+"],
                lines.TakeLast(4));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    /// <summary>
    /// #67: a restart queued for the tab's shell (bs_next, as RestartExited queues it) resets the terminal before the CLI
    /// starts. A fake claude on PATH stands in for the CLI; both run in a terminal from script -qec, as in a tab.
    /// </summary>
    [Fact]
    public void RestartResetsTheTerminalBeforeTheCliStarts()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/script")) return;
        var root = Path.Combine(Path.GetTempPath(), "bs-reset-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = new TabSession(root, DateTime.UtcNow);
            var dir = session.Directory;
            var bin = Path.Combine(dir, "bin");
            Directory.CreateDirectory(bin);
            var fake = Path.Combine(bin, "claude");
            File.WriteAllText(fake, "#!/bin/sh\nprintf 'CLI-STARTED\\n'\n");
            File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var track = new TabTrack(dir + "/tab-lib.sh", dir, "tab1", UpdateClis: false);
            session.QueueNext("tab1", Launcher.RestartScript(new TabConfig { Llm = "claude", Directory = dir }, track, resume: false));
            var psi = new System.Diagnostics.ProcessStartInfo("script") { RedirectStandardOutput = true, WorkingDirectory = dir };
            psi.Environment["PATH"] = bin + ":" + Environment.GetEnvironmentVariable("PATH");
            foreach (var a in new[] { "-qec", ". ./tab-lib.sh . tab1 && bs_next && exit", "/dev/null" }) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi)!;
            try
            {
                var output = p.StandardOutput.ReadToEnd();
                Assert.True(p.WaitForExit(30000));
                var reset = output.IndexOf("\x1b[?1049l", StringComparison.Ordinal);
                var cli = output.IndexOf("CLI-STARTED", StringComparison.Ordinal);
                Assert.True(reset >= 0, "no reset sequence in the output");
                Assert.True(cli > reset, "the CLI started before the terminal was reset");
                // Mouse, bracketed paste, cursor and the screen are all restored before the CLI draws.
                foreach (var seq in new[] { "\x1b[?1000l", "\x1b[?2004l", "\x1b[?25h", "\x1b[2J", "\x1b[3J" })
                    Assert.InRange(output.IndexOf(seq, StringComparison.Ordinal), 0, cli);
            }
            finally { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
