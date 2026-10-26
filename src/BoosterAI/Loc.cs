using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Avalonia.Markup.Xaml;

namespace BoosterAI;

/// <summary>
/// UI text per language. The strings live in Resources/Strings.en.json and Strings.sv.json
/// (embedded). To add a string: add the same key to BOTH files (the tests fail otherwise),
/// then use {local:T Some.key} in XAML or Loc.Get("some.key") / Loc.Format(...) in code.
/// The language is chosen once at startup (see Init), so a change applies after a restart.
/// </summary>
public static class Loc
{
    public const string Auto = "auto";
    public static readonly IReadOnlyList<string> Languages = ["sv", "en"];

    private static IReadOnlyDictionary<string, string> _current = Load("en");
    private static IReadOnlyDictionary<string, string> _fallback = _current;

    public static string Language { get; private set; } = "en";

    /// <summary>Selects the language from the config value ("auto", "sv" or "en").</summary>
    public static void Init(string? setting) => Use(Resolve(setting, DetectOsLanguage()));

    public static void Use(string language)
    {
        Language = Languages.Contains(language) ? language : "en";
        _current = Load(Language);
    }

    /// <summary>An explicit choice wins; "auto" follows the OS: Swedish if Swedish, otherwise English.</summary>
    public static string Resolve(string? setting, string? osLanguage) => setting switch
    {
        "sv" => "sv",
        "en" => "en",
        _ => string.Equals(osLanguage, "sv", StringComparison.OrdinalIgnoreCase) ? "sv" : "en",
    };

    /// <summary>
    /// Two-letter OS UI language. Read from the OS directly because the app runs with
    /// InvariantGlobalization, where CultureInfo.CurrentUICulture is always invariant.
    /// </summary>
    public static string DetectOsLanguage()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                // Primary language id 0x1D is Swedish.
                return (GetUserDefaultUILanguage() & 0x3FF) == 0x1D ? "sv" : "en";
            }
            catch (Exception) { return "en"; }
        }
        return FromEnvironment(Environment.GetEnvironmentVariable);
    }

    /// <summary>POSIX order: LANGUAGE, LC_ALL, LC_MESSAGES, LANG; "sv", "sv_SE.UTF-8" and "sv:en" all count as Swedish.</summary>
    public static string FromEnvironment(Func<string, string?> get)
    {
        foreach (var name in new[] { "LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG" })
        {
            var v = get(name);
            if (string.IsNullOrWhiteSpace(v) || v is "C" or "POSIX") continue;
            var first = v.Split(':')[0];
            return first.Length >= 2 ? first[..2].ToLowerInvariant() : "en";
        }
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    public static string Get(string key) =>
        _current.TryGetValue(key, out var s) || _fallback.TryGetValue(key, out s) ? s : key;

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>All keys of a language, for the completeness tests.</summary>
    public static IReadOnlyDictionary<string, string> Table(string language) => Load(language);

    /// <summary>True for the "new tab" default name in any language (to rename it after the folder).</summary>
    public static bool IsDefaultTabName(string name) =>
        Languages.Any(l => Load(l)["tab.newName"] == name);

    /// <summary>Display text for a known id (llm.skal, theme.Neon); the id itself when none is defined.</summary>
    public static string Display(string prefix, string id) =>
        _current.TryGetValue(prefix + "." + id, out var s) ? s : id;

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith($"Strings.{language}.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }
}

/// <summary>XAML access: Text="{local:T settings.button}".</summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
