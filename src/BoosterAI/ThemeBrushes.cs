using Avalonia;
using Avalonia.Media;

namespace BoosterAI;

/// <summary>Looks up the theme tokens in App.axaml from code, for the current theme.</summary>
public static class ThemeBrushes
{
    /// <summary>The brush for <paramref name="key"/> in the running app's theme; transparent without an app (unit tests).</summary>
    public static IBrush Get(string key)
    {
        var app = Application.Current;
        return app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : Brushes.Transparent;
    }
}
