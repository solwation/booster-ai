namespace BoosterAI;

/// <summary>
/// Clean-up and migration for the pre-rename name (BoosterStart). Every path is a literal,
/// fully specified location; nothing here uses wildcards or deletes a directory recursively
/// unless it is the old sessions folder.
/// </summary>
public static class LegacyNames
{
    public const string OldName = "BoosterStart";
    public const string OldAppId = "BoosterStart.App";

    /// <summary>Copies the old config to the new place when only the old one exists; the old file is kept.</summary>
    public static bool MigrateConfig(string oldPath, string newPath)
    {
        try
        {
            if (File.Exists(newPath) || !File.Exists(oldPath)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            File.Copy(oldPath, newPath, overwrite: false);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Removes the old sessions folder and the old toast icon under <paramref name="oldAppDir"/>
    /// (the old %LOCALAPPDATA%\BoosterStart), then the folder itself if nothing else is left in it.
    /// </summary>
    public static void RemoveOldLocalData(string oldAppDir)
    {
        try
        {
            var sessions = Path.Combine(oldAppDir, "sessions");
            if (Directory.Exists(sessions)) Directory.Delete(sessions, recursive: true);
            var icon = Path.Combine(oldAppDir, "boosterstart.png");
            if (File.Exists(icon)) File.Delete(icon);
            if (Directory.Exists(oldAppDir) && !Directory.EnumerateFileSystemEntries(oldAppDir).Any()) Directory.Delete(oldAppDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Removes the old Windows Terminal fragment file and its folder (only if empty afterwards).</summary>
    public static void RemoveOldFragment(string oldFragmentDir)
    {
        try
        {
            var file = Path.Combine(oldFragmentDir, "boosterstart.json");
            if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(oldFragmentDir) && !Directory.EnumerateFileSystemEntries(oldFragmentDir).Any()) Directory.Delete(oldFragmentDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static string OldLocalAppDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), OldName);

    public static string OldConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), OldName, "config.json");

    public static string OldFragmentDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "Windows Terminal", "Fragments", OldName);
}
