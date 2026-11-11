using Avalonia.Data.Converters;

namespace BoosterAI;

public static class Converters
{
    public static Func<string, bool>? IsCliMissing { get; set; }

    /// <summary>Localised name of an AI CLI id (only "skal"/shell differs from its id).</summary>
    public static readonly IValueConverter LlmName =
        new FuncValueConverter<string?, string?>(id =>
        {
            if (id is null) return null;
            var name = Loc.Display("llm", id);
            return id != Catalog.ShellOnly && IsCliMissing is { } check && check(id)
                ? $"{name} ({Loc.Get("setup.missingTag")})"
                : name;
        });
}
