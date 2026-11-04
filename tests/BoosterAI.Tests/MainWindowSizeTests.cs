using BoosterAI;
using Xunit;

public class MainWindowSizeTests
{
    [Fact]
    public void MaximizingPreservesNormalSizeAndRestoringUpdatesIt()
    {
        var normal = new MainWindowSize(1240, 720);
        var maximized = normal.Observe(1920, 1080, normal: false, maximized: true);
        Assert.Equal(new MainWindowSize(1240, 720, true), maximized);
        Assert.Equal(new MainWindowSize(1300, 800), maximized.Observe(1300, 800, normal: true, maximized: false));
        Assert.Equal(normal, normal.Observe(0, 0, normal: true, maximized: false));
        Assert.Equal(normal, normal.Observe(double.NaN, 800, normal: true, maximized: false));
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("null")]
    [InlineData("{\"Width\":0,\"Height\":0}")]
    [InlineData("{\"Width\":1e9,\"Height\":800}")]
    public void BrokenOrOutOfRangeStateUsesDefaults(string json)
    {
        WithFile(file =>
        {
            File.WriteAllText(file, json);
            Assert.Equal(new MainWindowSize(), MainWindowSize.Read(file));
        });
    }

    [Fact]
    public void PersistsBothNormalSizeAndMaximizedStateWithoutConfigChanges()
    {
        WithFile(file =>
        {
            Assert.Equal(new MainWindowSize(), MainWindowSize.Read(file));
            var size = new MainWindowSize(1280, 740, true);
            size.Save(file);
            Assert.Equal(size, MainWindowSize.Read(file));
            Assert.False(File.Exists(file + ".tmp"));
            new MainWindowSize(0, 0).Save(file);
            Assert.Equal(size, MainWindowSize.Read(file));
        });
    }

    private static void WithFile(Action<string> action)
    {
        var dir = Path.Combine(Path.GetTempPath(), "booster-window-size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { action(Path.Combine(dir, "window.json")); }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
