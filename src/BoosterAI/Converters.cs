using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BoosterAI;

public static class Converters
{
    /// <summary>Localised name of an AI CLI id (only "skal"/shell differs from its id).</summary>
    public static readonly IValueConverter LlmName =
        new FuncValueConverter<string?, string?>(id => id is null ? null : Loc.Display("llm", id));

    private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#E5484D"));

    /// <summary>Red for errors; otherwise <see cref="AvaloniaProperty.UnsetValue"/> (not null: a null binding result
    /// would set a local null Foreground that hides the text instead of letting the muted style apply).</summary>
    public static readonly IValueConverter ErrorBrush = new ErrorBrushConverter();

    internal sealed class ErrorBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value is true ? Error : AvaloniaProperty.UnsetValue;

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
