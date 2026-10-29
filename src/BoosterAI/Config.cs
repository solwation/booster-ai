using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoosterAI;

/// <summary>One terminal tab: where it opens and which AI CLI starts in it.</summary>
public sealed class TabConfig
{
    public string Name { get; set; } = Loc.Get("tab.newName");
    /// <summary>Position in the colour theme; -1 in files from before themes (derived from Color).</summary>
    public int ColorIndex { get; set; } = -1;
    /// <summary>The resolved colour, written for readability and used by the launcher.</summary>
    public string Color { get; set; } = "#1E66F5";
    /// <summary>Linux path: inside WSL on Windows, local on Linux.</summary>
    public string Directory { get; set; } = "~";
    public bool Enabled { get; set; } = true;
    public string Llm { get; set; } = Catalog.DefaultLlm;
    public string Model { get; set; } = Catalog.StandardModel;
    public bool Orkester { get; set; }
    /// <summary>Run the orchestra command once non-interactively, then leave a shell. Ignored while Orkester is off.</summary>
    public bool NonInteractive { get; set; }
}

public sealed class AppConfig
{
    public List<TabConfig> Tabs { get; set; } = new();
    /// <summary>Windows only: the WSL distribution the tabs run in.</summary>
    public string WslDistro { get; set; } = "Ubuntu";
    /// <summary>Linux only: terminal to use, or "auto".</summary>
    public string LinuxTerminal { get; set; } = "auto";
    /// <summary>Name of the tab colour theme, see Catalog.Themes.</summary>
    public string ColorTheme { get; set; } = "Standard";
    /// <summary>UI language: "auto" (follow the OS), "sv" or "en". Applied at startup.</summary>
    public string Language { get; set; } = Loc.Auto;
    /// <summary>App appearance: "auto" (follow the system), "light" or "dark". Applied live. Not the tab colour themes.</summary>
    public string AppTheme { get; set; } = "auto";
    /// <summary>Close after Start (the behaviour before tab monitoring) instead of watching the tabs from the tray.</summary>
    public bool CloseAfterLaunch { get; set; }
    /// <summary>System notification when a watched tab closes, fails to start or its CLI fails.</summary>
    public bool Notifications { get; set; } = true;
    /// <summary>Global CLI zoom in percent of the terminal's default font size; 0 = terminal default (nothing is changed).</summary>
    public int ZoomPercent { get; set; }
    /// <summary>Ask GitHub at start and then at most once a day whether a newer release exists.</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Update claude, codex and agy in the tab before they start, at most every 6 hours per CLI (tab-lib.sh's bs_update).</summary>
    public bool AutoUpdateClis { get; set; } = true;
    /// <summary>A version the user chose to skip ("Hoppa över den här versionen"); it is not offered again.</summary>
    public string? SkippedVersion { get; set; }
    /// <summary>When the last update check finished (UTC); drives the once-a-day check.</summary>
    public DateTime? LastUpdateCheck { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(TabConfig))]
internal partial class ConfigJsonContext : JsonSerializerContext;

public static class ConfigStore
{
    /// <summary>%APPDATA%\BoosterAI on Windows, ~/.config/BoosterAI on Linux.</summary>
    public static string PathOnDisk { get; private set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BoosterAI", "config.json");

    /// <summary>Reads (and saves) this file instead of the real config; used by <c>--config</c> for screenshots.</summary>
    public static void UseFile(string path)
    {
        PathOnDisk = System.IO.Path.GetFullPath(path);
        _customFile = true;
    }

    // A --config file is never filled from the user's old BoosterStart config.
    private static bool _customFile;

    public static AppConfig Load()
    {
        if (!_customFile) LegacyNames.MigrateConfig(LegacyNames.OldConfigPath, PathOnDisk);
        try
        {
            if (File.Exists(PathOnDisk))
            {
                var cfg = JsonSerializer.Deserialize(File.ReadAllText(PathOnDisk), ConfigJsonContext.Default.AppConfig);
                if (cfg is not null) return cfg;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken file falls back to defaults; it is overwritten on the next save.
        }
        return Defaults();
    }

    /// <summary>The config as it is on disk now, or null when there is none or it cannot be read; never migrates.</summary>
    public static AppConfig? TryRead()
    {
        try
        {
            return File.Exists(PathOnDisk) ? JsonSerializer.Deserialize(File.ReadAllText(PathOnDisk), ConfigJsonContext.Default.AppConfig) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A detached copy, e.g. the baseline <see cref="ConfigMerge"/> compares against.</summary>
    public static AppConfig Copy(AppConfig cfg) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(cfg, ConfigJsonContext.Default.AppConfig), ConfigJsonContext.Default.AppConfig)!;

    public static void Save(AppConfig cfg)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathOnDisk)!);
        var tmp = PathOnDisk + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, ConfigJsonContext.Default.AppConfig));
        File.Move(tmp, PathOnDisk, overwrite: true);
    }

    /// <summary>First start: no projects; the start window asks for them.</summary>
    private static AppConfig Defaults() => new();
}

/// <summary>
/// Every running BoosterAI keeps the config it loaded and saves all of it (update checks every few minutes,
/// tab moves, Start, closing), and nothing stops a second instance (e.g. one started from the shortcut while
/// another waits in the tray). Before a save, settings that another instance changed on disk while this one
/// left them alone are taken over instead of being written back with the old value (#61: a zoom choice
/// reverted to 0). Tabs are not merged.
/// </summary>
public static class ConfigMerge
{
    private sealed record Setting(Func<AppConfig, object?> Get, Action<AppConfig, AppConfig> Take);

    private static Setting S<T>(Func<AppConfig, T> get, Action<AppConfig, T> set) => new(c => get(c), (to, from) => set(to, get(from)));

    private static readonly Setting[] Settings =
    [
        S(c => c.WslDistro, (c, v) => c.WslDistro = v),
        S(c => c.LinuxTerminal, (c, v) => c.LinuxTerminal = v),
        S(c => c.ColorTheme, (c, v) => c.ColorTheme = v),
        S(c => c.Language, (c, v) => c.Language = v),
        S(c => c.AppTheme, (c, v) => c.AppTheme = v),
        S(c => c.CloseAfterLaunch, (c, v) => c.CloseAfterLaunch = v),
        S(c => c.Notifications, (c, v) => c.Notifications = v),
        S(c => c.ZoomPercent, (c, v) => c.ZoomPercent = v),
        S(c => c.AutoUpdateClis, (c, v) => c.AutoUpdateClis = v),
        S(c => c.CheckForUpdates, (c, v) => c.CheckForUpdates = v),
        S(c => c.SkippedVersion, (c, v) => c.SkippedVersion = v),
        S(c => c.LastUpdateCheck, (c, v) => c.LastUpdateCheck = v),
    ];

    /// <summary>
    /// Copies into <paramref name="mine"/> each setting (not the tabs) that <paramref name="disk"/> changed since
    /// <paramref name="baseline"/> (what this instance last loaded or saved) while <paramref name="mine"/> still
    /// has the baseline value. True when anything was taken.
    /// </summary>
    public static bool TakeOthers(AppConfig mine, AppConfig baseline, AppConfig disk)
    {
        var taken = false;
        foreach (var s in Settings)
        {
            if (Equals(s.Get(mine), s.Get(baseline)) && !Equals(s.Get(disk), s.Get(baseline)))
            {
                s.Take(mine, disk);
                taken = true;
            }
        }
        return taken;
    }
}
