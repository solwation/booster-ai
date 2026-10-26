using System.Text.Json;
using System.Text.Json.Nodes;
using BoosterAI;
using Xunit;

public class AgyPermissionsTests
{
    [Theory]
    [InlineData("git status && gh auth status", "git,gh")]
    [InlineData("ls -la | grep x; wc -l f || echo no & sleep 1", "ls,grep,wc,echo,sleep")]
    [InlineData("FOO=1 BAR=x /usr/bin/npm test", "npm")]
    [InlineData("(cd src && dotnet build)", "cd,dotnet")]
    [InlineData("if test -f x\nthen cat x\nfi", "test,cat")]
    [InlineData("git log && git diff", "git")]
    [InlineData("\"$HOME/bin/tool\" run && ./x.sh", "tool,x.sh")]
    [InlineData("", "")]
    public void FindsTheProgramsOfACommandLine(string line, string expected) =>
        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries), AgyPermissions.Programs(line));

    [Fact]
    public void MissingSkipsFullyAllowedPrograms()
    {
        const string settings = """{ "permissions": { "allow": ["command(git)", "command(gh pr list)"] } }""";
        Assert.Equal(["gh", "npm"], AgyPermissions.Missing(settings, ["git", "gh", "npm"]));
        Assert.Empty(AgyPermissions.Missing("""{"permissions":{"allow":["command(*)"]}}""", ["npm"]));
        Assert.Equal(["git"], AgyPermissions.Missing(null, ["git"]));
        Assert.Equal(["git"], AgyPermissions.Missing("not json", ["git"]));
    }

    [Fact]
    public void AddRulesKeepsOtherSettingsAndRules()
    {
        const string settings = """
            {
              // agy writes plain JSON, but a hand edit may add comments
              "model": "Gemini",
              "trustedWorkspaces": ["/home/me/a"],
              "permissions": { "allow": ["command(git)"], "deny": ["command(rm)"] },
            }
            """;
        var result = JsonNode.Parse(AgyPermissions.AddRules(settings, ["git", "gh", "gh", "bad name"]))!;
        Assert.Equal("Gemini", (string?)result["model"]);
        Assert.Equal("/home/me/a", (string?)result["trustedWorkspaces"]![0]);
        Assert.Equal("command(rm)", (string?)result["permissions"]!["deny"]![0]);
        Assert.Equal(["command(git)", "command(gh)"], result["permissions"]!["allow"]!.AsArray().Select(n => (string)n!));
    }

    [Fact]
    public void AddRulesStartsAnEmptyFileAndRefusesABrokenOne()
    {
        Assert.Equal(["command(npm)"], JsonNode.Parse(AgyPermissions.AddRules("", ["npm"]))!["permissions"]!["allow"]!.AsArray().Select(n => (string)n!));
        Assert.ThrowsAny<JsonException>(() => AgyPermissions.AddRules("{ broken", ["npm"]));
        Assert.ThrowsAny<JsonException>(() => AgyPermissions.AddRules("[1]", ["npm"]));
    }
}
