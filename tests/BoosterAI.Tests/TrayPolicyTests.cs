using Avalonia.Controls;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

public class TrayPolicyTests
{
    [Fact]
    public void MinimizeToTrayIsOnByDefault() => Assert.True(new AppConfig().MinimizeToTray);

    [Fact]
    public void UsersCloseHidesWhenSettingAndTrayAreOn() =>
        Assert.True(TrayPolicy.HideOnClose(true, true, WindowCloseReason.WindowClosing, false, false));

    [Theory]
    [InlineData(false, true, WindowCloseReason.WindowClosing, false, false)] // setting off: today's behaviour
    [InlineData(true, false, WindowCloseReason.WindowClosing, false, false)] // no tray: never an invisible app
    [InlineData(true, true, WindowCloseReason.WindowClosing, true, false)]   // Close() after Start
    [InlineData(true, true, WindowCloseReason.ApplicationShutdown, false, false)] // update install, Shutdown()
    [InlineData(true, true, WindowCloseReason.OSShutdown, false, false)]
    [InlineData(true, true, WindowCloseReason.WindowClosing, false, true)]   // tray Quit
    public void OtherClosesReallyClose(bool setting, bool tray, WindowCloseReason reason, bool programmatic, bool quit) =>
        Assert.False(TrayPolicy.HideOnClose(setting, tray, reason, programmatic, quit));

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void MinimizeHidesOnlyWithSettingAndTray(bool setting, bool tray, bool expected) =>
        Assert.Equal(expected, TrayPolicy.HideOnMinimize(setting, tray));
}
