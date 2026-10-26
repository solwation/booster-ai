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
    /// <summary>Close after Start (the behaviour before tab monitoring) instead of watching the tabs from the tray.</summary>
    public bool CloseAfterLaunch { get; set; }
    /// <summary>System notification when a watched tab closes, fails to start or its CLI fails.</summary>
    public bool Notifications { get; set; } = true;
    /// <summary>Global CLI zoom in percent of the terminal's default font size; 0 = terminal default (nothing is changed).</summary>
    public int ZoomPercent { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppConfig))]
internal partial class ConfigJsonContext : JsonSerializerContext;

public static class ConfigStore
{
    /// <summary>%APPDATA%\BoosterAI on Windows, ~/.config/BoosterAI on Linux.</summary>
    public static string PathOnDisk { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BoosterAI", "config.json");

    public static AppConfig Load()
    {
        LegacyNames.MigrateConfig(LegacyNames.OldConfigPath, PathOnDisk);
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
