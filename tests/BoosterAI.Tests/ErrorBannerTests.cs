using BoosterAI;
using Xunit;

/// <summary>The error banner (#153): every error kind has a recovery action, and errors never land in the status line.</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class ErrorBannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-error-banner-" + Guid.NewGuid().ToString("N"));

    public ErrorBannerTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    public static TheoryData<ErrorKind> Kinds() => [.. Enum.GetValues<ErrorKind>()];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryErrorKindShownInTheBannerHasOneOrTwoActions(ErrorKind kind)
    {
        var actions = ErrorBannerViewModel.ActionsFor(kind);
        Assert.InRange(actions.Count, 1, 2);
        Assert.Equal(actions.Count, actions.Distinct().Count());

        // Shown with what its actions need, the banner offers exactly those buttons, each with a label.
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Atlas" }] });
        vm.ShowError(kind, "Synthetic cause.", retry: () => { }, tab: vm.Tabs[0]);
        Assert.True(vm.Error.IsVisible);
        Assert.True(vm.Error.HasFirst);
        Assert.Equal(actions.Count > 1, vm.Error.HasSecond);
        // LocTests keeps the Swedish and English keys in step.
        Assert.All(actions, a => Assert.False(string.IsNullOrWhiteSpace(ErrorBannerViewModel.Label(a))));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AnActionIsNeverOfferedWithoutSomethingToDo(ErrorKind kind)
    {
        var banner = new ErrorBannerViewModel();
        var actions = ErrorBannerViewModel.ActionsFor(kind);
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Atlas" }] });
        if (actions.Contains(ErrorAction.Retry)) Assert.Throws<ArgumentException>(() => banner.Show(kind, "x", retry: null, tab: vm.Tabs[0]));
        if (actions.Contains(ErrorAction.Details)) Assert.Throws<ArgumentException>(() => banner.Show(kind, "x", retry: () => { }, tab: null));
        Assert.False(banner.IsVisible);
    }

    [Fact]
    public void AFailedStartShowsTheBannerWithRetryAndTerminalSettingsNotTheStatusLine()
    {
        var name = TestTerminal.FailPrefix + Guid.NewGuid().ToString("N");
        var vm = new MainViewModel(new AppConfig { Notifications = false, Tabs = [new() { Name = name, Directory = "~/alpha" }] });
        Assert.False(vm.Launch());
        Assert.True(vm.Error.IsVisible);
        Assert.Equal(ErrorKind.Launch, vm.Error.Kind);
        Assert.Contains("synthetic terminal failure", vm.Error.Text);
        Assert.Equal([ErrorAction.Retry, ErrorAction.TerminalSettings], vm.Error.Actions);
        Assert.Equal("", vm.Status);
        Assert.True(vm.Error.FirstIsPrimary);

        // "Försök igen" runs the start again: here it fails again and the banner returns; after a fix it is gone.
        var retried = 0;
        vm.LaunchFromWindow = () => { retried++; vm.Launch(); };
        vm.Error.Retry();
        Assert.Equal(1, retried);
        Assert.True(vm.Error.IsVisible);
        vm.Tabs[0].Name = "fixed-" + name;
        vm.Error.Retry();
        Assert.False(vm.Error.IsVisible);
        Assert.Single(TestTerminal.For("fixed-" + name));
    }

    [Fact]
    public void AnInvalidTabPointsToTheProjectSettingsInsteadOfARetry()
    {
        var vm = new MainViewModel(new AppConfig { Notifications = false, Tabs = [new() { Name = "Atlas", Directory = "~/a;b" }] });
        Assert.False(vm.Launch());
        Assert.Equal(ErrorKind.InvalidTab, vm.Error.Kind);
        Assert.Equal([ErrorAction.ProjectSettings], vm.Error.Actions);
        Assert.Equal("", vm.Status);
    }

    [Fact]
    public void NeutralAcknowledgementStaysInTheStatusLineAndRetiresAnOlderError()
    {
        var vm = new MainViewModel(new AppConfig());
        vm.ShowError(ErrorKind.Save, "Kunde inte spara: disk full", retry: () => { });
        Assert.True(vm.StatusIsError);
        vm.ShowStatus("Sparat");
        Assert.Equal("Sparat", vm.Status);
        Assert.False(vm.Error.IsVisible);
        Assert.False(vm.StatusIsError);
    }

    [Fact]
    public void TheCloseButtonHidesTheBannerWithoutRunningAnything()
    {
        var vm = new MainViewModel(new AppConfig());
        var ran = false;
        vm.ShowError(ErrorKind.Link, "Kunde inte öppna sidan", retry: () => ran = true);
        vm.Error.Hide();
        Assert.False(vm.Error.IsVisible);
        vm.Error.Retry();
        Assert.False(ran);
    }

    [Fact]
    public void AtMostOneBannerActionIsPrimary()
    {
        // The empty state's "Hitta projekt" is primary: no banner action may be.
        Assert.Equal(PrimaryBanner.None, MainViewModel.ChoosePrimary(true, true, true, true, true, 1, true));
        // The error outranks every other banner.
        Assert.Equal(PrimaryBanner.Error, MainViewModel.ChoosePrimary(false, true, true, true, true, 1, true));
        Assert.Equal(PrimaryBanner.Update, MainViewModel.ChoosePrimary(false, false, true, true, false, 0, true));
        Assert.Equal(PrimaryBanner.Wsl, MainViewModel.ChoosePrimary(false, false, false, false, true, 1, true));
        // Several missing agents stay secondary rather than pick one; the next banner may then be primary.
        Assert.Equal(PrimaryBanner.Optimization, MainViewModel.ChoosePrimary(false, false, false, false, false, 2, true));
        Assert.Equal(PrimaryBanner.Missing, MainViewModel.ChoosePrimary(false, false, false, false, false, 1, true));
        Assert.Equal(PrimaryBanner.None, MainViewModel.ChoosePrimary(false, false, false, false, false, 0, false));

        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Atlas" }] });
        vm.Update.ShowFake("0.37.0");
        Assert.True(vm.UpdatePrimary);
        vm.ShowError(ErrorKind.Save, "Kunde inte spara", retry: () => { });
        Assert.True(vm.Error.FirstIsPrimary);
        Assert.False(vm.UpdatePrimary);
        vm.Error.Hide();
        Assert.False(vm.Error.FirstIsPrimary);
        Assert.True(vm.UpdatePrimary);
    }

    [Fact]
    public void AnExitWithAnErrorKeepsItsLastLinesForThePopup()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Atlas" }] });
        var tab = vm.Tabs[0];
        tab.SetState(TabState.CliExited, 0);
        Assert.False(tab.IsExitError);
        Assert.False(tab.ShowsOutput);
        tab.SetState(TabState.CliExited, 1);
        Assert.True(tab.IsExitError);
        Assert.True(tab.ShowsOutput);
        Assert.True(tab.ShowReload);
        tab.SetOutput(["Error: Invalid API key"]);
        Assert.True(tab.HasOutput);
        Assert.False(tab.HasOutputNote);
    }

    [Fact]
    public void TheTerminalSettingsActionNamesTheRenamedTabAndTheBaseFolderPickerOpensProjects()
    {
        // #157: Optimering is now Minne och kontext; the WSL page keeps its name. The base folder moved to Projekt.
        var labels = BoosterAI.Tests.UiSession.Run(() =>
        {
            var previous = Loc.Language;
            try
            {
                Loc.Use("sv");
                var sv = (ErrorBannerViewModel.Label(ErrorAction.TerminalSettings, windows: true), ErrorBannerViewModel.Label(ErrorAction.TerminalSettings, windows: false));
                Loc.Use("en");
                return (sv, ErrorBannerViewModel.Label(ErrorAction.TerminalSettings, windows: true));
            }
            finally { Loc.Use(previous); }
        });
        Assert.Equal(("Öppna Inställningar → Minne och kontext → WSL och miljö", "Öppna Inställningar → Allmänt"), labels.sv);
        Assert.Equal("Open Settings → Memory and context → WSL & environment", labels.Item2);
        Assert.Equal([ErrorAction.ProjectSettings], ErrorBannerViewModel.ActionsFor(ErrorKind.SearchRootPicker));
    }
}
