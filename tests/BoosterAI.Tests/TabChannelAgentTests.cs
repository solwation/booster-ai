using System.Diagnostics;
using System.Text.Json;
using BoosterAI;
using Xunit;

/// <summary>The status channel's agent side (#248): exports, registration per CLI, the MCP server and boosterai-tab.</summary>
public class TabChannelAgentTests
{
    private static readonly TabTrack Off = new("/s/tab-lib.sh", "/s", "tab1");
    private static readonly TabTrack On = Off with { StatusChannel = true };

    private static TabConfig Tab(string llm, bool orkester = true, bool once = false) =>
        new() { Name = "Example", Directory = "~/example", Llm = llm, Model = Catalog.StandardModel, Orkester = orkester, NonInteractive = once };

    /// <summary>Every script BoosterAI can run in a tracked tab for <paramref name="tab"/>.</summary>
    private static IEnumerable<string> Scripts(TabConfig tab, TabTrack track) =>
    [
        Launcher.TabScript(tab, null, track),
        Launcher.TabScript(tab, null, track, resume: true),
        Launcher.TabScript(tab, Launcher.InstallOrkesterPrompt, track),
        Launcher.RestartScript(tab, track, resume: false),
        Launcher.RestartScript(tab, track, resume: true),
    ];

    private static readonly string[] ChannelMarks =
        ["BOOSTERAI_TAB", "boosterai-tab", "mcp", "append-system-prompt", "developer_instructions", "allowedTools", "/s/bin"];

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("agy")]
    [InlineData("aider")]
    [InlineData("openhands")]
    [InlineData("interpreter")]
    [InlineData(Catalog.ShellOnly)]
    public void OffLeavesEveryScriptWithoutTheChannel(string llm)
    {
        foreach (var once in new[] { false, true })
            foreach (var script in Scripts(Tab(llm, once: once), Off))
                foreach (var mark in ChannelMarks)
                    Assert.DoesNotContain(mark, script);
        Assert.Equal("", TabChannelAgent.ExportCommand(Off));
        Assert.Equal("", TabChannelAgent.Arguments(llm, Off));
        Assert.Equal("", TabChannelAgent.Arguments(llm, null));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("agy")]
    [InlineData("aider")]
    [InlineData("openhands")]
    [InlineData("interpreter")]
    [InlineData(Catalog.ShellOnly)]
    public void OnExportsTheVariablesAndTheCommandWithoutSemicolonsOrDoubleQuotes(string llm)
    {
        foreach (var once in new[] { false, true })
            foreach (var script in Scripts(Tab(llm, once: once), On))
            {
                Assert.DoesNotContain(';', script);
                Assert.DoesNotContain('"', script);
                Assert.Contains("BOOSTERAI_TAB_EVENTS='/s/tab1.events' BOOSTERAI_TAB_ANSWERS='/s/tab1.answers' BOOSTERAI_TAB_PROTOCOL='1' "
                    + "PATH='/s/bin':${PATH#'/s/bin':} && export BOOSTERAI_TAB_EVENTS BOOSTERAI_TAB_ANSWERS BOOSTERAI_TAB_PROTOCOL PATH && ", script);
            }
        // A shell without the lib (the plain branch) gets nothing: the channel needs the tracked tab.
        var plain = Launcher.TabScript(Tab(llm), null, On).Split("\nelse ")[1];
        foreach (var mark in ChannelMarks) Assert.DoesNotContain(mark, plain);
    }

    [Fact]
    public void ClaudeGetsTheSessionMcpConfigTheToolsAndTheInstruction()
    {
        var expected = " --mcp-config '/s/boosterai-mcp.json' --allowedTools mcp__boosterai --append-system-prompt " + Launcher.Quote(TabChannelAgent.Instruction);
        Assert.Equal(expected, TabChannelAgent.Arguments("claude", On));
        foreach (var script in Scripts(Tab("claude"), On))
        {
            // Inside bs_run's quoted command line; the user's own MCP servers stay (no --strict-mcp-config).
            Assert.Contains(expected.Replace("'", "'\\''"), script);
            Assert.DoesNotContain("strict-mcp-config", script);
        }
        // Last on the command line: --mcp-config and --allowedTools take several values and must not swallow the prompt.
        var start = Launcher.TabScript(Tab("claude"), null, On);
        Assert.True(start.IndexOf("'\\''/orkester'\\''", StringComparison.Ordinal) < start.IndexOf("--mcp-config", StringComparison.Ordinal));
        var config = JsonDocument.Parse(TabChannelAgent.McpConfigJson("/s")).RootElement.GetProperty("mcpServers").GetProperty("boosterai");
        Assert.Equal("stdio", config.GetProperty("type").GetString());
        Assert.Equal("python3", config.GetProperty("command").GetString());
        Assert.Equal(["-I", "/s/bin/boosterai-tab", "mcp"], config.GetProperty("args").EnumerateArray().Select(a => a.GetString()));
    }

    [Fact]
    public void CodexGetsTheServerAsConfigOverridesAndDeveloperInstructions()
    {
        var args = TabChannelAgent.Arguments("codex", On);
        Assert.Equal(" -c 'mcp_servers.boosterai.command=python3'"
            + " -c 'mcp_servers.boosterai.args=['\\''-I'\\'','\\''/s/bin/boosterai-tab'\\'','\\''mcp'\\'']'"
            + " -c 'mcp_servers.boosterai.env_vars=['\\''BOOSTERAI_TAB_EVENTS'\\'','\\''BOOSTERAI_TAB_ANSWERS'\\'','\\''BOOSTERAI_TAB_PROTOCOL'\\'']'"
            + $" -c 'mcp_servers.boosterai.tool_timeout_sec={TabChannelAgent.MaxAskTimeoutSeconds + 60}'"
            + " -c 'mcp_servers.boosterai.default_tools_approval_mode=approve'"
            + " -c " + Launcher.Quote("developer_instructions=" + TabChannelAgent.Instruction), args);
        foreach (var script in Scripts(Tab("codex"), On)) Assert.Contains(args.Replace("'", "'\\''"), script);
        Assert.Contains("codex resume --last -c tui.auto_recap=false -c '\\''mcp_servers.boosterai.command=python3", Launcher.TabScript(Tab("codex"), null, On, resume: true));
        // A session path that is no TOML literal string gets no server (the command still works).
        Assert.Equal("", TabChannelAgent.Arguments("codex", new TabTrack("/it's/tab-lib.sh", "/it's", "tab1", StatusChannel: true)));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void RunOnceRegistersNothing(string llm)
    {
        var once = Launcher.TabScript(Tab(llm, once: true), null, On);
        Assert.DoesNotContain("mcp", once);
        Assert.DoesNotContain("append-system-prompt", once);
        Assert.DoesNotContain("developer_instructions", once);
        // Its Save and close turn neither.
        Assert.DoesNotContain("mcp", Launcher.SaveAndCloseScript(Tab(llm), "t"));
    }

    [Theory]
    [InlineData("agy")]
    [InlineData("aider")]
    [InlineData("openhands")]
    [InlineData("interpreter")]
    [InlineData(Catalog.ShellOnly)]
    public void OtherCliesGetOnlyTheCommand(string llm)
    {
        Assert.Equal("", TabChannelAgent.Arguments(llm, On));
        foreach (var script in Scripts(Tab(llm), On))
        {
            Assert.DoesNotContain("mcp", script);
            Assert.DoesNotContain(TabChannelAgent.Instruction, script);
        }
    }

    [Fact]
    public void TheInstructionIsShortAndSafeEverywhere()
    {
        Assert.InRange(TabChannelAgent.Instruction.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 20, 60);
        Assert.DoesNotContain(';', TabChannelAgent.Instruction);
        Assert.DoesNotContain('"', TabChannelAgent.Instruction);
        Assert.DoesNotContain('\'', TabChannelAgent.Instruction);
        Assert.Contains("boosterai_status", TabChannelAgent.Instruction);
        Assert.Contains("boosterai_ask", TabChannelAgent.Instruction);
    }

    [Fact]
    public void TheScriptUsesTheSameLimitsAsTheReader()
    {
        var s = TabChannelAgent.Script;
        Assert.Contains($"VERSION = {TabChannel.Version}\n", s);
        Assert.Contains($"MAX_TEXT, MAX_OPTIONS, MAX_OPTION = {TabChannel.MaxTextLength}, {TabChannel.MaxOptions}, {TabChannel.MaxOptionLength}\n", s);
        Assert.Contains($"ASK_DEFAULT, ASK_MAX = {TabChannelAgent.AskTimeoutSeconds}, {TabChannelAgent.MaxAskTimeoutSeconds}\n", s);
        foreach (var name in new[] { TabChannel.EventsVariable, TabChannel.AnswersVariable, TabChannel.VersionVariable }) Assert.Contains(name, s);
        Assert.DoesNotContain('\r', s);
    }

    private static bool Available => OperatingSystem.IsLinux() && File.Exists("/usr/bin/python3");

    [Fact]
    public void TheSessionHoldsTheExecutableCommandAndTheMcpConfig()
    {
        var root = Directory.CreateTempSubdirectory("bs-agent").FullName;
        try
        {
            var session = TabSession.Open(Path.Combine(root, "s"));
            var command = Path.Combine(session.Directory, "bin", TabChannelAgent.CommandName);
            Assert.Equal(TabChannelAgent.Script + "\n", File.ReadAllText(command));
            // The configuration names the folder as the tab sees it: on Windows the tab runs in WSL (#259).
            var seen = OperatingSystem.IsWindows() ? PathMapping.ToWslPath(session.Directory) : session.Directory;
            Assert.Equal(TabChannelAgent.McpConfigJson(seen), File.ReadAllText(Path.Combine(session.Directory, TabChannelAgent.McpConfigFile)));
            Assert.Single(Directory.GetFileSystemEntries(Path.Combine(session.Directory, "bin")));
            if (OperatingSystem.IsLinux()) Assert.True(File.GetUnixFileMode(command).HasFlag(UnixFileMode.UserExecute));
            // Nothing of a tab's channel exists before its agent writes.
            Assert.Empty(Directory.GetFileSystemEntries(session.Directory, "*.events"));
            Assert.Empty(Directory.GetFileSystemEntries(session.Directory, "*.answers"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>A client of the session's MCP server, as Claude Code or Codex starts it (python3 -I script mcp).</summary>
    private sealed class McpClient : IDisposable
    {
        private readonly Process _process;
        private int _id;

        public McpClient(TabSession session, TabTrack track, string root)
        {
            var psi = new ProcessStartInfo("python3") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "-I", TabChannelAgent.CommandPath(session.Directory), "mcp" }) psi.ArgumentList.Add(a);
            TestShell.Configure(psi, root);
            foreach (var (k, v) in TabChannel.Environment(track)) psi.Environment[k] = v;
            _process = Process.Start(psi)!;
        }

        public void Notify(string method) => Send($$"""{"jsonrpc":"2.0","method":"{{method}}"}""");

        public void Send(string json) { _process.StandardInput.WriteLine(json); _process.StandardInput.Flush(); }

        public int Request(string method, string paramsJson = "{}")
        {
            var id = ++_id;
            Send($$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{paramsJson}}""" + "}");
            return id;
        }

        public JsonElement Read(int timeoutMs = 20000)
        {
            var line = _process.StandardOutput.ReadLineAsync();
            Assert.True(line.Wait(timeoutMs), "no reply from the MCP server");
            Assert.NotNull(line.Result);
            return JsonDocument.Parse(line.Result!).RootElement.Clone();
        }

        public void Dispose()
        {
            try
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(5000)) _process.Kill(entireProcessTree: true);
                _process.WaitForExit();
            }
            finally { _process.Dispose(); }
        }
    }

    private static string ToolText(JsonElement reply)
    {
        var result = reply.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        return result.GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    [Fact]
    public void McpHandshakeListsAndCallsTheTools()
    {
        if (!Available) return;
        var root = Directory.CreateTempSubdirectory("bs-agent-mcp").FullName;
        try
        {
            var session = TabSession.Open(Path.Combine(root, "s"));
            var track = session.NewTrack(statusChannel: true)!;
            using var client = new McpClient(session, track, root);
            var init = client.Request("initialize", """{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}""");
            var reply = client.Read();
            Assert.Equal(init, reply.GetProperty("id").GetInt32());
            Assert.Equal("2025-06-18", reply.GetProperty("result").GetProperty("protocolVersion").GetString());
            Assert.Equal("boosterai", reply.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
            Assert.True(reply.GetProperty("result").GetProperty("capabilities").TryGetProperty("tools", out _));
            client.Notify("notifications/initialized");
            client.Request("ping");
            Assert.Equal(JsonValueKind.Object, client.Read().GetProperty("result").ValueKind);

            client.Request("tools/list");
            var tools = client.Read().GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
            Assert.Equal(["boosterai_status", "boosterai_ask"], tools.Select(t => t.GetProperty("name").GetString()));
            Assert.Equal(["idle", "working", "blocked", "done"],
                tools[0].GetProperty("inputSchema").GetProperty("properties").GetProperty("state").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
            Assert.Equal(["question"], tools[1].GetProperty("inputSchema").GetProperty("required").EnumerateArray().Select(e => e.GetString()));

            client.Request("tools/call", """{"name":"boosterai_status","arguments":{"state":"blocked","text":"Waiting for the API key"}}""");
            Assert.Contains("blocked", ToolText(client.Read()));
            var status = session.ReadChannel(track.Id).Status!;
            Assert.Equal(AgentState.Blocked, status.State);
            Assert.Equal("Waiting for the API key", status.Text);

            // An invalid state is a tool error, not a protocol error.
            client.Request("tools/call", """{"name":"boosterai_status","arguments":{"state":"sleeping"}}""");
            Assert.True(client.Read().GetProperty("result").GetProperty("isError").GetBoolean());

            // ask waits for BoosterAI's answer; meanwhile the server still answers other requests.
            var ask = client.Request("tools/call", """{"name":"boosterai_ask","arguments":{"question":"Which branch?","options":["main","dev"]}}""");
            TabQuestion? question = null;
            Assert.True(SpinWait.SpinUntil(() => (question = session.ReadChannel(track.Id).Open.FirstOrDefault()) is not null, 10000));
            Assert.Equal("Which branch?", question!.Text);
            Assert.Equal(["main", "dev"], question.Options);
            Assert.False(question.Free);
            Assert.Equal(question.At + TabChannelAgent.AskTimeoutSeconds * 1000L, question.ExpiresAt);
            var ping = client.Request("ping");
            Assert.Equal(ping, client.Read().GetProperty("id").GetInt32());
            Assert.Equal(AnswerResult.Answered, session.Answer(track.Id, question.Id, "dev"));
            reply = client.Read();
            Assert.Equal(ask, reply.GetProperty("id").GetInt32());
            Assert.Equal("The user answered: dev", ToolText(reply));
            Assert.Empty(session.ReadChannel(track.Id).Open);

            // No answer within the time: a clear result, not an error, so the agent can go on.
            client.Request("tools/call", """{"name":"boosterai_ask","arguments":{"question":"Anyone there?","timeout_seconds":1}}""");
            Assert.StartsWith("No answer from the user within 1 s.", ToolText(client.Read()));
            var expired = session.ReadChannel(track.Id).Open.Single();
            Assert.True(expired.Free);
            Assert.Equal(expired.At + 1000, expired.ExpiresAt);

            client.Request("tools/call", """{"name":"nope","arguments":{}}""");
            Assert.Equal(-32602, client.Read().GetProperty("error").GetProperty("code").GetInt32());
            client.Request("resources/list");
            Assert.Equal(-32601, client.Read().GetProperty("error").GetProperty("code").GetInt32());
        }
        finally { TestShell.StopLeftovers(root); Directory.Delete(root, recursive: true); }
    }

    /// <summary>#275: a notice BoosterAI leaves in tabN.notice comes with the next tool result once, and is recorded as read.</summary>
    [Fact]
    public void ANoticeComesWithTheNextToolResultOnce()
    {
        if (!Available) return;
        var root = Directory.CreateTempSubdirectory("bs-agent-notice").FullName;
        try
        {
            var session = TabSession.Open(Path.Combine(root, "s"));
            var track = session.NewTrack(statusChannel: true)!;
            using var client = new McpClient(session, track, root);
            client.Request("initialize", """{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}""");
            client.Read();
            client.Request("tools/call", """{"name":"boosterai_status","arguments":{"state":"working","text":"Reading"}}""");
            Assert.Equal("BoosterAI shows the status working: Reading", ToolText(client.Read()));

            Assert.True(session.SendNotice(track.Id, "quota-session-1", "Please consider saving your state."));
            Assert.False(File.Exists(Path.Combine(session.Directory, TabChannel.NoticeFile(track.Id) + ".tmp")));
            client.Request("tools/call", """{"name":"boosterai_status","arguments":{"state":"working","text":"Building"}}""");
            var text = ToolText(client.Read());
            Assert.StartsWith("BoosterAI shows the status working: Building", text);
            Assert.EndsWith("Message from BoosterAI (the app that runs this tab, not the user): Please consider saving your state.", text);
            Assert.False(File.Exists(Path.Combine(session.Directory, TabChannel.NoticeFile(track.Id))));
            var state = session.ReadChannel(track.Id);
            Assert.Equal("quota-session-1", state.NoticeRead);
            Assert.NotNull(state.NoticeReadAt);
            Assert.Equal("Building", state.Status!.Text);

            // Taken once: the next call has no notice.
            client.Request("tools/call", """{"name":"boosterai_status","arguments":{"state":"done","text":"Saved"}}""");
            Assert.Equal("BoosterAI shows the status done: Saved", ToolText(client.Read()));
            Assert.Empty(Directory.GetFiles(session.Directory, "*.taken"));
        }
        finally { TestShell.StopLeftovers(root); Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ACancelledAskStopsWaiting()
    {
        if (!Available) return;
        var root = Directory.CreateTempSubdirectory("bs-agent-cancel").FullName;
        try
        {
            var session = TabSession.Open(Path.Combine(root, "s"));
            var track = session.NewTrack(statusChannel: true)!;
            using var client = new McpClient(session, track, root);
            client.Request("initialize", """{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1"}}""");
            client.Read();
            var ask = client.Request("tools/call", """{"name":"boosterai_ask","arguments":{"question":"Stop?"}}""");
            Assert.True(SpinWait.SpinUntil(() => session.ReadChannel(track.Id).Open.Count == 1, 10000));
            client.Send("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":" + ask + "}}");
            // The cancelled call gets no reply; the next request is answered at once.
            var ping = client.Request("ping");
            Assert.Equal(ping, client.Read().GetProperty("id").GetInt32());
        }
        finally { TestShell.StopLeftovers(root); Directory.Delete(root, recursive: true); }
    }

    private static (int Code, string Out, string Err) RunCommand(TabSession session, TabTrack? track, string root, params string[] args)
    {
        var psi = new ProcessStartInfo(TabChannelAgent.CommandPath(session.Directory)) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        TestShell.Configure(psi, root);
        if (track is not null) foreach (var (k, v) in TabChannel.Environment(track)) psi.Environment[k] = v;
        using var p = Process.Start(psi)!;
        try
        {
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            Assert.True(p.WaitForExit(20000));
            return (p.ExitCode, stdout.Result, stderr.Result);
        }
        finally { if (!p.HasExited) p.Kill(entireProcessTree: true); }
    }

    [Fact]
    public void TheCommandReportsAsksAndExplainsMissingSetup()
    {
        if (!Available) return;
        var root = Directory.CreateTempSubdirectory("bs-agent-cmd").FullName;
        try
        {
            var session = TabSession.Open(Path.Combine(root, "s"));
            var track = session.NewTrack(statusChannel: true)!;
            var r = RunCommand(session, track, root, "status", "done", "All", "tests", "pass");
            Assert.Equal(0, r.Code);
            Assert.Equal((AgentState.Done, "All tests pass"), (session.ReadChannel(track.Id).Status!.State, session.ReadChannel(track.Id).Status!.Text));
            r = RunCommand(session, track, root, "ask", "--option", "yes", "--option", "no", "--timeout", "1", "Deploy now?");
            Assert.Equal(2, r.Code);
            Assert.StartsWith("No answer from the user within 1 s.", r.Err);
            Assert.Equal(["yes", "no"], session.ReadChannel(track.Id).Open.Single().Options);
            r = RunCommand(session, null, root, "status", "done");
            Assert.Equal(1, r.Code);
            Assert.Contains("BOOSTERAI_TAB_EVENTS", r.Err);
            Assert.Equal(2, RunCommand(session, track, root, "status", "sleeping").Code); // argparse: invalid choice
        }
        finally { TestShell.StopLeftovers(root); Directory.Delete(root, recursive: true); }
    }

    /// <summary>
    /// The real tracked tab script on a pseudo-terminal, with the channel on and a stand-in claude: it finds boosterai-tab on
    /// PATH, starts the MCP server from its --mcp-config like Claude Code does, reports a status, asks, and records the
    /// answer that BoosterAI's side (TabSession.Answer) wrote.
    /// </summary>
    [Fact]
    public void ATabWithTheChannelOnReportsAndAsksOnAPseudoTerminal()
    {
        if (!Available || !File.Exists("/usr/bin/script")) return;
        var root = Directory.CreateTempSubdirectory("bs-agent-pty").FullName;
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
            var bin = Directory.CreateDirectory(Path.Combine(root, "shell-bin")).FullName;
            var session = TabSession.Open(Path.Combine(root, "s"));
            var track = session.NewTrack(statusChannel: true, logo: false)!;
            var result = Path.Combine(root, "result.json");
            // The stand-in claude: an MCP client of the server named by --mcp-config, plus the command line fallback.
            File.WriteAllText(Path.Combine(bin, "claude"), """
#!/usr/bin/python3
import json, os, subprocess, sys
args = sys.argv[1:]
config = json.load(open(args[args.index("--mcp-config") + 1]))["mcpServers"]["boosterai"]
out = {"args": args, "which": subprocess.run(["sh", "-c", "command -v boosterai-tab"], capture_output=True, text=True).stdout.strip(),
       "env": {k: os.environ.get(k) for k in ("BOOSTERAI_TAB_EVENTS", "BOOSTERAI_TAB_ANSWERS", "BOOSTERAI_TAB_PROTOCOL")}}
out["command"] = subprocess.run(["boosterai-tab", "status", "working", "Started"], capture_output=True, text=True).returncode
server = subprocess.Popen([config["command"]] + config["args"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
def call(i, method, params):
    server.stdin.write(json.dumps({"jsonrpc": "2.0", "id": i, "method": method, "params": params}) + "\n")
    server.stdin.flush()
    return json.loads(server.stdout.readline())
out["init"] = call(1, "initialize", {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "stub", "version": "1"}})["result"]["serverInfo"]["name"]
out["status"] = call(2, "tools/call", {"name": "boosterai_status", "arguments": {"state": "done", "text": "Finished the task"}})["result"]["content"][0]["text"]
out["ask"] = call(3, "tools/call", {"name": "boosterai_ask", "arguments": {"question": "Ship it?", "options": ["Yes", "Not yet"], "allow_free_text": True}})["result"]["content"][0]["text"]
server.stdin.close()
server.wait()
json.dump(out, open(os.environ["BS_RESULT"], "w"))
""");
            File.SetUnixFileMode(Path.Combine(bin, "claude"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var tab = new TabConfig { Name = "Example", Directory = project, Llm = "claude", Model = Catalog.StandardModel, Orkester = true };
            var script = Launcher.TabScript(tab, null, track);
            Assert.DoesNotContain(';', script);
            var psi = new ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("-qefc");
            psi.ArgumentList.Add("bash --noprofile --norc -c " + Launcher.Quote(script));
            psi.ArgumentList.Add("/dev/null");
            TestShell.Configure(psi, root, bin, blockAgents: false);
            psi.Environment["BS_RESULT"] = result;
            using var process = Process.Start(psi)!;
            try
            {
                TabQuestion? question = null;
                if (!SpinWait.SpinUntil(() => (question = session.ReadChannel(track.Id).Open.FirstOrDefault()) is not null, 30000))
                {
                    process.Kill(entireProcessTree: true);
                    Assert.Fail("no question: " + process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd() + "\nlog: "
                        + File.ReadAllText(Path.Combine(session.Directory, track.Id + ".log")));
                }
                Assert.Equal("Ship it?", question!.Text);
                Assert.True(question.Free);
                var status = session.ReadChannel(track.Id).Status!;
                Assert.Equal((AgentState.Done, "Finished the task"), (status.State, status.Text));
                Assert.Equal(AnswerResult.Answered, session.Answer(track.Id, question.Id, "Not yet, run the tests first"));
                Assert.True(SpinWait.SpinUntil(() => File.Exists(result), 30000), "the stand-in did not finish");
                // Typed only once the idle shell waits, so script(1) of the finished CLI does not take it.
                var log = Path.Combine(session.Directory, track.Id + ".log");
                Assert.True(SpinWait.SpinUntil(() => File.ReadAllText(log).Contains(" cli-exit rc=0"), 30000), "no cli-exit");
                Thread.Sleep(1000);
                process.StandardInput.WriteLine("exit 0");
                process.StandardInput.Flush();
                Assert.True(process.WaitForExit(30000));
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            var r = JsonDocument.Parse(File.ReadAllText(result)).RootElement;
            Assert.Equal("The user answered: Not yet, run the tests first", r.GetProperty("ask").GetString());
            Assert.Equal("boosterai", r.GetProperty("init").GetString());
            Assert.Equal(0, r.GetProperty("command").GetInt32());
            Assert.Equal(TabChannelAgent.CommandPath(track.Dir), r.GetProperty("which").GetString());
            Assert.Equal($"{track.Dir}/{track.Id}.events", r.GetProperty("env").GetProperty("BOOSTERAI_TAB_EVENTS").GetString());
            Assert.Equal("1", r.GetProperty("env").GetProperty("BOOSTERAI_TAB_PROTOCOL").GetString());
            var args = r.GetProperty("args").EnumerateArray().Select(a => a.GetString()).ToList();
            Assert.Equal("/orkester", args[0]);
            Assert.Equal(TabChannelAgent.Instruction, args[args.IndexOf("--append-system-prompt") + 1]);
            Assert.Equal("mcp__boosterai", args[args.IndexOf("--allowedTools") + 1]);
            // The events file as the tab wrote it: the command's status, the server's status and the question, all version 1.
            var lines = File.ReadAllLines(Path.Combine(session.Directory, TabChannel.EventsFile(track.Id)));
            Assert.Equal(3, lines.Length);
            Assert.All(lines, l => Assert.Equal(1, JsonDocument.Parse(l).RootElement.GetProperty("v").GetInt32()));
            Assert.Empty(session.ReadChannel(track.Id).Open);
        }
        finally { TestShell.StopLeftovers(root); Directory.Delete(root, recursive: true); }
    }
}
