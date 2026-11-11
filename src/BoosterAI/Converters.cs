using Avalonia.Data.Converters;

namespace BoosterAI;

public static class Converters
{
    public static Func<string, bool>? IsCliMissing { get; set; }

    private static bool Missing(string? id) => id is not null && id != Catalog.ShellOnly && IsCliMissing is { } check && check(id);

    /// <summary>Localised name of an AI CLI id (only "skal"/shell differs from its id).</summary>
    public static readonly IValueConverter LlmName =
        new FuncValueConverter<string?, string?>(id => id is null ? null : Loc.Display("llm", id));

    /// <summary>The CLI is not installed where the tabs run: the agent shows a warning marker after its name (#152).</summary>
    public static readonly IValueConverter LlmMissing = new FuncValueConverter<string?, bool>(Missing);

    /// <summary>The marker's tooltip and accessible name: "codex saknas".</summary>
    public static readonly IValueConverter LlmMissingName =
        new FuncValueConverter<string?, string?>(id => id is null ? null : Loc.Format("agent.missing", Loc.Display("llm", id)));
}
