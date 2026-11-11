namespace BoosterAI;

public sealed partial class MainViewModel
{
    public IReadOnlyList<CompactChoice> ContextModes { get; } =
        new[] { "off", "clear", "summary" }.Select(id => new CompactChoice(id, Loc.Get("settings.context.mode." + id))).ToArray();

    // Remember Auto versus Manual while switching the page's main choice; saved model budgets are never removed.
    private string? _contextSummaryMode;
    public IReadOnlyList<CompactChoice> ContextSummaryModes => CompactGlobalModes.Where(c => c.Id != AutoCompactPolicy.Off).ToArray();
    public bool ContextHasSummary => AutoCompactPolicy.Normalize(_cfg.AutoCompactMode) != AutoCompactPolicy.Off;
    public bool ContextHasLegacyCombination => _cfg.AutoClearIdle && ContextHasSummary;
    public CompactChoice? SelectedContextMode
    {
        get => ContextHasLegacyCombination ? null : ContextModes.Single(c => c.Id == (_cfg.AutoClearIdle ? "clear" : ContextHasSummary ? "summary" : "off"));
        set
        {
            if (value is null || !ContextModes.Contains(value) || value == SelectedContextMode) return;
            if (ContextHasSummary) _contextSummaryMode = _cfg.AutoCompactMode;
            _cfg.AutoClearIdle = value.Id == "clear";
            _cfg.AutoCompactMode = value.Id == "summary" ? _contextSummaryMode ?? AutoCompactPolicy.Auto : AutoCompactPolicy.Off;
            Raise(nameof(AutoClearIdle));
            RefreshCompactMode();
        }
    }

    private void RefreshContextMode()
    {
        foreach (var name in new[] { nameof(SelectedContextMode), nameof(ContextHasSummary), nameof(ContextHasLegacyCombination) }) Raise(name);
    }
}
