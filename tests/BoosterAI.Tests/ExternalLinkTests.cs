using System.ComponentModel;
using System.Diagnostics;
using Xunit;

namespace BoosterAI.Tests;

public class ExternalLinkTests
{
    [Fact]
    public void MissingHandlerIsSwallowed()
    {
        // #175: no registered browser used to end the app from a button click.
        Assert.False(ExternalLink.Open("https://example.invalid/", _ => throw new Win32Exception(2, "No application is associated")));
        Assert.False(ExternalLink.Open("https://example.invalid/", _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void StartsTheUrlThroughTheShell()
    {
        ProcessStartInfo? seen = null;
        Assert.True(ExternalLink.Open("https://example.invalid/page", psi => { seen = psi; return null; }));
        Assert.Equal("https://example.invalid/page", seen?.FileName);
        Assert.True(seen?.UseShellExecute);
    }

    [Fact]
    public void LinkButtonsUseTheGuardedOpener()
    {
        var src = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BoosterAI");
        var offenders = Directory.GetFiles(src, "*.cs")
            // SetupViewModel's elevated WSL install (runas) is a program, not a link, and has its own handling.
            .Where(f => Path.GetFileName(f) is not ("ExternalLink.cs" or "SetupViewModel.cs"))
            .Where(f => File.ReadAllText(f).Contains("UseShellExecute = true"))
            .Select(Path.GetFileName);
        Assert.Empty(offenders);
    }
}
