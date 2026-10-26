using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

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
    private string _name, _color, _directory, _llm, _model;
    private bool _enabled, _orkester;

    public TabViewModel(TabConfig c)
    {
        _name = c.Name; _color = c.Color; _directory = c.Directory; _enabled = c.Enabled;
        _llm = Catalog.Llms.Contains(c.Llm) ? c.Llm : Catalog.DefaultLlm;
        _model = string.IsNullOrWhiteSpace(c.Model) ? Catalog.StandardModel : c.Model;
        _orkester = c.Orkester;
    }

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Directory { get => _directory; set => Set(ref _directory, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool Orkester { get => _orkester; set => Set(ref _orkester, value); }
    public string Model { get => _model; set => Set(ref _model, value?.Trim() ?? ""); }

    public string Color
    {
        get => _color;
        set { if (Set(ref _color, value)) Raise(nameof(ColorBrush)); }
    }

    public IBrush ColorBrush => Avalonia.Media.Color.TryParse(_color, out var c) ? new SolidColorBrush(c) : Brushes.Gray;

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
            // Another CLI's model id rarely fits; fall back to its default.
            if (!Catalog.ModelsFor(value).Contains(Model)) Model = Catalog.StandardModel;
        }
    }

    public void NextColor()
    {
        var i = Catalog.Palette.ToList().FindIndex(p => string.Equals(p, Color, StringComparison.OrdinalIgnoreCase));
        Color = Catalog.Palette[(i + 1) % Catalog.Palette.Count];
    }

    public TabConfig ToConfig() => new()
    {
        Name = Name.Trim(), Color = Color, Directory = Directory.Trim(), Enabled = Enabled,
        Llm = Llm, Model = string.IsNullOrWhiteSpace(Model) ? Catalog.StandardModel : Model, Orkester = Orkester && IsAi,
    };
}

public sealed class MainViewModel : Observable
{
    private readonly AppConfig _cfg;
    private string _status = "";

    public MainViewModel(AppConfig cfg)
    {
        _cfg = cfg;
        Tabs = new ObservableCollection<TabViewModel>(cfg.Tabs.Select(t => new TabViewModel(t)));
    }

    public ObservableCollection<TabViewModel> Tabs { get; }
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsLinux => !OperatingSystem.IsWindows();
    public IReadOnlyList<string> LinuxTerminals => Launcher.LinuxTerminals;
    public string WslDistro { get => _cfg.WslDistro; set { _cfg.WslDistro = value.Trim(); Raise(); } }
    public string LinuxTerminal { get => _cfg.LinuxTerminal; set { _cfg.LinuxTerminal = value; Raise(); } }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string ConfigPath => ConfigStore.PathOnDisk;

    public void AddTab() => Tabs.Add(new TabViewModel(new TabConfig
    {
        Color = Catalog.Palette[Tabs.Count % Catalog.Palette.Count],
    }));

    public AppConfig Snapshot()
    {
        _cfg.Tabs = Tabs.Select(t => t.ToConfig()).ToList();
        return _cfg;
    }

    public void Save()
    {
        try { ConfigStore.Save(Snapshot()); }
        catch (Exception e) { Status = "Kunde inte spara: " + e.Message; }
    }

    /// <summary>Saves the choices and opens the terminal; true when the window can close.</summary>
    public bool Launch()
    {
        var cfg = Snapshot();
        var error = Launcher.Launch(cfg);
        Save();
        Status = error;
        return error == "";
    }
}
