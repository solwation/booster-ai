using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Threading;

namespace BoosterStart;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TabViewModel : Observable
{
    private readonly MainViewModel _owner;
    private string _name, _directory, _llm, _model;
    private int _colorIndex;
    private bool _enabled, _orkester;
    private bool? _hasOrkester;

    public TabViewModel(TabConfig c, MainViewModel owner, int index)
    {
        _owner = owner;
        _name = c.Name; _directory = c.Directory; _enabled = c.Enabled;
        _colorIndex = c.ColorIndex >= 0 ? c.ColorIndex : LegacyIndex(c.Color, index);
        _llm = Catalog.Llms.Contains(c.Llm) ? c.Llm : Catalog.DefaultLlm;
        _model = string.IsNullOrWhiteSpace(c.Model) ? Catalog.StandardModel : c.Model;
        _orkester = c.Orkester;
    }

    public string Name { get => _name; set => Set(ref _name, value); }
    public bool Orkester { get => _orkester; set => Set(ref _orkester, value); }
    public string Model { get => _model; set => Set(ref _model, value?.Trim() ?? ""); }

    public string Directory
    {
        get => _directory;
        set { if (Set(ref _directory, value)) _owner.CheckOrkester(this); }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!Set(ref _enabled, value)) return;
            Raise(nameof(RowOpacity));
            _owner.TabsChanged();
        }
    }

    /// <summary>Unticked tabs stay editable but recede.</summary>
    public double RowOpacity => _enabled ? 1 : 0.45;

    /// <summary>Whether the directory has the shared orchestra; null until checked.</summary>
    public bool? HasOrkester
    {
        get => _hasOrkester;
        set
        {
            if (!Set(ref _hasOrkester, value)) return;
            Raise(nameof(CanOrkester));
            Raise(nameof(CanInstallOrkester));
        }
    }

    public bool CanOrkester => _hasOrkester == true && IsAi;

    /// <summary>What the "Installera" link will do, for the confirmation dialog.</summary>
    public string InstallSummary =>
        $"Vald AI ({Llm}) startar i en ny flik med {(Model == Catalog.StandardModel ? "sin standardmodell" : "modellen " + Model)}, " +
        "läser projektet och frågar dig om språk, publicering och arbetsregler innan den skriver policyn. " +
        "Inget committas förrän du har godkänt.";
    public bool CanInstallOrkester => _hasOrkester == false && IsAi;

    public string Color => _owner.Theme.ColorAt(_colorIndex);
    public IBrush ColorBrush => new SolidColorBrush(Avalonia.Media.Color.Parse(Color));

    /// <summary>Called when the theme changes.</summary>
    public void RaiseColor()
    {
        Raise(nameof(Color));
        Raise(nameof(ColorBrush));
    }

    private static int LegacyIndex(string color, int fallback)
    {
        var i = Catalog.Themes[0].Colors.ToList().FindIndex(p => string.Equals(p, color, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : fallback;
    }

    /// <summary>Example shown in an empty directory field.</summary>
    public string DirectoryHint => OperatingSystem.IsWindows() ? @"C:\Users\du\Documents\projekt" : "~/projekt";
    public IReadOnlyList<string> Llms => Catalog.Llms;
    public IReadOnlyList<string> Models => Catalog.ModelsFor(_llm);
    public bool IsAi => _llm != Catalog.ShellOnly;

    public string Llm
    {
        get => _llm;
        set
        {
            if (value is null || !Set(ref _llm, value)) return;
            Raise(nameof(Models));
            Raise(nameof(IsAi));
            Raise(nameof(CanOrkester));
            Raise(nameof(CanInstallOrkester));
            // Another CLI's model id rarely fits; fall back to its default.
            if (!Catalog.ModelsFor(value).Contains(Model)) Model = Catalog.StandardModel;
        }
    }

    public void NextColor()
    {
        _colorIndex = (_colorIndex + 1) % _owner.Theme.Colors.Count;
        RaiseColor();
    }

    /// <summary>
    /// The saved choice keeps Orkester even while the directory lacks the orchestra;
    /// a launch only sends /orkester where it exists.
    /// </summary>
    public TabConfig ToConfig(bool forLaunch) => new()
    {
        Name = Name.Trim(), Color = Color, ColorIndex = _colorIndex, Directory = Directory.Trim(), Enabled = Enabled,
        Llm = Llm, Model = string.IsNullOrWhiteSpace(Model) ? Catalog.StandardModel : Model,
        Orkester = forLaunch ? Orkester && CanOrkester : Orkester,
    };
}

public sealed class MainViewModel : Observable
{
    private readonly AppConfig _cfg;
    private string _status = "";
    private bool _statusIsError;
    private string? _wslHome;

    public MainViewModel(AppConfig cfg)
    {
        _cfg = cfg;
        Tabs = new ObservableCollection<TabViewModel>(cfg.Tabs.Select((t, i) => new TabViewModel(t, this, i)));
        Tabs.CollectionChanged += (_, _) => TabsChanged();
        _ = InitAsync();
    }

    public ObservableCollection<TabViewModel> Tabs { get; }
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsLinux => !OperatingSystem.IsWindows();
    public IReadOnlyList<string> LinuxTerminals => Launcher.LinuxTerminals;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool StatusIsError { get => _statusIsError; private set => Set(ref _statusIsError, value); }

    public void ShowStatus(string text, bool error) { Status = text; StatusIsError = error; }

    public string WslDistro
    {
        get => _cfg.WslDistro;
        set { _cfg.WslDistro = value.Trim(); Raise(); _ = InitAsync(); }
    }

    public string LinuxTerminal { get => _cfg.LinuxTerminal; set { _cfg.LinuxTerminal = value; Raise(); } }

    public IReadOnlyList<ColorTheme> Themes => Catalog.Themes;
    public ColorTheme Theme => Catalog.Theme(_cfg.ColorTheme);

    public ColorTheme SelectedTheme
    {
        get => Theme;
        set
        {
            if (value is null || value.Name == _cfg.ColorTheme) return;
            _cfg.ColorTheme = value.Name;
            Raise();
            foreach (var t in Tabs) t.RaiseColor();
        }
    }

    public bool HasTabs => Tabs.Count > 0;
    public int EnabledCount => Tabs.Count(t => t.Enabled);
    public bool CanLaunch => EnabledCount > 0;
    public string LaunchLabel => EnabledCount switch { 0 => "Ingen flik vald", 1 => "Starta 1 flik", var n => $"Starta {n} flikar" };

    public void TabsChanged()
    {
        Raise(nameof(HasTabs));
        Raise(nameof(EnabledCount));
        Raise(nameof(CanLaunch));
        Raise(nameof(LaunchLabel));
    }

    public void AddTab()
    {
        var tab = new TabViewModel(new TabConfig { ColorIndex = Tabs.Count }, this, Tabs.Count);
        Tabs.Add(tab);
        CheckOrkester(tab);
    }

    /// <summary>Resolves the WSL home (for ~ paths on Windows), then checks every tab.</summary>
    private async Task InitAsync()
    {
        if (OperatingSystem.IsWindows()) _wslHome = await Task.Run(() => QueryWslHome(_cfg.WslDistro));
        foreach (var t in Tabs) CheckOrkester(t);
    }

    public void CheckOrkester(TabViewModel tab)
    {
        var local = PathMapping.ToLocalPath(tab.Directory, _cfg.WslDistro, _wslHome);
        if (local is null) { tab.HasOrkester = null; return; }
        Task.Run(() =>
        {
            bool? has;
            try { has = System.IO.Directory.Exists(local) ? PathMapping.HasOrkester(local) : null; }
            catch (Exception) { has = null; }
            Dispatcher.UIThread.Post(() =>
            {
                // Ignore a result for a directory the user has typed away from.
                if (PathMapping.ToLocalPath(tab.Directory, _cfg.WslDistro, _wslHome) == local) tab.HasOrkester = has;
            });
        });
    }

    private static string? QueryWslHome(string distro)
    {
        try
        {
            var psi = new ProcessStartInfo("wsl.exe") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-d", distro, "--exec", "printenv", "HOME" }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var home = p.StandardOutput.ReadToEnd().Trim();
            return p.WaitForExit(5000) && home.StartsWith('/') ? home : null;
        }
        catch (Exception) { return null; }
    }

    private AppConfig Snapshot(bool forLaunch)
    {
        _cfg.Tabs = Tabs.Select(t => t.ToConfig(forLaunch)).ToList();
        return _cfg;
    }

    public void Save()
    {
        try { ConfigStore.Save(Snapshot(forLaunch: false)); }
        catch (Exception e) { ShowStatus("Kunde inte spara: " + e.Message, error: true); }
    }

    /// <summary>Saves the choices and opens the terminal; true when the window can close.</summary>
    public bool Launch()
    {
        var error = Launcher.Launch(Snapshot(forLaunch: true));
        Save();
        ShowStatus(error, error: error != "");
        return error == "";
    }

    /// <summary>Shows the tab's directory in Explorer, Finder or the Linux file manager.</summary>
    public void OpenFolder(TabViewModel tab)
    {
        var local = PathMapping.ToLocalPath(tab.Directory, _cfg.WslDistro, _wslHome);
        if (local is null) { ShowStatus("Hittar inte WSL-hemkatalogen än; försök igen om en stund.", error: true); return; }
        if (!System.IO.Directory.Exists(local)) { ShowStatus($"Katalogen finns inte: {tab.Directory}", error: true); return; }
        try
        {
            var opener = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            var psi = new ProcessStartInfo(opener) { UseShellExecute = false };
            psi.ArgumentList.Add(local);
            Process.Start(psi);
        }
        catch (Exception e)
        {
            ShowStatus("Kunde inte öppna katalogen: " + e.Message, error: true);
        }
    }

    /// <summary>Opens one tab whose AI is asked to install the orchestra in that project.</summary>
    public void InstallOrkester(TabViewModel tab)
    {
        var cfg = new AppConfig
        {
            WslDistro = _cfg.WslDistro, LinuxTerminal = _cfg.LinuxTerminal,
            Tabs = [tab.ToConfig(forLaunch: false)],
        };
        cfg.Tabs[0].Enabled = true;
        cfg.Tabs[0].Name = tab.Name.Trim() + " (orkester)";
        var error = Launcher.Launch(cfg, Launcher.InstallOrkesterPrompt);
        // The files appear once the agent has worked; reopening BoosterStart or editing the path rechecks.
        ShowStatus(error != "" ? error : $"Installationen körs i en ny flik för {tab.Name.Trim()}.", error: error != "");
    }
}
