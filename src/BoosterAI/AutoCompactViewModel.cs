using System.Globalization;

namespace BoosterAI;

public sealed record CompactChoice(string Id, string Label);

/// <summary>Edits a launch preference without touching a live CLI or its configuration.</summary>
public sealed class AutoCompactViewModel : Observable
{
    private IList<AutoCompactSetting> _settings;
    private bool _manual = true, _hasDraft;
    public bool CanEdit => _manual;
    public string EditExplanation => Loc.Get(_manual ? "compact.edit.manual" : "compact.edit.locked");
    public void UpdateManualMode(bool manual)
    {
        _manual = manual;
        foreach (var name in new[] { nameof(CanEdit), nameof(CanSave), nameof(Validation), nameof(EditExplanation), nameof(ShowBudget), nameof(BudgetPercent), nameof(BudgetHint) }) Raise(name);
    }
    public void UpdateSettings(IList<AutoCompactSetting> settings)
    {
        _settings = settings;
        if (!_hasDraft) Load();
    }
    private readonly Action _saved;
    private IReadOnlyDictionary<string, int> _codex = new Dictionary<string, int>();
    private string _model, _context = "", _custom = "";
    private CompactChoice _profile, _mode;
    private bool _loading;
    private string _status = "";
    public string Cli { get; }
    private bool _currentInterpreter;
    public bool Supported => AutoCompact.Supports(Cli) && (Cli != "interpreter" || _currentInterpreter);
    public void UpdateCliSupport(CliStatus status)
    {
        _currentInterpreter = status.Installed && status.Help.Contains("--oss", StringComparison.Ordinal) && status.Help.Contains("--config", StringComparison.Ordinal);
        Raise(nameof(Supported)); Raise(nameof(UnitExplanation)); Raise(nameof(CanSave));
    }
    public bool IsExpanded { get; set; }
    public IReadOnlyList<string> Models => Catalog.ModelsFor(Cli);
    public IReadOnlyList<CompactChoice> Profiles { get; } = Choices("profile", "questions", "code", "orchestra");
    public IReadOnlyList<CompactChoice> Modes { get; } = Choices("mode", "default", "recommended", "custom");
    private static IReadOnlyList<CompactChoice> Choices(string kind, params string[] ids) => ids.Select(id => new CompactChoice(id, Loc.Get("compact." + kind + "." + id))).ToArray();

    public string Model { get => _model; set { if (Set(ref _model, value ?? "")) Load(); } }
    public CompactChoice Profile { get => _profile; set { if (value is not null && Set(ref _profile, value)) Load(); } }
    public CompactChoice Mode { get => _mode; set { if (value is not null && Set(ref _mode, value)) Changed(); } }
    public string Context { get => _context; set { if (Set(ref _context, value ?? "")) Changed(); } }
    public string Custom { get => _custom; set { if (Set(ref _custom, value ?? "")) Changed(); } }
    public bool IsCustom => Mode.Id == "custom";
    public bool ShowBudget => CanEdit && AutoCompact.Resolve(Draft()) is not null && ContextValue > 0;
    public double BudgetPercent => ShowBudget ? Math.Clamp(100d * AutoCompact.Resolve(Draft())!.Value / ContextValue, 0, 100) : 0;
    public string BudgetValue => AutoCompact.Resolve(Draft()) is { } tokens ? Number(tokens) : "—";
    public string BudgetCapacity => Loc.Format("compact.visual.capacity", Number(ContextValue));
    public string BudgetHint => Loc.Get(Mode.Id == "default" ? "compact.visual.default" : ShowBudget ? "compact.visual.known" : "compact.visual.unknown");
    public string Validation => !CanEdit || !IsCustom || CanSave ? "" : Loc.Get("compact.custom.invalid");
    private static string Number(int value) => Loc.Language == "sv" ? value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ' ) : value.ToString("N0", CultureInfo.InvariantCulture);
    private int ContextValue => int.TryParse(Context, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    private int CustomValue => int.TryParse(Custom, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    private int? KnownContext => Cli == "claude" ? AutoCompactModels.ClaudeContext(Model) : Cli == "codex" && _codex.TryGetValue(Model, out var n) ? n : null;
    public string ContextSource => Loc.Get(KnownContext is { } known && known == ContextValue
        ? Cli == "claude" ? "compact.source.claude" : "compact.source.codex" : "compact.source.manual");
    public string UnitExplanation => Loc.Get("compact.unit." + (Supported ? Cli : "unsupported"));
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Recommendation
    {
        get
        {
            if (Model == Catalog.StandardModel || !Catalog.IsSafeModel(Model)) return Loc.Get("compact.model.required");
            if (AutoCompact.Recommend(Cli, ContextValue, Profile.Id) is not { } r) return Loc.Get("compact.unknown");
            return Loc.Format("compact.recommendation", Number(r.Tokens), Number(r.ContextTokens),
                Number(r.ReserveTokens), Number(r.FileBudgetTokens));
        }
    }
    public bool CanSave => CanEdit && Supported && Catalog.IsSafeModel(Model) && Model != Catalog.StandardModel
        && (Mode.Id == "default" || Mode.Id == "recommended" && AutoCompact.Resolve(Draft()) is not null
            || Mode.Id == "custom" && (Context == "" || ContextValue is >= 1000 and <= AutoCompact.MaxContext) && AutoCompact.Resolve(Draft()) is not null);

    public AutoCompactViewModel(string cli, IList<AutoCompactSetting> settings, Action saved, string? model = null)
    {
        Cli = cli; _settings = settings; _saved = saved;
        IsExpanded = settings.Any(s => s.Cli == cli && s.Mode != "default");
        _model = model ?? settings.LastOrDefault(s => s.Cli == cli && s.Active)?.Model ?? Catalog.ModelsFor(cli).FirstOrDefault(m => m != Catalog.StandardModel) ?? Catalog.StandardModel;
        _profile = Profiles.FirstOrDefault(p => p.Id == settings.LastOrDefault(s => s.Cli == cli && s.Model == _model && s.Active)?.Profile) ?? Profiles[1];
        _mode = Modes[0];
        Load();
    }

    public void UpdateMetadata(IReadOnlyDictionary<string, int> codex)
    {
        _codex = codex;
        // Metadata never replaces a manual capacity or an unsaved edit.
        if (_context.Length == 0 && KnownContext is { } known) _context = known.ToString(CultureInfo.InvariantCulture);
        Changed(edited: false);
        Raise(nameof(Context));
    }

    private void Load()
    {
        _loading = true;
        var existing = _settings.LastOrDefault(s => s.Cli == Cli && s.Model == Model && s.Profile == Profile.Id);
        _mode = Modes.FirstOrDefault(m => m.Id == existing?.Mode) ?? Modes[0];
        var context = existing?.ContextTokens is > 0 ? existing.ContextTokens : KnownContext;
        _context = context?.ToString(CultureInfo.InvariantCulture) ?? "";
        _custom = existing?.CustomTokens is > 0 ? existing.CustomTokens.ToString(CultureInfo.InvariantCulture) : "";
        _loading = false;
        Status = "";
        foreach (var name in new[] { nameof(Mode), nameof(Context), nameof(Custom) }) Raise(name);
        _hasDraft = false;
        Changed(edited: false);
    }

    private AutoCompactSetting Draft() => new()
    { Cli = Cli, Model = Model, Profile = Profile.Id, Mode = Mode.Id, ContextTokens = ContextValue, CustomTokens = CustomValue };

    private void Changed(bool edited = true)
    {
        if (_loading) return;
        if (edited) _hasDraft = true;
        Status = "";
        foreach (var name in new[] { nameof(CanSave), nameof(IsCustom), nameof(Recommendation), nameof(ContextSource), nameof(Validation), nameof(ShowBudget), nameof(BudgetPercent), nameof(BudgetValue), nameof(BudgetCapacity), nameof(BudgetHint) }) Raise(name);
    }

    public void Save()
    {
        if (!CanSave) return;
        for (var i = _settings.Count - 1; i >= 0; i--)
        {
            var setting = _settings[i];
            if (setting.Cli != Cli || setting.Model != Model) continue;
            if (setting.Profile == Profile.Id) _settings.RemoveAt(i);
            else setting.Active = false;
        }
        _settings.Add(Draft());
        _hasDraft = false;
        _saved();
        Status = Loc.Get("compact.saved");
    }
    /// <summary>Reset only this agent's complete compaction history and current unsaved draft.</summary>
    public void Reset()
    {
        if (!CanEdit) return;
        for (var i = _settings.Count - 1; i >= 0; i--)
            if (_settings[i].Cli == Cli) _settings.RemoveAt(i);
        _model = Catalog.ModelsFor(Cli).FirstOrDefault(m => m != Catalog.StandardModel) ?? Catalog.StandardModel;
        _profile = Profiles[1];
        Load();
        Raise(nameof(Model)); Raise(nameof(Profile));
        _saved();
        Status = Loc.Get("compact.reset.done");
    }

}


public sealed partial class MainViewModel
{
    private readonly Dictionary<string, AutoCompactViewModel> _compactEditors = [];
    private IReadOnlyDictionary<string, int> _compactCodexModels = new Dictionary<string, int>();

    private AutoCompactViewModel CompactEditor(CliStatus status)
    {
        if (!_compactEditors.TryGetValue(status.Cli, out var editor))
        {
            editor = new AutoCompactViewModel(status.Cli, _cfg.AutoCompactSettings, () =>
            {
                foreach (var tab in Tabs) tab.RaiseRestartTip();
                Save();
            });
            editor.UpdateMetadata(_compactCodexModels);
            _compactEditors.Add(status.Cli, editor);
        }
        editor.UpdateManualMode(_cfg.AutoCompactMode == AutoCompactPolicy.Manual);
        editor.UpdateCliSupport(status);
        return editor;
    }

    private int _compactMetadataGeneration;

    internal async Task LoadCompactMetadataAsync(string distro, Func<string, Task<IReadOnlyDictionary<string, int>>>? read = null)
    {
        var generation = ++_compactMetadataGeneration;
        var models = await (read ?? AutoCompactModels.ReadCodexAsync)(distro);
        if (_cfg.WslDistro != distro || generation != _compactMetadataGeneration) return;
        ApplyCompactMetadata(models);
    }

    internal void ApplyCompactMetadata(IReadOnlyDictionary<string, int> models)
    {
        _compactCodexModels = models;
        foreach (var editor in _compactEditors.Values) editor.UpdateMetadata(models);
        foreach (var tab in Tabs) tab.RaiseRestartTip();
        Raise(nameof(CompactMetadataStatus));
    }

    private void InvalidateCompactMetadata()
    {
        _compactMetadataGeneration++;
        ApplyCompactMetadata(new Dictionary<string, int>());
    }

    public IReadOnlyList<CompactChoice> CompactGlobalModes { get; } =
        new[] { AutoCompactPolicy.Off, AutoCompactPolicy.Auto, AutoCompactPolicy.Manual }
            .Select(id => new CompactChoice(id, Loc.Get("compact.global." + id))).ToArray();
    public CompactChoice SelectedCompactMode
    {
        get => CompactGlobalModes.First(c => c.Id == AutoCompactPolicy.Normalize(_cfg.AutoCompactMode));
        set
        {
            if (value is null || !CompactGlobalModes.Contains(value) || _cfg.AutoCompactMode == value.Id) return;
            _cfg.AutoCompactMode = value.Id;
            RefreshCompactMode();
        }
    }
    public string CompactModeExplanation => Loc.Get("compact.global." + SelectedCompactMode.Id + ".detail");
    public bool CompactIsAuto => SelectedCompactMode.Id == AutoCompactPolicy.Auto;
    public string CompactMetadataStatus => Loc.Get(_compactCodexModels.Count > 0 ? "compact.global.metadata.ready" : "compact.global.metadata.missing");

    private void RefreshCompactMode()
    {
        foreach (var editor in _compactEditors.Values) editor.UpdateManualMode(_cfg.AutoCompactMode == AutoCompactPolicy.Manual);
        foreach (var tab in Tabs) tab.RaiseRestartTip();
        foreach (var name in new[] { nameof(SelectedCompactMode), nameof(CompactModeExplanation), nameof(CompactIsAuto), nameof(CompactMetadataStatus) }) Raise(name);
    }
}
