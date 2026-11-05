using System.Diagnostics;
using Avalonia.Threading;

namespace BoosterAI;

/// <summary>One CLI button in the setup bars: "Installera codex" or the CLI an alias is offered for.</summary>
public sealed record CliOffer(string Cli, string Label, string Tip);

/// <summary>One supported AI CLI listed in the Settings agents hub (#91).</summary>
public sealed class AgentInfo : Observable
{
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; internal set => Set(ref _isSelected, value); }
    public AutoCompactViewModel? Compact { get; init; }
    public CliStatus Detected { get; }
    public string Cli => Detected.Cli;
    public string Name => Loc.Display("llm", Cli);
    public string DisplayName => Cli switch { "claude" => "Claude Code", "codex" => "Codex", "agy" => "Antigravity", "aider" => "Aider", "interpreter" => "Open Interpreter", "openhands" => "OpenHands", _ => Name };
    private int _section;
    public int Section { get => _section; set { if (Set(ref _section, value)) { Raise(nameof(IsOverview)); Raise(nameof(IsPermissions)); Raise(nameof(IsContext)); } } }
    public bool IsOverview => Section == 0;
    public bool IsPermissions => Section == 1;
    public bool IsContext => Section == 2;
    public bool IsYolo => SelectedMode.Mode == AgentMode.Yolo;
    public string Description => Loc.Get("agents.description." + Cli);
    public bool IsInstalled => Detected.Installed;
    public string StatusText => Loc.Get(IsInstalled ? "setup.status.installed" : "setup.status.missing");
    public string VersionText => IsInstalled ? Detected.Version ?? Loc.Get("agents.version.unknown") : "—";
    public string InstallButtonText => Loc.Format(IsInstalled ? "agents.update" : "setup.cli.install", Cli);
    public string InstallTip => Loc.Format("setup.cli.install.tip", Prerequisites.InstallScript(Cli));
    public string UpdateInfo => Loc.Get("agents.update." + Cli);
    public IReadOnlyList<AgentModeChoice> ModeChoices { get; }
    public AgentMode CurrentMode { get; }
    public bool HasExternalDefinition => CurrentMode == AgentMode.Custom;
    public string CurrentModeText => Loc.Get(!IsInstalled ? "agents.alias.unverified" : CurrentMode != AgentMode.Custom
        ? "agents.mode." + CurrentMode : Detected.Kind == "function" ? "agents.alias.function" : Detected.Kind == "alias" ? "agents.alias.external" : "agents.alias.block");
    public string CurrentAliasCommand => Detected.Kind switch
    {
        "alias" => Detected.AliasDefinition ?? "",
        "function" => Detected.FunctionDefinition ?? "",
        "file" => Cli,
        _ => "",
    };
    public bool MissingDefinition => Detected.HasAlias && CurrentAliasCommand.Length == 0;
    public bool HasCurrentAliasCommand => CurrentAliasCommand.Length > 0;
    public string CurrentAliasExplanation => Loc.Get(!IsInstalled ? "agents.alias.unverified.detail"
        : Detected.Kind == "function" ? "agents.alias.function.detail"
        : Detected.Kind == "alias" ? HasExternalDefinition ? "agents.alias.external.detail" : "agents.alias.managed.detail"
        : HasExternalDefinition ? "agents.alias.block.detail" : "agents.alias.none.detail");
    public bool DefinitionTruncated => Detected.Kind == "function" && System.Text.Encoding.UTF8.GetByteCount(CurrentAliasCommand) >= 8192;
    public string AliasInspectCommand => "type " + Cli + (Detected.Kind == "function" ? "; declare -f " + Cli : "; alias " + Cli);
    public string AliasRecommendation => Loc.Get(ModeChoices.Any(c => c.Mode == AgentMode.AutoApprove) ? "agents.alias.recommend" : "agents.alias.recommend.basic");
    public bool CanSelectMode => IsInstalled;
    public bool CanApply => CanSelectMode && !HasExternalDefinition && SelectedMode.Mode != CurrentMode;
    public bool HasPresetPreview => SelectedMode.Mode != AgentMode.Custom;
    public string ModeExplanation => SelectedMode.Explanation;
    public string ModeCommand => AgentModes.AliasLine(Cli, SelectedMode);
    public bool HasModeCommand => ModeCommand != "";
    public string ModePreview => SelectedMode.Mode == AgentMode.Standard ? Cli : ModeCommand;
    public string ModePreviewEffect => Loc.Get(HasExternalDefinition ? "agents.alias.preview.external" : SelectedMode.Mode == AgentMode.Standard ? "agents.alias.remove" : "agents.alias.write");
    private AgentModeChoice _selectedMode;
    public AgentModeChoice SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (value is null || !Set(ref _selectedMode, value)) return;
            Raise(nameof(IsYolo)); Raise(nameof(CanApply)); Raise(nameof(HasPresetPreview)); Raise(nameof(ModePreview)); Raise(nameof(ModePreviewEffect)); Raise(nameof(ModeExplanation)); Raise(nameof(ModeCommand)); Raise(nameof(HasModeCommand));
        }
    }

    public AgentInfo(CliStatus status)
    {
        Detected = status;
        CurrentMode = AgentModes.Current(status);
        ModeChoices = CurrentMode == AgentMode.Custom
            ? [new AgentModeChoice(AgentMode.Custom, "", "agents.mode.custom.detail"), .. AgentModes.Choices(status)]
            : AgentModes.Choices(status);
        _selectedMode = ModeChoices.FirstOrDefault(c => c.Mode == CurrentMode) ?? ModeChoices[0];
    }
}

/// <summary>
/// The quiet setup bars in the main window (#74): WSL missing (Windows), CLIs missing in the tabs' shell with an
/// install button each, and the full-auto alias offer; plus the summary that Settings shows after a check.
/// </summary>
public sealed class SetupViewModel : Observable
{
    private SetupStatus _status = SetupStatus.Empty;
    private bool _checking, _checked, _missingDismissed;
    private IReadOnlyList<string> _rowLlms = [];
    private readonly HashSet<string> _dismissedAliases = [];

    public SetupStatus Status => _status;
    public bool IsChecking { get => _checking; set { if (Set(ref _checking, value)) { Raise(nameof(Summary)); Raise(nameof(CheckNotice)); } } }
    public string CheckNotice => IsChecking || !_status.Checked || WslVisible ? Summary : "";
    public Action? OnStatusChanged { get; set; }

    public IReadOnlyList<AgentInfo> Agents { get; private set; } = [];
    private AgentInfo? _selectedAgent;
    public AgentInfo? SelectedAgent
    {
        get => _selectedAgent;
        set
        {
            if ((value is not null && !Agents.Contains(value)) || ReferenceEquals(value, _selectedAgent)) return;
            if (_selectedAgent is { } previous) previous.IsSelected = false;
            Set(ref _selectedAgent, value);
            if (value is not null) value.IsSelected = true;
            Raise(nameof(IsAgentOverview));
        }
    }
    public bool IsAgentOverview => SelectedAgent is null;
    private Func<CliStatus, AutoCompactViewModel>? _compactFactory;
    public void ConfigureCompact(Func<CliStatus, AutoCompactViewModel> factory) { _compactFactory = factory; Refresh(); }

    public SetupViewModel()
    {
        Refresh();
    }

    public bool WslVisible => _status.Wsl is WslState.Missing or WslState.DistroMissing;
    public bool CanInstallWsl => _status.Wsl == WslState.Missing;
    public string WslText => _status.Wsl == WslState.DistroMissing
        ? Loc.Format("setup.wsl.distroMissing", _distro, string.Join(", ", _status.Distros))
        : Loc.Get("setup.wsl.missing");
    private string _distro = "";

    public IReadOnlyList<CliOffer> Missing { get; private set; } = [];
    public bool MissingVisible => Missing.Count > 0 && !_missingDismissed;
    public string MissingText
    {
        get
        {
            var text = Loc.Format("setup.cli.missing", Loc.Get(OperatingSystem.IsWindows() ? "setup.where.wsl" : "setup.where.local"),
                string.Join(", ", Missing.Select(m => m.Cli)));
            return Missing.Any(m => m.Cli == "codex") && !_status.NpmFound ? text + " " + Loc.Get("setup.cli.npmMissing") : text;
        }
    }

    public IReadOnlyList<string> Aliases { get; private set; } = [];
    public bool AliasVisible => Aliases.Count > 0 && !MissingVisible && !WslVisible;
    public string AliasText => Loc.Format("setup.alias.offer", string.Join(", ", Aliases));
    public string AliasTip => string.Join("\n", Aliases.Select(Prerequisites.AliasLine));

    /// <summary>What the last check found, for Settings.</summary>
    public string Summary
    {
        get
        {
            if (IsChecking) return Loc.Get("setup.summary.checking");
            if (!_checked) return Loc.Get("setup.summary.none");
            if (_status.Wsl is WslState.Missing or WslState.DistroMissing) return WslText;
            if (!_status.Checked) return Loc.Get("setup.summary.failed");
            return string.Join(" · ", Prerequisites.Clis.Select(c => _status.Cli(c) switch
            {
                { Installed: true, HasAlias: true } => Loc.Format("setup.summary.alias", c),
                { Installed: true } => Loc.Format("setup.summary.installed", c),
                _ => Loc.Format("setup.summary.missing", c),
            }));
        }
    }

    public void Apply(SetupStatus status, string distro)
    {
        _status = status;
        _distro = distro.Trim();
        _checked = true;
        Refresh();
        OnStatusChanged?.Invoke();
    }

    /// <summary>The project rows' CLIs changed: recompute what to offer without a new check.</summary>
    public void RowsChanged(IEnumerable<string> llms)
    {
        var list = llms.Distinct().OrderBy(l => l, StringComparer.Ordinal).ToList();
        if (list.SequenceEqual(_rowLlms)) return;
        _rowLlms = list;
        Refresh();
    }

    public void DismissMissing() { _missingDismissed = true; Refresh(); }

    public void DismissAliases() { _dismissedAliases.UnionWith(Aliases); Refresh(); }

    private void Refresh()
    {
        var selectedCli = SelectedAgent?.Cli;
        var sections = Agents.ToDictionary(a => a.Cli, a => a.Section);
        Missing = Prerequisites.MissingToOffer(_status, _rowLlms)
            .Select(c => new CliOffer(c, Loc.Format("setup.cli.install", c), Loc.Format("setup.cli.install.tip", Prerequisites.InstallScript(c)))).ToList();
        Aliases = Prerequisites.AliasesToOffer(_status, _dismissedAliases);
        Agents = Prerequisites.Clis.Select(c =>
        {
            var status = _status.Cli(c) ?? new CliStatus(c, "none", null);
            return new AgentInfo(status) { Compact = _compactFactory?.Invoke(status), Section = sections.GetValueOrDefault(c) };
        }).ToList();
        Raise(nameof(Agents));
        SelectedAgent = Agents.FirstOrDefault(a => a.Cli == selectedCli);
        foreach (var name in new[] { nameof(Status), nameof(WslVisible), nameof(CanInstallWsl), nameof(WslText), nameof(Missing), nameof(MissingVisible),
                     nameof(MissingText), nameof(Aliases), nameof(AliasVisible), nameof(AliasText), nameof(AliasTip), nameof(Summary), nameof(CheckNotice) })
            Raise(name);
    }
}

public sealed partial class MainViewModel
{
    /// <summary>The setup bars and the Settings check (#74).</summary>
    public SetupViewModel Setup { get; } = new();

    private bool _setupEnabled, _setupRunning;
    private DateTime _lastSetupCheck = DateTime.MinValue;
    private readonly HashSet<string> _installing = [];
    private DateTime _installUntil;
    private DispatcherTimer? _installTimer;

    /// <summary>Starts the background checks; only the real app calls it (tests and screenshots never run a shell).</summary>
    public void StartSetupChecks()
    {
        _setupEnabled = true;
        SetupRowsChanged();
        CheckSetup();
    }

    internal void SetupRowsChanged() => Setup.RowsChanged(Tabs.Select(t => t.Llm));

    /// <summary>The main window was activated: check again, at most every 30 seconds (an install may have finished).</summary>
    public void SetupWindowActive()
    {
        if (_setupEnabled && DateTime.UtcNow - _lastSetupCheck > TimeSpan.FromSeconds(30)) CheckSetup();
    }

    /// <summary>"Kontrollera" in Settings: also works when the automatic checks are off (they are only off in tests and screenshots).</summary>
    public void CheckSetupNow()
    {
        _setupEnabled = true;
        CheckSetup();
    }

    /// <summary>Runs Prerequisites.Detect off the UI thread (no window, no focus change) and updates the bars.</summary>
    public void CheckSetup()
    {
        if (!_setupEnabled || _setupRunning) return;
        _setupRunning = true;
        _lastSetupCheck = DateTime.UtcNow;
        Setup.IsChecking = true;
        var distro = _cfg.WslDistro;
        Task.Run(() => Prerequisites.Detect(distro)).ContinueWith(t =>
        {
            _setupRunning = false;
            Setup.IsChecking = false;
            if (!t.IsCompletedSuccessfully) return;
            Setup.Apply(t.Result, distro);
            if (t.Result.Cli("codex")?.Installed == true) _ = LoadCompactMetadataAsync(distro);
            SetupRowsChanged();
            var done = _installing.Where(c => t.Result.Cli(c) is { Installed: true }).ToList();
            if (done.Count > 0)
            {
                _installing.ExceptWith(done);
                ShowStatus(Loc.Format("status.cliInstalled", string.Join(", ", done)), error: false);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Opens a visible tab that runs the CLI's official installer; the check repeats until it is found.</summary>
    public void InstallCli(string cli)
    {
        var cfg = new AppConfig
        {
            WslDistro = _cfg.WslDistro, LinuxTerminal = _cfg.LinuxTerminal, ZoomPercent = _cfg.ZoomPercent,
            Tabs = [new TabConfig { Name = Loc.Format("setup.cli.install", cli), Directory = "~", Llm = Catalog.ShellOnly, Script = Prerequisites.InstallScript(cli) }],
        };
        var error = Launcher.Launch(cfg);
        ShowStatus(error != "" ? error : Loc.Format("status.cliInstallRunning", cli, Prerequisites.LoginCommand(cli)), error: error != "");
        if (error != "") return;
        // New tabs run a new login shell, so they find the CLI once it is installed; the bars follow within 15 s.
        _installing.Add(cli);
        _installUntil = DateTime.UtcNow.AddMinutes(20);
        _installTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) =>
        {
            if (_installing.Count == 0 || DateTime.UtcNow > _installUntil) { _installTimer?.Stop(); return; }
            CheckSetup();
        });
        _installTimer.Start();
    }

    /// <summary>Starts the elevated, visible <c>wsl --install</c> after the user confirmed (Windows only).</summary>
    public void InstallWsl()
    {
        try
        {
            var (exe, args) = Prerequisites.WslInstall;
            var psi = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi)?.Dispose();
            ShowStatus(Loc.Get("status.wslInstallStarted"), error: false);
        }
        catch (Exception e)
        {
            // Includes the user's "No" in the UAC prompt.
            ShowStatus(Loc.Format("status.wslInstallFailed", e.Message), error: true);
        }
    }

    /// <summary>The ~/.bashrc of the tabs' Linux as this process can read it; null until the WSL home is known.</summary>
    private string? BashrcFile => PathMapping.ToLocalPath("~/.bashrc", _cfg.WslDistro, _wslHome);

    /// <summary>Adds the offered full-auto aliases to BoosterAI's block in ~/.bashrc (after the user confirmed).</summary>
    public void AddAliases()
    {
        if (BashrcFile is not { } path) { ShowStatus(Loc.Get("status.wslHomeMissing"), error: true); return; }
        var clis = Setup.Aliases.ToList();
        try
        {
            var current = File.Exists(path) ? File.ReadAllText(path) : null;
            var updated = Prerequisites.AddAliases(current, clis);
            // Written in place, so the file keeps its permissions.
            if (updated != (current ?? "")) File.WriteAllText(path, updated);
            ShowStatus(Loc.Format("status.aliasesAdded", "~/.bashrc", string.Join(", ", clis)), error: false);
            Setup.DismissAliases();
            CheckSetup();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowStatus(Loc.Format("status.aliasesFailed", path, e.Message), error: true);
        }
    }

    /// <summary>Called only by the Apply action after its required confirmation; never changes a running CLI.</summary>
    public bool ApplyAgentMode(AgentInfo agent, bool yoloConfirmed)
    {
        if (!agent.CanApply || (agent.SelectedMode.Mode == AgentMode.Yolo && !yoloConfirmed)) return false;
        if (BashrcFile is not { } path) { ShowStatus(Loc.Get("status.wslHomeMissing"), error: true); return false; }
        try
        {
            var current = File.Exists(path) ? File.ReadAllText(path) : null;
            var updated = AgentModes.Change(current, agent.Detected, agent.SelectedMode);
            if (updated != (current ?? "")) File.WriteAllText(path, updated);
            ShowStatus(Loc.Format("agents.mode.applied", agent.Name, agent.SelectedMode.Label), error: false);
            CheckSetup();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowStatus(Loc.Format("agents.mode.failed", e.Message), error: true);
            return false;
        }
    }

    /// <summary>Neutral, deterministic versions and permission states; never reads an installed CLI or a user file.</summary>
    public void ShowSampleAgents()
    {
        const string modern = "--approve-for-me --ask-for-approval --sandbox --dangerously-bypass-approvals-and-sandbox --oss --config";
        Setup.Apply(new SetupStatus(WslState.Ok, ["Ubuntu"],
        [
            new("claude", "alias", "/sample/bin/claude") { Version = "2.1.304", Help = "--autocompact acceptEdits --permission-mode --dangerously-skip-permissions", AliasDefinition = "alias claude='claude --permission-mode acceptEdits'", ManagedAlias = "alias claude='claude --permission-mode acceptEdits'" },
            new("codex", "file", "/sample/bin/codex") { Version = "0.170.0", Help = modern },
            new("agy", "function", "/sample/bin/agy") { Version = "1.4.0", Help = "--mode auto --dangerously-skip-permissions", FunctionDefinition = "agy () {\n    command agy --mode=auto \"$@\";\n}" },
            new("aider", "file", "/sample/bin/aider") { Help = "--yes-always" },
            new("interpreter", "file", "/sample/bin/interpreter") { Version = "0.0.59", Help = modern },
            new("openhands", "none", null),
        ], true, true), _cfg.WslDistro);
    }

    /// <summary>For --screenshot --fake-setup: codex and agy missing (no npm), claude installed without an alias.</summary>
    public void ShowSampleSetup(bool wslMissing = false)
    {
        Setup.Apply(wslMissing
            ? new SetupStatus(WslState.Missing, [], [], false, false)
            : new SetupStatus(WslState.Ok, ["Ubuntu"], [new("claude", "file", "/home/me/.local/bin/claude"), new("codex", "none", null), new("agy", "none", null)], false, false),
            _cfg.WslDistro);
        Setup.RowsChanged(["claude", "codex", "agy"]);
    }

    /// <summary>For --screenshot --fake-alias: every CLI installed, claude and codex without an alias.</summary>
    public void ShowSampleAlias()
    {
        Setup.Apply(new SetupStatus(WslState.Ok, ["Ubuntu"],
            [new("claude", "file", "/home/me/.local/bin/claude"), new("codex", "file", "/home/me/.nvm/bin/codex"), new("agy", "alias", "/home/me/.local/bin/agy")], true, false),
            _cfg.WslDistro);
        Setup.RowsChanged(Tabs.Select(t => t.Llm));
    }
}
