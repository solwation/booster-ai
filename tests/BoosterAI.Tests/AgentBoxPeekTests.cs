using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>#221: hovering anywhere over a row's closed agent box previews the same status popup as the status column.</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class AgentBoxPeekTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-agentpeek-" + Guid.NewGuid().ToString("N"));
    private readonly string _language = Loc.Language;

    public AgentBoxPeekTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
        Loc.Use("sv");
    }

    public void Dispose()
    {
        Loc.Use(_language);
        Directory.Delete(_root, recursive: true);
    }

    /// <summary>Whether a pointer can reach <paramref name="visual"/> through every ancestor up to <paramref name="top"/>.</summary>
    private static bool Reachable(Visual visual, Visual top)
    {
        for (var v = visual; v is not null && v != top; v = v.GetVisualParent())
            if (v is InputElement { IsHitTestVisible: false } || !v.IsVisible) return false;
        return true;
    }

    [Fact]
    public void HoverOverTheClosedAgentBoxShowsTheStatusPopup()
    {
        var results = UiSession.Run(() =>
        {
            Loc.Use("sv");
            var vm = new MainViewModel(new AppConfig { Tabs = [
                new TabConfig { Name = "Atlas", Directory = "/srv/atlas", Enabled = true },
                new TabConfig { Name = "Boreal", Directory = "/srv/boreal", Enabled = true, Llm = "codex" }] });
            var window = new MainWindow(vm) { Width = 1400, Height = 700 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                return window.GetVisualDescendants().OfType<ComboBox>().Where(b => b.Classes.Contains("agent-choice") && b.IsEffectivelyVisible)
                    .Select(box =>
                    {
                        var host = (Panel)box.GetVisualParent()!;
                        // Any reachable control inside the box or its meters with a tip of its own would win over the host's popup.
                        var competing = host.GetVisualDescendants().OfType<Control>()
                            .Where(c => ToolTip.GetTip(c) is not null && Reachable(c, host)).Select(c => c.GetType().Name).ToList();
                        var presenter = box.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "ContentPresenter");
                        return (Tip: ToolTip.GetTip(host), Competing: competing, ContentHittable: presenter.IsHitTestVisible,
                            Context: host.DataContext == box.DataContext && host.DataContext is TabViewModel);
                    }).ToList();
            }
            finally { window.Close(); }
        });
        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.IsType<StatusPeekView>(Assert.IsType<ToolTip>(r.Tip).Content);
            Assert.Empty(r.Competing);
            Assert.False(r.ContentHittable);
            Assert.True(r.Context);
        });
    }
}
