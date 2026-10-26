using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BoosterStart;

public static class Converters
{
    private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#E5484D"));

    /// <summary>Red for errors; otherwise unset so the muted text style applies.</summary>
    public static readonly IValueConverter ErrorBrush =
        new FuncValueConverter<bool, IBrush?>(isError => isError ? Error : null);
}
