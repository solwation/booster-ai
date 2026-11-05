using System.Globalization;

namespace BoosterAI;

public sealed record CompactChoice(string Id, string Label);

/// <summary>Edits a launch preference without touching a live CLI or its configuration.</summary>
public sealed class AutoCompactViewModel : Observable
{
    private readonly IList<AutoCompactSetting> _settings;
    private readonly Action _saved;
    private IReadOnlyDictionary<string, int> _codex = new Dictionary<string, int>();
    private string _model, _context = "", _custom = "";
    private CompactChoice _profile, _mode;
    private bool _loading;
    private string _status = "";
    public string Cli { get; }
    public bool Supported => AutoCompact.Supports(Cli);
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
            return Loc.Format("compact.recommendation", r.Tokens.ToString("N0"), r.ContextTokens.ToString("N0"),
                r.ReserveTokens.ToString("N0"), r.FileBudgetTokens.ToString("N0"));
        }
    }
    public bool CanSave => Supported && Catalog.IsSafeModel(Model) && Model != Catalog.StandardModel
        && (Mode.Id == "default" || Mode.Id == "recommended" && AutoCompact.Resolve(Draft()) is not null
            || Mode.Id == "custom" && (Context == "" || ContextValue is >= 1000 and <= AutoCompact.MaxContext) && AutoCompact.Resolve(Draft()) is not null);

    public AutoCompactViewModel(string cli, IList<AutoCompactSetting> settings, Action saved, string? model = null)
    {
        Cli = cli; _settings = settings; _saved = saved;
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
        Changed();
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
        Changed();
    }

    private AutoCompactSetting Draft() => new()
    { Cli = Cli, Model = Model, Profile = Profile.Id, Mode = Mode.Id, ContextTokens = ContextValue, CustomTokens = CustomValue };

    private void Changed()
    {
        if (_loading) return;
        Status = "";
        foreach (var name in new[] { nameof(CanSave), nameof(IsCustom), nameof(Recommendation), nameof(ContextSource) }) Raise(name);
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
        _saved();
        Status = Loc.Get("compact.saved");
    }
}
