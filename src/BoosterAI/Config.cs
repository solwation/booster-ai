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
