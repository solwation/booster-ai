using System.Collections.ObjectModel;

namespace BoosterAI;

/// <summary>One entry of the wizard's step indicator.</summary>
public sealed record WizardStepItem(int Number, string Label, bool IsCurrent, bool IsPast);

/// <summary>One agent of the picker in the settings step, with whether it is installed (checked in the background).</summary>
public sealed class LlmChoice(string id) : Observable
{
    private bool? _installed;
    private bool _checking = true;

    public string Id { get; } = id;
    public string Label => Loc.Display("llm", Id);

    /// <summary>True installed, false missing, null when the check could not tell (WSL missing, timeout).</summary>
    public bool? Installed { get => _installed; set { if (Set(ref _installed, value)) RaiseStatus(); } }
    public bool Checking { get => _checking; set { if (Set(ref _checking, value)) RaiseStatus(); } }
    public bool IsInstalled => _installed == true;
    public bool IsMissing => _installed == false;

    /// <summary>"claude · installerad", "codex · saknas, kan installeras efter start".</summary>
    public string StatusText => Loc.Format(_installed switch
    {
        true => "setup.agent.installed",
        false => "setup.agent.missing",
        _ => _checking ? "setup.agent.checking" : "setup.agent.unknown",
    }, Label);

    private void RaiseStatus()
    {
        Raise(nameof(IsInstalled));
        Raise(nameof(IsMissing));
        Raise(nameof(StatusText));
    }
}

/// <summary>An install-step sentence; a path it names is shown on its own line in the mono font, never wrapped.</summary>
public sealed record InstallLine(string Text, string? Path = null)
{
    public bool HasPath => Path is not null;
}

/// <summary>
/// The setup wizard (#131): welcome, basic settings, "Hitta projekt", install, done. Every step but
/// Install can be skipped; a skipped settings step changes nothing, a skipped project step adds nothing.
/// </summary>
public sealed class InstallWizardViewModel : Observable, IProjectList
{
    public enum Step { Welcome, Settings, Projects, Install, Done }

    private const int StepCount = 4;
    private readonly AppConfig? _existing;
    private readonly Action<string> _applyTheme;
    private Step _step;
    private bool _settingsSkipped, _working;
    private string _status = "", _error = "", _doneNote = "", _searchRoot, _searchRootDefault;
    private bool _projectsSkipped, _llmChosen, _detected;
    private LanguageChoice _language;
    private ThemeChoice _appearance;
    private LlmChoice _llm;

    /// <param name="plan">Where the install goes (a sample plan for screenshots).</param>
    /// <param name="existing">The config on disk, or null: it prefills the settings and is merged, never replaced.</param>
    /// <param name="installed">Whether an installed copy already exists (an upgrade).</param>
    /// <param name="applyTheme">Applies an appearance choice live ("auto", "light", "dark").</param>
    public InstallWizardViewModel(InstallPlan plan, AppConfig? existing, bool installed, Action<string> applyTheme)
    {
        Plan = plan;
        _existing = existing;
        IsUpgrade = installed;
        _applyTheme = applyTheme;
        _language = LanguageChoices.FirstOrDefault(c => c.Id == existing?.Language) ?? LanguageChoices[0];
        _appearance = AppearanceChoices.FirstOrDefault(c => c.Id == existing?.AppTheme) ?? AppearanceChoices[0];
        LlmChoices = [.. Catalog.Llms.Where(l => l != Catalog.ShellOnly).Select(l => new LlmChoice(l))];
        // An agent the config's projects already use stays; otherwise the check below picks an installed one.
        _llmChosen = existing?.Tabs.Any(t => Catalog.Llms.Contains(t.Llm) && t.Llm != Catalog.ShellOnly) == true;
        _llm = LlmChoices.FirstOrDefault(c => c.Id == Installer.PreferredLlm(existing)) ?? LlmChoices[0];
        _searchRoot = existing?.ProjectSearchRoot ?? "";
        _searchRootDefault = ProjectScanner.DefaultRoot();
        Items.CollectionChanged += (_, _) => RaiseProjects();
    }

    public InstallPlan Plan { get; }
    public bool IsUpgrade { get; }

    /// <summary>Raised after the language choice changed and Loc switched, so the window can rebuild its texts.</summary>
    public event Action? LanguageChanged;

    public Step Current
    {
        get => _step;
        set
        {
            if (!Set(ref _step, value)) return;
            RaiseStep();
        }
    }

    private void RaiseStep()
    {
        foreach (var n in new[] { nameof(IsWelcome), nameof(IsSettings), nameof(IsProjects), nameof(IsInstall), nameof(IsDone),
                     nameof(Steps), nameof(StepText), nameof(CanBack), nameof(CanSkip), nameof(ShowNext), nameof(ShowInstall),
                     nameof(SkipLabel), nameof(ShowSteps), nameof(InstallLines) })
            Raise(n);
    }

    public bool IsWelcome => _step == Step.Welcome;
    public bool IsSettings => _step == Step.Settings;
    public bool IsProjects => _step == Step.Projects;
    public bool IsInstall => _step == Step.Install;
    public bool IsDone => _step == Step.Done;
    public bool ShowSteps => _step != Step.Done;

    public IReadOnlyList<WizardStepItem> Steps =>
    [
        .. new[] { "setup.step.welcome", "setup.step.settings", "setup.step.projects", "setup.step.install" }
            .Select((key, i) => new WizardStepItem(i + 1, Loc.Get(key), (int)_step == i, (int)_step > i)),
    ];

    public string StepText => Loc.Format("setup.step", Math.Min((int)_step + 1, StepCount), StepCount);

    public bool IsWorking { get => _working; private set { if (Set(ref _working, value)) { Raise(nameof(CanBack)); Raise(nameof(CanSkip)); Raise(nameof(CanInstall)); } } }
    public bool CanBack => _step is Step.Settings or Step.Projects or Step.Install && !_working;
    public bool CanSkip => _step is Step.Welcome or Step.Settings or Step.Projects && !_working;
    public bool ShowNext => _step is Step.Welcome or Step.Settings or Step.Projects;
    public bool ShowInstall => _step == Step.Install;
    public bool CanInstall => !_working;
    public string SkipLabel => Loc.Get(_step == Step.Welcome ? "setup.button.installNow" : "setup.button.skip");

    // ---- Settings ----

    public IReadOnlyList<LanguageChoice> LanguageChoices { get; } = [new(Loc.Auto), new("sv"), new("en")];
    public IReadOnlyList<ThemeChoice> AppearanceChoices { get; } = [new("auto"), new("light"), new("dark")];
    public IReadOnlyList<LlmChoice> LlmChoices { get; }

    /// <summary>
    /// Applies what the background check found (installed or not, per agent). Unless the user or the config already
    /// chose an agent, the default moves to an installed one, so the first run does not end in "agent missing".
    /// </summary>
    public void ApplyDetection(SetupStatus status)
    {
        _detected = true;
        foreach (var c in LlmChoices)
        {
            c.Checking = false;
            c.Installed = status.Checked ? status.Cli(c.Id)?.Installed ?? false : null;
        }
        if (!_llmChosen && !_llm.IsInstalled && LlmChoices.FirstOrDefault(c => c.IsInstalled) is { } installed)
            SetLlm(installed);
        Raise(nameof(InstallLines));
    }

    /// <summary>Starts the check off the UI thread; a failed check leaves every agent as "status unknown".</summary>
    public async Task DetectAsync(Func<SetupStatus> detect)
    {
        SetupStatus status;
        try { status = await Task.Run(detect); }
        catch (Exception) { status = SetupStatus.Empty; }
        ApplyDetection(status);
    }

    private void SetLlm(LlmChoice value)
    {
        if (!Set(ref _llm, value, nameof(SelectedLlm))) return;
        Raise(nameof(InstallLines));
    }

    public LanguageChoice SelectedLanguage
    {
        get => _language;
        set
        {
            if (value is null || !Set(ref _language, value)) return;
            Loc.Use(Loc.Resolve(value.Id, Loc.DetectOsLanguage()));
            LanguageChanged?.Invoke();
        }
    }

    public ThemeChoice SelectedAppearance
    {
        get => _appearance;
        set
        {
            if (value is null || !Set(ref _appearance, value)) return;
            _applyTheme(value.Id);
        }
    }

    public LlmChoice SelectedLlm
    {
        get => _llm;
        set
        {
            if (value is null) return;
            _llmChosen = true;
            SetLlm(value);
        }
    }

    /// <summary>What the settings step contributes; null when it was skipped.</summary>
    public SetupSettings? Settings => _settingsSkipped ? null : new(_language.Id, _appearance.Id, _llm.Id);

    // ---- Projects ----

    /// <summary>Every project found in this step, ticked or not (the list of <see cref="ProjectPickList"/>).</summary>
    public ObservableCollection<ImportItem> Items { get; } = [];
    public bool HasItems => Items.Count > 0;

    /// <summary>The ticked projects: only these are added by the install.</summary>
    public IReadOnlyList<FoundProject> Projects => [.. Items.Where(i => i.Selected).Select(i => i.Project)];

    public string ProjectsText => (HasItems ? Projects.Count : 0) switch
    {
        _ when !HasItems => Loc.Get("setup.projects.none"),
        0 => Loc.Get("setup.projects.noneTicked"),
        1 => Loc.Get("setup.projects.one"),
        var n => Loc.Format("setup.projects.many", n),
    };

    /// <summary>The header check box: on when every project is ticked, mixed when some are; setting it ticks or clears all.</summary>
    public bool? AllSelected
    {
        get => Items.All(i => i.Selected) ? Items.Count > 0 : Items.Any(i => i.Selected) ? null : false;
        set { foreach (var i in Items) i.Selected = value ?? true; }
    }

    private void SelectionChanged()
    {
        Raise(nameof(Projects));
        Raise(nameof(ProjectsText));
        Raise(nameof(AllSelected));
        Raise(nameof(InstallLines));
    }

    private void RaiseProjects()
    {
        Raise(nameof(HasItems));
        SelectionChanged();
    }

    /// <summary>The "Hitta projekt" base folder (AppConfig.ProjectSearchRoot); empty searches <see cref="SearchRootDefault"/>.</summary>
    public string SearchRoot { get => _searchRoot; set => Set(ref _searchRoot, (value ?? "").Trim()); }

    /// <summary>Shown as the field's placeholder: where an empty base folder searches (a neutral path in screenshots).</summary>
    public string SearchRootDefault { get => _searchRootDefault; set => Set(ref _searchRootDefault, value); }

    /// <summary>The folder the search starts in, as Settings' "Hitta projekt" resolves it.</summary>
    public string ScanRoot => ProjectScanner.ResolveRoot(_searchRoot, out _);

    /// <summary>The base folder to save: null (unchanged) when the project step was skipped or the value is the config's.</summary>
    public string? SearchRootToSave => _projectsSkipped || _searchRoot == (_existing?.ProjectSearchRoot ?? "").Trim() ? null : _searchRoot;

    /// <summary>Local paths already known (config tabs and picked folders), left out of the next search.</summary>
    public IEnumerable<string> KnownPaths =>
        (_existing?.Tabs.Select(t => PathMapping.ToLocalPath(t.Directory, _existing.WslDistro, null)).OfType<string>() ?? [])
        .Concat(Items.Select(i => i.Path));

    public void AddProjects(IEnumerable<FoundProject> picked)
    {
        var known = Items.Select(i => ProjectScanner.Key(i.Path)).ToHashSet();
        foreach (var p in picked)
            if (known.Add(ProjectScanner.Key(p.Path))) Items.Add(new ImportItem(p, SelectionChanged) { Selected = true });
    }

    // ---- Install ----

    /// <summary>The install step's summary, one sentence per line.</summary>
    public IReadOnlyList<InstallLine> InstallLines
    {
        get
        {
            var lines = new List<InstallLine> { new(Loc.Get("setup.install.copy"), Plan.InstallDir), new(Loc.Get("setup.install.shortcuts")) };
            if (IsUpgrade) lines.Add(new(Loc.Get("setup.install.upgrade")));
            lines.Add(new(Settings is not null ? Loc.Format("setup.install.settings", _language.Label, _appearance.Label) : Loc.Get("setup.install.settingsKept")));
            var added = Projects.Count;
            // The agent the new projects start with: the chosen one, or the default when the settings step was skipped.
            var agent = _settingsSkipped ? LlmChoices.First(c => c.Id == Catalog.DefaultLlm) : _llm;
            lines.Add(new(added > 0
                ? Loc.Format("setup.install.projects", added, agent.Label)
                : Loc.Get("setup.install.noProjects")));
            if (_detected)
            {
                if (added > 0 && agent.IsMissing) lines.Add(new(Loc.Format("setup.install.agentMissing", agent.Label)));
                var missing = LlmChoices.Where(c => c.IsMissing && !(added > 0 && c == agent)).Select(c => c.Label).ToList();
                if (missing.Count > 0) lines.Add(new(Loc.Format("setup.install.missingAgents", string.Join(", ", missing))));
            }
            if (_existing is not null) lines.Add(new(Loc.Get("setup.install.config"), Plan.ConfigPath));
            lines.Add(new(Loc.Get("setup.install.start")));
            return lines;
        }
    }

    public string Status { get => _status; set => Set(ref _status, value); }
    public string Error { get => _error; set { if (Set(ref _error, value)) Raise(nameof(HasError)); } }
    public bool HasError => _error != "";
    public string DoneNote { get => _doneNote; set { if (Set(ref _doneNote, value)) Raise(nameof(HasDoneNote)); } }
    public bool HasDoneNote => _doneNote != "";

    // ---- Navigation ----

    public void Next()
    {
        if (_step == Step.Settings) _settingsSkipped = false;
        if (_step == Step.Projects) _projectsSkipped = false;
        if (_step < Step.Install) Current = _step + 1;
    }

    public void Back()
    {
        if (CanBack) Current = _step - 1;
    }

    /// <summary>Welcome: straight to Install with nothing changed. Settings: keep the config's values. Projects: add none.</summary>
    public void Skip()
    {
        switch (_step)
        {
            case Step.Welcome:
                _settingsSkipped = true;
                _projectsSkipped = true;
                Items.Clear();
                Current = Step.Install;
                break;
            case Step.Settings:
                _settingsSkipped = true;
                Current = Step.Projects;
                break;
            case Step.Projects:
                _projectsSkipped = true;
                Items.Clear();
                Current = Step.Install;
                break;
        }
    }

    /// <summary>Runs the install off the UI thread; on success shows the done page, on failure the error (Install can be retried).</summary>
    public async Task InstallAsync(Func<InstallPlan, SetupSettings?, IReadOnlyList<FoundProject>, string?, IProgress<string>, string?> install)
    {
        if (_working) return;
        IsWorking = true;
        Error = "";
        var progress = new Progress<string>(s => Status = s);
        var settings = Settings;
        var projects = Projects;
        var searchRoot = SearchRootToSave;
        try
        {
            DoneNote = await Task.Run(() => install(Plan, settings, projects, searchRoot, progress)) ?? "";
            Current = Step.Done;
        }
        catch (Exception e)
        {
            Error = Loc.Format("setup.error.failed", e.Message);
        }
        finally
        {
            Status = "";
            IsWorking = false;
        }
    }

    /// <summary>For --screenshot --window setup --section projects: a fixed, neutral selection (no disk scan).</summary>
    public void ShowSampleProjects(string root)
    {
        var sep = root.Contains('\\') ? '\\' : '/';
        AddProjects([
            new(root + sep + "webshop", "webshop", Git: true, Ai: true, Orchestra: true, OrchestraVersion: "0.9.0"),
            new(root + sep + "recipes", "recipes", Git: true, Ai: false),
            new(root + sep + "notes" + sep + "research", "research", Git: false, Ai: true, Orchestra: true),
        ]);
        // The screenshot shows the project step's choice: one project left unticked.
        Items[1].Selected = false;
    }

    /// <summary>For --section progress: the install running at its copy step.</summary>
    public void ShowSampleProgress()
    {
        IsWorking = true;
        Status = Loc.Get("setup.progress.copy");
    }
}
