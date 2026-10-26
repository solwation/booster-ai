using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BoosterAI;

public static class Converters
{
    /// <summary>Localised name of an AI CLI id (only "skal"/shell differs from its id).</summary>
    public static readonly IValueConverter LlmName =
        new FuncValueConverter<string?, string?>(id => id is null ? null : Loc.Display("llm", id));

    private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#E5484D"));

    /// <summary>Red for errors; otherwise unset so the muted text style applies.</summary>
    public static readonly IValueConverter ErrorBrush =
        new FuncValueConverter<bool, IBrush?>(isError => isError ? Error : null);
}
