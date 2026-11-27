using System.Text;
using BoosterAI;
using Xunit;

/// <summary>The per-tab status channel (#247): parsing tabN.events, answering in tabN.answers/ and the setting.</summary>
public class TabChannelTests
{
    private const string Status = """{"v":1,"at":200,"kind":"status","state":"working","text":"Running the tests"}""";
    private const string Question = """{"v":1,"at":300,"kind":"question","id":"q1","text":"Which branch?","options":["main","dev"],"free":false}""";

    [Fact]
    public void ParsesStatusAndQuestions()
    {
        var state = TabChannel.Parse(Status + "\n" + Question + "\n"
            + """{"v":1,"at":400,"kind":"status","state":"blocked","text":"Waiting for an answer"}""" + "\n"
            + """{"v":1,"at":500,"kind":"question","id":"q-2","text":"Anything else?"}""" + "\n", sinceMs: null);
        Assert.Equal(new AgentStatus(AgentState.Blocked, "Waiting for an answer", 400), state.Status);
        Assert.Equal(2, state.Open.Count);
        Assert.Equal("q1", state.Open[0].Id);
        Assert.Equal(["main", "dev"], state.Open[0].Options);
        Assert.False(state.Open[0].Free);
        // Without options and without "free", a question takes free text.
        Assert.True(state.Open[1].Free);
        Assert.Empty(state.Answered);
    }

    [Fact]
    public void SkipsBrokenUnknownAndHalfWrittenLines()
    {
        var text = "garbage\n{not json\n[1,2]\n"
            + """{"at":100,"kind":"status","state":"done","text":"no version"}""" + "\n"
            + """{"v":2,"at":100,"kind":"status","state":"done","text":"newer version"}""" + "\n"
            + """{"v":1,"kind":"status","state":"done","text":"no time"}""" + "\n"
            + """{"v":1,"at":100,"kind":"status","state":"sleeping"}""" + "\n"
            + """{"v":1,"at":100,"kind":"chat","text":"?"}""" + "\n"
            + """{"v":1,"at":100,"kind":"question","id":"../x","text":"bad id","free":true}""" + "\n"
            + """{"v":1,"at":100,"kind":"question","id":"q0","text":"nobody can answer","options":[],"free":false}""" + "\n"
            + Status + "\r\n"
            // Still being written: no newline yet.
            + """{"v":1,"at":900,"kind":"status","state":"idle","text":"half""";
        var state = TabChannel.Parse(text, sinceMs: null);
        Assert.Equal(AgentState.Working, state.Status!.State);
        Assert.Empty(state.Open);
        Assert.Equal(TabChannelState.Empty, TabChannel.Parse("""{"v":1,"at":1,"kind":"status","state":"idle"}""", null));
    }

    [Fact]
    public void IgnoresEventsBeforeTheLatestCliStart()
    {
        var state = TabChannel.Parse(Status + "\n" + Question + "\n", sinceMs: 250);
        Assert.Null(state.Status);
        Assert.Equal("q1", Assert.Single(state.Open).Id);
        Assert.Equal(TabChannelState.Empty.Status, TabChannel.Parse(Status + "\n" + Question + "\n", sinceMs: 301).Status);
        Assert.Empty(TabChannel.Parse(Status + "\n" + Question + "\n", sinceMs: 301).Open);
        Assert.Equal(1500, TabLog.LastCliStart("1000 start pid=1 starttime=2\n1100 cli-start\n1200 cli-exit rc=0\n1500 cli-start\n"));
        Assert.Null(TabLog.LastCliStart("1000 start pid=1 starttime=2\n"));
    }

    [Fact]
    public void CapsLongTextsAndOptions()
    {
        var options = string.Join(",", Enumerable.Range(0, 30).Select(i => $"\"{new string('o', 300)}{i}\""));
        var line = $$"""{"v":1,"at":1,"kind":"question","id":"q","text":"{{new string('x', 5000)}}","options":[{{options}}]}""";
        var q = Assert.Single(TabChannel.Parse(line + "\n", null).Open);
        Assert.Equal(TabChannel.MaxTextLength, q.Text.Length);
        Assert.Equal(TabChannel.MaxOptions, q.Options.Count);
        Assert.All(q.Options, o => Assert.Equal(TabChannel.MaxOptionLength, o.Length));
    }

    [Fact]
    public void ReadsOnlyTheTailOfAnOversizeFile()
    {
        var dir = Directory.CreateTempSubdirectory("bs-channel").FullName;
        try
        {
            var path = Path.Combine(dir, "tab1.events");
            var filler = """{"v":1,"at":1,"kind":"status","state":"working","text":"filler"}""" + "\n";
            var sb = new StringBuilder();
            while (sb.Length < 3 * TabChannel.MaxReadBytes) sb.Append(filler);
            sb.Append("""{"v":1,"at":2,"kind":"status","state":"done","text":"last"}""" + "\n");
            File.WriteAllText(path, sb.ToString());
            var tail = TabChannel.ReadTail(path);
            Assert.True(Encoding.UTF8.GetByteCount(tail) <= TabChannel.MaxReadBytes);
            // Starts at a whole line, so nothing half is parsed.
            Assert.StartsWith("{", tail);
            Assert.Equal(new AgentStatus(AgentState.Done, "last", 2), TabChannel.Parse(tail, null).Status);
            Assert.Equal("ab\n", TabChannel.ReadTail(WriteBytes(dir, "small", "ab\n")));
            Assert.Equal("cd\n", TabChannel.ReadTail(WriteBytes(dir, "cut", "abcdef\ncd\n"), maxBytes: 5));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string WriteBytes(string dir, string name, string text)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void ReadsWhileTheAgentAppends()
    {
        var dir = Directory.CreateTempSubdirectory("bs-channel").FullName;
        try
        {
            var path = Path.Combine(dir, "tab1.events");
            using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var bytes = Encoding.UTF8.GetBytes(Status + "\n" + """{"v":1,"at":300,"kind":"status","state":"id""");
            writer.Write(bytes);
            writer.Flush();
            Assert.Equal(AgentState.Working, TabChannel.Parse(TabChannel.ReadTail(path), null).Status!.State);
            writer.Write(Encoding.UTF8.GetBytes("le\",\"text\":\"\"}\n"));
            writer.Flush();
            Assert.Equal(AgentState.Idle, TabChannel.Parse(TabChannel.ReadTail(path), null).Status!.State);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void AnswersOnceAtomically()
    {
        var root = Directory.CreateTempSubdirectory("bs-channel").FullName;
        try
        {
            var session = TabSession.Open(Path.Combine(root, "s"));
            Assert.Equal(TabChannelState.Empty, session.ReadChannel("tab1"));
            Assert.Equal(AnswerResult.UnknownQuestion, session.Answer("tab1", "q1", "main"));
            File.WriteAllText(Path.Combine(session.Directory, "tab1.log"), "100 start pid=1 starttime=2\n150 cli-start\n");
            File.WriteAllText(Path.Combine(session.Directory, "tab1.events"), Status + "\n" + Question + "\n");

            Assert.Equal(AnswerResult.Invalid, session.Answer("tab1", "q1", "feature"));
            Assert.Equal(AnswerResult.Invalid, session.Answer("tab1", "q1", "  "));
            Assert.Equal(AnswerResult.Answered, session.Answer("tab1", "q1", "dev"));
            Assert.Equal(AnswerResult.AlreadyAnswered, session.Answer("tab1", "q1", "main"));

            var answers = Path.Combine(session.Directory, "tab1.answers");
            // Only the answer itself is left: the temporary file was renamed into place.
            Assert.Equal(["q1"], Directory.GetFiles(answers).Select(Path.GetFileName));
            var answer = TabChannel.ReadAnswer(Path.Combine(answers, "q1"))!;
            Assert.Equal(("q1", 300L, "dev"), (answer.Id, answer.QuestionAt, answer.Text));
            var state = session.ReadChannel("tab1");
            Assert.Empty(state.Open);
            Assert.Equal("dev", Assert.Single(state.Answered).Answer.Text);
            // A second writer that already holds the question still loses.
            Assert.Equal(AnswerResult.AlreadyAnswered, TabChannel.WriteAnswer(answers, state.Answered[0].Question, "main", 1));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ConcurrentAnswersHaveOneWinner()
    {
        var dir = Directory.CreateTempSubdirectory("bs-channel").FullName;
        try
        {
            var q = new TabQuestion("q1", "?", [], true, 7);
            var results = Enumerable.Range(0, 16).AsParallel().Select(i => TabChannel.WriteAnswer(dir, q, "answer " + i, 1)).ToList();
            Assert.Equal(1, results.Count(r => r == AnswerResult.Answered));
            Assert.All(results, r => Assert.Contains(r, new[] { AnswerResult.Answered, AnswerResult.AlreadyAnswered }));
            Assert.Equal(["q1"], Directory.GetFiles(dir).Select(Path.GetFileName));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ALockLeftByADeadWriterIsTakenOver()
    {
        var dir = Directory.CreateTempSubdirectory("bs-channel").FullName;
        try
        {
            var q = new TabQuestion("q1", "?", [], true, 7);
            var lockPath = Path.Combine(dir, ".q1.lock");
            File.WriteAllText(lockPath, "");
            Assert.Equal(AnswerResult.AlreadyAnswered, TabChannel.WriteAnswer(dir, q, "busy", 1));
            File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow - TabChannel.StaleLock - TimeSpan.FromSeconds(1));
            Assert.Equal(AnswerResult.Answered, TabChannel.WriteAnswer(dir, q, "taken over", 1));
            Assert.Equal(["q1"], Directory.GetFiles(dir).Select(Path.GetFileName));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void AReusedIdAfterARestartIsOpenAgain()
    {
        var root = Directory.CreateTempSubdirectory("bs-channel").FullName;
        try
        {
            var session = TabSession.Open(Path.Combine(root, "s"));
            var log = Path.Combine(session.Directory, "tab1.log");
            var events = Path.Combine(session.Directory, "tab1.events");
            File.WriteAllText(log, "100 start pid=1 starttime=2\n150 cli-start\n");
            File.WriteAllText(events, Question + "\n");
            Assert.Equal(AnswerResult.Answered, session.Answer("tab1", "q1", "main"));
            // An in-place restart: the new CLI asks q1 again; the old answer neither counts nor blocks the new one.
            File.AppendAllText(log, "400 cli-exit rc=0\n500 cli-start\n");
            File.AppendAllText(events, Question.Replace("\"at\":300", "\"at\":600") + "\n");
            Assert.Equal(600, Assert.Single(session.ReadChannel("tab1").Open).At);
            Assert.Equal(AnswerResult.Answered, session.Answer("tab1", "q1", "dev"));
            Assert.Equal("dev", Assert.Single(session.ReadChannel("tab1").Answered).Answer.Text);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void BrokenAnswerFilesDoNotCount()
    {
        Assert.Null(TabChannel.ParseAnswer("{half"));
        Assert.Null(TabChannel.ParseAnswer("""{"v":1,"id":"q1","text":"x","at":1}"""));
        var a = new TabAnswer("q1", 3, "line \"one\"\nline two", 4);
        Assert.Equal(a, TabChannel.ParseAnswer(TabChannel.AnswerJson(a)));
    }

    [Fact]
    public void QuestionIdsAreSafeFileNames()
    {
        Assert.True(TabChannel.IsValidId("q-1_a.b"));
        Assert.False(TabChannel.IsValidId(".hidden"));
        Assert.False(TabChannel.IsValidId("../x"));
        Assert.False(TabChannel.IsValidId("a/b"));
        Assert.False(TabChannel.IsValidId(""));
        Assert.False(TabChannel.IsValidId(new string('a', 65)));
    }

    [Fact]
    public void SettingIsOnByDefaultAndSaved()
    {
        Assert.True(new AppConfig().TabStatusChannel);
        var json = System.Text.Json.JsonSerializer.Serialize(new AppConfig { TabStatusChannel = false }, ConfigJsonContext.Default.AppConfig);
        Assert.False(System.Text.Json.JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)!.TabStatusChannel);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize("{}", ConfigJsonContext.Default.AppConfig)!.TabStatusChannel);
    }

    [Fact]
    public void OffCreatesNothingAndExportsNothing()
    {
        var root = Directory.CreateTempSubdirectory("bs-channel").FullName;
        try
        {
            var session = TabSession.Open(Path.Combine(root, "s"));
            var off = session.NewTrack(statusChannel: false)!;
            var on = session.NewTrack(statusChannel: true)!;
            Assert.False(off.StatusChannel);
            Assert.True(on.StatusChannel);
            Assert.Empty(TabChannel.Environment(off));
            var env = TabChannel.Environment(on);
            Assert.Equal($"{on.Dir}/{on.Id}.events", env[TabChannel.EventsVariable]);
            Assert.Equal($"{on.Dir}/{on.Id}.answers", env[TabChannel.AnswersVariable]);
            Assert.Equal("1", env[TabChannel.VersionVariable]);
            Assert.Empty(Directory.GetFileSystemEntries(session.Directory, "*.events"));
            Assert.Empty(Directory.GetFileSystemEntries(session.Directory, "*.answers"));
            // Off, the tab script has nothing of the channel (#248 exports and registers it only when on).
            var tab = new TabConfig { Name = "Example", Directory = "~/example", Llm = "claude" };
            Assert.DoesNotContain("BOOSTERAI_TAB", Launcher.TabScript(tab, null, off));
            Assert.Contains("BOOSTERAI_TAB", Launcher.TabScript(tab, null, on));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
