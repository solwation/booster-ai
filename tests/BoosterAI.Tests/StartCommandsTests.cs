using System.Text;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>The start command list (#255): discovery per agent, the old settings' migration and the command line it builds.</summary>
public class StartCommandsTests
{
    private static string B64(string s) => s.Length == 0 ? "-" : Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
    private static string Line(int i, string kind, string root, string path, string text) => $"bscmd {i} {kind} {B64(root)} {B64(path)} {B64(text)}";
    private static string Done(int i) => $"bscmd {i} done - - -";

    private static IReadOnlyList<StartCommand> One(string llm, params string[] lines) =>
        StartCommands.Parse(string.Join('\n', lines.Append(Done(0))), [new StartCommandRequest(llm, "/work/proj")])[0]!;

    [Fact]
    public void ClaudeCommandsUseSubdirectoriesAsNamespaceAndTheirFrontmatter()
    {
        var list = One("claude",
            "noise from .bashrc",
            Line(0, "cc-pc", "/work/proj/.claude/commands", "deploy.md", "---\ndescription: Ship it\n---\nDeploy now."),
            Line(0, "cc-pc", "/work/proj/.claude/commands", "docs/check-links.md", "---\ndescription: \"Find broken: links\"\nargument-hint: [path]\n---\n"),
            Line(0, "cc-pc", "/work/proj/.claude/commands", "orkester.md", "---\ndescription: orchestra\n---\n"),
            Line(0, "cc-uc", "/home/u/.claude/commands", "deploy.md", "---\ndescription: user copy\n---\n"),
            Line(0, "cc-uc", "/home/u/.claude/commands", "standup.md", "# Summarise yesterday\nbody"));
        Assert.Equal(["/deploy", "/docs:check-links", "/standup"], list.Select(c => c.Name));
        Assert.Equal(StartCommandSource.Project, list[0].Source);
        Assert.Equal("Ship it", list[0].Description); // the project's, not the user's copy
        Assert.Equal("Find broken: links", list[1].Description);
        // Without a description, the body's first line, as Claude Code shows it.
        Assert.Equal(("Summarise yesterday", StartCommandSource.User), (list[2].Description, list[2].Source));
    }

    [Fact]
    public void BrokenFrontmatterStillListsTheCommandWithoutDescription()
    {
        var list = One("claude",
            Line(0, "cc-pc", "/p/.claude/commands", "half.md", "---\ndescription: never closed\nbody"),
            Line(0, "cc-pc", "/p/.claude/commands", "odd.md", "---\n: no key\njust words\n  indented: x\n---\n"),
            Line(0, "cc-ps", "/p/.claude/skills", "fixer/SKILL.md", "---\nname: [broken\n---\n"));
        Assert.Equal(["/fixer", "/half", "/odd"], list.Select(c => c.Name));
        Assert.All(list, c => Assert.Null(c.Description));
    }

    [Fact]
    public void ClaudeSkillsAreListedUnlessNotUserInvocable()
    {
        var list = One("claude",
            Line(0, "cc-ps", "/p/.claude/skills", "release/SKILL.md", "---\nname: release-notes\ndescription: >\n  Draft the notes\n  for a release\n---\n"),
            Line(0, "cc-us", "/h/.claude/skills", "hidden/SKILL.md", "---\nname: hidden\nuser-invocable: false\n---\n"),
            Line(0, "cc-us", "/h/.claude/skills", "tidy/SKILL.md", "no frontmatter"));
        Assert.Equal(["/release-notes", "/tidy"], list.Select(c => c.Name));
        Assert.Equal("Draft the notes for a release", list[0].Description);
    }

    [Fact]
    public void ClaudePluginsCountOnlyWhenEnabledAndInScope()
    {
        const string installed = """
{ "version": 2, "plugins": {
  "kit@market": [ { "scope": "user", "installPath": "/h/.claude/plugins/cache/market/kit/1.0.0" } ],
  "off@market": [ { "scope": "user", "installPath": "/h/.claude/plugins/cache/market/off/2.0.0" } ],
  "local@market": [ { "scope": "project", "projectPath": "/elsewhere", "installPath": "/h/.claude/plugins/cache/market/local/1.0.0" } ] } }
""";
        var list = One("claude",
            Line(0, "cc-plugins", "", "", installed),
            Line(0, "cc-set-user", "", "", """{ "enabledPlugins": { "kit@market": true, "off@market": true, "local@market": true } }"""),
            Line(0, "cc-set-project", "", "", """{ "enabledPlugins": { "off@market": false } }"""),
            Line(0, "cc-plc", "/h/.claude/plugins/cache/market/kit/1.0.0/commands", "component.md", "---\ndescription: Scaffold\n---\n"),
            Line(0, "cc-pls", "/h/.claude/plugins/cache/market/kit/1.0.0/skills", "lint/SKILL.md", "---\nname: lint\n---\n"),
            Line(0, "cc-plc", "/h/.claude/plugins/cache/market/off/2.0.0/commands", "nope.md", ""),
            Line(0, "cc-plc", "/h/.claude/plugins/cache/market/local/1.0.0/commands", "nope2.md", ""));
        Assert.Equal(["/kit:component", "/kit:lint"], list.Select(c => c.Name));
        Assert.All(list, c => Assert.Equal(StartCommandSource.Plugin, c.Source));
    }

    [Fact]
    public void CodexSkillsAreDollarNamesAndAgyWorkflowsSlashNames()
    {
        var codex = One("codex",
            Line(0, "cx-ps", "/p/.agents/skills", "orkester/SKILL.md", "---\nname: orkester\n---\n"),
            Line(0, "cx-ps", "/p/.agents/skills", "notes/SKILL.md", "---\nname: release-notes\ndescription: Notes\n---\n"),
            Line(0, "cx-us", "/h/.codex/skills", "tidy/SKILL.md", ""));
        Assert.Equal(["$release-notes", "$tidy"], codex.Select(c => c.Name));
        var agy = One("agy",
            Line(0, "ag-pw", "/p/.agent/workflows", "deploy-preview.md", "---\ndescription: Preview\n---\n"),
            Line(0, "ag-uw", "/h/.gemini/config/global_workflows", "standup.md", ""));
        Assert.Equal(["/deploy-preview", "/standup"], agy.Select(c => c.Name));
        Assert.Equal([StartCommandSource.Project, StartCommandSource.User], agy.Select(c => c.Source));
    }

    [Fact]
    public void RequestsWithoutTheirDoneLineKeepTheOldList()
    {
        var output = Line(0, "cc-pc", "/p", "a.md", "") + "\n" + Done(0) + "\n" + Line(1, "cc-pc", "/q", "b.md", "");
        var result = StartCommands.Parse(output, [new("claude", "/p"), new("claude", "/q")]);
        Assert.Equal("/a", Assert.Single(result[0]!).Name);
        Assert.Null(result[1]);
        Assert.Equal(["claude", "/p", "", "codex", "~", "/h/acct"],
            StartCommands.ScriptArguments([new("claude", "/p"), new("codex", " ", "/h/acct")]));
    }

    /// <summary>The real script in bash, against an invented home and project (Linux; on Windows it runs inside WSL).</summary>
    [Fact]
    public void ScriptFindsTheFilesOfEachAgent()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "bs-cmds-" + Guid.NewGuid().ToString("N"));
        try
        {
            void Write(string path, string text)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
                File.WriteAllText(Path.Combine(root, path), text);
            }
            Write("proj/.claude/commands/ns/sub/deep.md", "---\ndescription: Deep\n---\n");
            Write("proj/.claude/commands/top.md", "x");
            Write("proj/.claude/skills/rel/SKILL.md", "---\nname: rel\n---\n");
            Write("proj/.agents/skills/notes/SKILL.md", "---\nname: notes\n---\n");
            Write("proj/_agents/workflows/ship.md", "");
            Write("home/.claude/commands/mine.md", "");
            Write("acct/commands/acct-only.md", "");
            Write("home/.agents/skills/tidy/SKILL.md", "");
            Write("home/.codex/skills/.system/hidden/SKILL.md", "");
            Write("home/.gemini/config/global_workflows/standup.md", "");
            var psi = Prerequisites.Hidden("bash", new[] { "-c", StartCommands.DiscoverScript, "bash" }
                .Concat(StartCommands.ScriptArguments([new("claude", root + "/proj"), new("claude", root + "/proj", root + "/acct"),
                    new("codex", root + "/proj"), new("agy", root + "/proj"), new("aider", root + "/proj"), new("claude", root + "/missing")])));
            psi.Environment["HOME"] = root + "/home";
            psi.Environment.Remove("CODEX_HOME");
            var (_, code, output) = Prerequisites.Run(psi, TimeSpan.FromSeconds(20));
            Assert.Equal(0, code);
            var r = StartCommands.Parse(output!, [new("claude", root + "/proj"), new("claude", root + "/proj", root + "/acct"),
                new("codex", root + "/proj"), new("agy", root + "/proj"), new("aider", root + "/proj"), new("claude", root + "/missing")]);
            Assert.Equal(["/ns:sub:deep", "/rel", "/top", "/mine"], r[0]!.Select(c => c.Name));
            Assert.Equal(["/ns:sub:deep", "/rel", "/top", "/acct-only"], r[1]!.Select(c => c.Name));
            Assert.Equal(["$notes", "$tidy"], r[2]!.Select(c => c.Name));
            Assert.Equal(["/ship", "/standup"], r[3]!.Select(c => c.Name));
            Assert.Empty(r[4]!);
            Assert.Empty(r[5]!);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void OldConfigsMigrate()
    {
        var cfg = ConfigStore.Parse("""
{ "Tabs": [
  { "Name": "On", "Orkester": true },
  { "Name": "Once", "Orkester": true, "NonInteractive": true },
  { "Name": "Off with a stale once", "Orkester": false, "NonInteractive": true,
    "SubTabs": [ { "Name": "Sub", "Orkester": false, "NonInteractive": true, "Command": "/x\n" } ] } ] }
""")!;
        var tabs = TabGroups.All(cfg.Tabs).ToList();
        Assert.Equal((true, "", false), (tabs[0].Orkester, tabs[0].Command, tabs[0].NonInteractive));
        Assert.Equal((true, true), (tabs[1].Orkester, tabs[1].NonInteractive));
        Assert.False(tabs[2].NonInteractive);
        Assert.Equal(("/x", true), (tabs[3].Command, tabs[3].NonInteractive));
        Assert.Equal("/orkester", StartCommands.Text(tabs[1]));
    }

    [Fact]
    public void CommandLineCarriesTheCommandAndItsArguments()
    {
        var tab = new TabConfig { Directory = "~/proj", Llm = "claude", Command = "/deploy", CommandArgs = "staging --fast" };
        Assert.EndsWith("claude '/deploy staging --fast' || true && exec bash", Launcher.TabScript(tab));
        Assert.True(Launcher.CanContinue(tab));

        tab.NonInteractive = true;
        var once = Launcher.TabScript(tab);
        Assert.Contains("claude -p '/deploy staging --fast'", once);
        Assert.DoesNotContain(Launcher.ForegroundWatchNote, once);
        Assert.False(Launcher.CanContinue(tab));
        Assert.True(StartCommands.RunsOnce(tab));
    }

    [Theory]
    [InlineData("codex", false, "codex -c tui.auto_recap=false '$release-notes 12'")]
    [InlineData("codex", true, "codex exec -c tui.auto_recap=false '$release-notes 12'")]
    [InlineData("agy", false, "agy -i '$release-notes 12'")]
    [InlineData("agy", true, "agy -p '$release-notes 12'")]
    public void EveryAgentGetsTheCommandAsItsFirstPrompt(string llm, bool once, string expected)
    {
        var tab = new TabConfig { Llm = llm, Command = "$release-notes", CommandArgs = "12", NonInteractive = once };
        Assert.Contains(expected, Launcher.TabScript(tab));
    }

    [Fact]
    public void OrchestraKeepsItsSpecialFormsWithArguments()
    {
        var codex = Launcher.TabScript(new TabConfig { Llm = "codex", Orkester = true, CommandArgs = "#12", Command = "/ignored" });
        Assert.Contains("$(test -d .agents/skills/orkester && echo '$orkester' || echo /orkester)' #12'", codex);
        var once = Launcher.TabScript(new TabConfig { Llm = "agy", Orkester = true, NonInteractive = true });
        Assert.Contains("-p '/orkester " + Launcher.ForegroundWatchNote + "'", once);
    }

    [Fact]
    public void ArgumentsNeverBreakTheTabScript()
    {
        // Windows Terminal splits on ';' and drops '"' (#124): both are encoded, newlines become spaces.
        var script = Launcher.TabScript(new TabConfig { Llm = "claude", Command = "/x", CommandArgs = "a; b \"c\"\nd" }, null,
            new TabTrack("/s/tab-lib.sh", "/s", "tab1"));
        Assert.DoesNotContain(';', script);
        Assert.DoesNotContain('"', script);
        Assert.Contains("'/x a'$'\\x3b'' b '$'\\x22''c'$'\\x22'' d'", script);
    }
}
