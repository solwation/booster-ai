using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoosterAI;

/// <summary>Normal client size in device-independent pixels; minimizing never replaces it.</summary>
internal sealed record MainWindowSize(double Width = 1100, double Height = 560, bool Maximized = false)
{
    internal bool Valid => double.IsFinite(Width) && double.IsFinite(Height)
        && Width >= 900 && Height >= 380 && Width <= 100000 && Height <= 100000;

    internal MainWindowSize Observe(double width, double height, bool normal, bool maximized)
    {
        var next = normal ? new MainWindowSize(width, height, false) : this with { Maximized = maximized };
        return next.Valid ? next : this;
    }

    internal static string StateFile => Path.Combine(Path.GetDirectoryName(ConfigStore.PathOnDisk)!, "main-window.json");

    internal static MainWindowSize Read(string file)
    {
        try
        {
            var size = JsonSerializer.Deserialize(File.ReadAllText(file), MainWindowSizeJson.Default.MainWindowSize);
            return size is { Valid: true } ? size : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    internal void Save(string file)
    {
        if (!Valid) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            var temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, MainWindowSizeJson.Default.MainWindowSize));
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Retry on the next resize. */ }
    }
}

[JsonSerializable(typeof(MainWindowSize))]
internal partial class MainWindowSizeJson : JsonSerializerContext;
