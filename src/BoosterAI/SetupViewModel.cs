using System.Diagnostics;
using Avalonia.Threading;

namespace BoosterAI;

/// <summary>One CLI installation button in the setup bar.</summary>
public sealed record CliOffer(string Cli, string Label, string Tip);

/// <summary>One supported AI CLI listed in the Settings agents hub (#91).</summary>
public sealed class AgentInfo : Observable
{
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; internal set => Set(ref _isSelected, value); }
    public AutoCompactViewModel? Compact { get; internal set; }
    /// <summary>The agent's accounts (#186); null for an agent that only has its default account.</summary>
    public AgentAccountsViewModel? Accounts { get; internal set; }
    public bool SupportsAccounts => Accounts is not null;
    public bool IsAccounts => Section == 3;
    public SettingsModelAdviceViewModel Advice { get; }
    public CliStatus Detected { get; private set; }
    private bool _statusKnown = true;
    public string Cli => Detected.Cli;
    public string Name => Loc.Display("llm", Cli);
    public string DisplayName => Cli switch { "claude" => "Claude Code", "codex" => "Codex", "agy" => "Antigravity", "aider" => "Aider", "interpreter" => "Open Interpreter", "openhands" => "OpenHands", _ => Name };
    private int _section;
    public int Section { get => _section; set { if (Set(ref _section, value)) { if (value == 3) _ = Accounts?.RefreshStatusAsync(); Raise(nameof(IsOverview)); Raise(nameof(IsPermissions)); Raise(nameof(IsContext)); Raise(nameof(IsAccounts)); Raise(nameof(ShowModelAdvice)); } } }
    public bool IsOverview => Section == 0;
    public bool ShowModelAdvice => Advice.IsSupported && (!IsInstalled || IsOverview);
    public bool IsPermissions => Section == 1;
    public bool IsContext => Section == 2;
    public bool IsYolo => SelectedMode.Mode == AgentMode.Yolo;
    public string Description => Loc.Get("agents.description." + Cli);
    public bool IsInstalled => Detected.Installed;
    public string StatusText => Loc.Get(!_statusKnown ? "agents.status.unknown.short" : IsInstalled ? "setup.status.installed" : "setup.status.missing");
    /// <summary>The overview card's status word (#158): Ready, Missing or Needs update, in plain language.</summary>
    public string CardStatusText => Loc.Get(CardStatusKey(_statusKnown, Detected.Installed, Detected.Outdated));
    internal static string CardStatusKey(bool known, bool installed, bool outdated) =>
        !known ? "agents.status.unknown.short" : !installed ? "agents.card.status.missing" : outdated ? "agents.card.status.outdated" : "agents.card.status.ready";
    /// <summary>Alias state shown on the detail page of a missing agent (moved off the overview card, #158).</summary>
    public string AliasStateText => Loc.Format("agents.card.aliasState", CurrentModeText);
    public bool CardDotReady => _statusKnown && IsInstalled && !Detected.Outdated;
    public bool CardDotMissing => _statusKnown && !IsInstalled;
    public bool CardDotOutdated => _statusKnown && IsInstalled && Detected.Outdated;
    public bool CardDotUnknown => !_statusKnown;
    /// <summary>The overview card's permissions row: "Asks before tools" or "Approves automatically".</summary>
    public string CardPermissionText => Loc.Format("agents.card.permissions", Loc.Get(CardPermissionKey(IsInstalled, CurrentMode)));
    internal static string CardPermissionKey(bool installed, AgentMode mode) => !installed ? "agents.card.perm.unknown" : mode switch
    {
        AgentMode.Standard => "agents.card.perm.ask",
        AgentMode.AutoApprove or AgentMode.Yolo => "agents.card.perm.auto",
        _ => "agents.card.perm.custom",
    };
    /// <summary>The card offers Install only for a missing agent whose status is known.</summary>
    public bool ShowCardInstall => _statusKnown && !IsInstalled;
    public bool ShowCardAdvice => Advice.IsSupported && !ShowCardInstall;
    public string CardInstallTip => InstallTip;
    public string VersionText => IsInstalled ? Detected.Version ?? Loc.Get("agents.version.unknown") : "—";
    public string InstallButtonText => Loc.Format(IsInstalled ? "agents.update" : "setup.cli.install", Cli);
    public string InstallTip => Loc.Format("setup.cli.install.tip", Prerequisites.InstallScript(Cli));
    public string UpdateInfo => Loc.Get("agents.update." + Cli);
    public IReadOnlyList<AgentModeChoice> ModeChoices { get; private set; }
    public AgentMode CurrentMode { get; private set; }
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
    public bool CanApply => CanSelectMode && SelectedMode.Mode != AgentMode.Custom && (SelectedMode.Mode != CurrentMode || CanRepairConfig);

    private IReadOnlyList<CodexConfigState> _codexConfigs = [];
    /// <summary>
    /// Codex's config.toml as the last check read it (#182); null for other agents or when it could not be read. With several
    /// accounts (#186) it is the one that disagrees with the alias, if any, so the warning covers every CODEX_HOME.
    /// </summary>
    public CodexConfigState? CodexConfig => global::BoosterAI.CodexConfig.Pick(_codexConfigs, CurrentMode == AgentMode.Yolo);
    /// <summary>The alias says YOLO, but Codex started without an interactive shell still uses config.toml's sandbox.</summary>
    public bool ConfigNeedsYolo => Cli == "codex" && IsInstalled && CurrentMode == AgentMode.Yolo && CodexConfig is { IsYolo: false };
    /// <summary>config.toml disables sandbox and approvals although the alias is not YOLO.</summary>
    public bool ConfigUnexpectedYolo => Cli == "codex" && IsInstalled && CurrentMode is AgentMode.Standard or AgentMode.AutoApprove && CodexConfig is { IsYolo: true };
    public bool HasConfigWarning => ConfigNeedsYolo || ConfigUnexpectedYolo;
    /// <summary>Applying the current mode again fixes config.toml: YOLO writes it, Standard/Auto restore BoosterAI's backup.</summary>
    public bool CanRepairConfig => SelectedMode.Mode == CurrentMode && (ConfigNeedsYolo || ConfigUnexpectedYolo && CodexConfig!.HasBackup);
    public string ConfigWarningText => ConfigNeedsYolo
        ? Loc.Format("agents.codexConfig.notYolo", ConfigSetting(global::BoosterAI.CodexConfig.SandboxKey, CodexConfig!.SandboxMode), ConfigSetting(global::BoosterAI.CodexConfig.ApprovalKey, CodexConfig.ApprovalPolicy))
        : ConfigUnexpectedYolo ? Loc.Format(CodexConfig!.HasBackup ? "agents.codexConfig.unexpectedYolo.backup" : "agents.codexConfig.unexpectedYolo", CurrentModeText) : "";
    private static string ConfigSetting(string key, string? value) => value is null ? Loc.Format("agents.codexConfig.unset", key) : $"{key} = \"{value}\"";

    internal void SetCodexConfig(CodexConfigState? state) => SetCodexConfigs(state is null ? [] : [state]);

    /// <summary>The config.toml of the default account first, then of each Codex account (#186).</summary>
    internal void SetCodexConfigs(IReadOnlyList<CodexConfigState> states)
    {
        if (Cli != "codex" || _codexConfigs.SequenceEqual(states)) return;
        _codexConfigs = states;
        foreach (var name in new[] { nameof(CodexConfig), nameof(ConfigNeedsYolo), nameof(ConfigUnexpectedYolo), nameof(HasConfigWarning), nameof(CanRepairConfig), nameof(ConfigWarningText), nameof(CanApply) })
            Raise(name);
    }
    public bool HasPresetPreview => SelectedMode.Mode != AgentMode.Custom;
    public string ModeExplanation => SelectedMode.Explanation;
    public string ModeCommand => AgentModes.AliasLine(Cli, SelectedMode);
    public bool HasModeCommand => ModeCommand != "";
    public string ModePreview => SelectedMode.Mode == AgentMode.Standard ? Cli : ModeCommand;
    public string ModePreviewEffect => Loc.Get(HasExternalDefinition || Detected.HasPermissionOverride
        ? SelectedMode.Mode == AgentMode.Standard ? "agents.alias.override.none" : "agents.alias.preview.external"
        : SelectedMode.Mode == AgentMode.Standard ? "agents.alias.remove" : "agents.alias.write");
    /// <summary>True while the change confirmation is open (#187); selection changes made meanwhile or by the revert are ignored.</summary>
    internal bool ModeChangePending { get; set; }
    /// <summary>Puts the selection back on the mode that actually applies; used when a change was cancelled or failed.</summary>
    internal void RevertSelection() => SelectedMode = ModeChoices.FirstOrDefault(c => c.Mode == CurrentMode) ?? ModeChoices[0];
    private AgentModeChoice _selectedMode;
    public AgentModeChoice SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (value is null || !Set(ref _selectedMode, value)) return;
            Raise(nameof(IsYolo)); Raise(nameof(CanApply)); Raise(nameof(CanRepairConfig)); Raise(nameof(HasPresetPreview)); Raise(nameof(ModePreview)); Raise(nameof(ModePreviewEffect)); Raise(nameof(ModeExplanation)); Raise(nameof(ModeCommand)); Raise(nameof(HasModeCommand));
        }
    }

    /// <summary>Refresh facts in place so mounted pages, selection and unsaved editors survive a background check.</summary>
    internal void RefreshDetection(CliStatus status, bool known)
    {
        if (Detected == status && _statusKnown == known) return;
        var draft = SelectedMode.Mode != CurrentMode ? SelectedMode.Mode : (AgentMode?)null;
        Detected = status;
        _statusKnown = known;
        CurrentMode = AgentModes.Current(status);
        ModeChoices = CurrentMode == AgentMode.Custom
            ? [new AgentModeChoice(AgentMode.Custom, "", "agents.mode.custom.detail"), .. AgentModes.Choices(status)]
            : AgentModes.Choices(status);
        _selectedMode = ModeChoices.FirstOrDefault(c => c.Mode == (draft ?? CurrentMode))
            ?? ModeChoices.FirstOrDefault(c => c.Mode == CurrentMode) ?? ModeChoices[0];
        foreach (var name in new[] { nameof(Detected), nameof(IsInstalled), nameof(ShowModelAdvice), nameof(StatusText), nameof(CardStatusText), nameof(AliasStateText), nameof(CardDotReady), nameof(CardDotMissing), nameof(CardDotOutdated), nameof(CardDotUnknown), nameof(CardPermissionText), nameof(ShowCardInstall), nameof(ShowCardAdvice), nameof(VersionText), nameof(InstallButtonText),
                     nameof(CurrentMode), nameof(ModeChoices), nameof(SelectedMode), nameof(IsYolo), nameof(HasExternalDefinition), nameof(CurrentModeText),
                     nameof(CurrentAliasCommand), nameof(MissingDefinition), nameof(HasCurrentAliasCommand), nameof(CurrentAliasExplanation),
                     nameof(DefinitionTruncated), nameof(AliasInspectCommand), nameof(AliasRecommendation), nameof(CanSelectMode), nameof(CanApply),
                     nameof(HasPresetPreview), nameof(ModeExplanation), nameof(ModeCommand), nameof(HasModeCommand), nameof(ModePreview), nameof(ModePreviewEffect),
                     nameof(ConfigNeedsYolo), nameof(ConfigUnexpectedYolo), nameof(HasConfigWarning), nameof(CanRepairConfig), nameof(ConfigWarningText) })
            Raise(name);
    }

    public AgentInfo(CliStatus status, SettingsHardwareAdviceState? hardwareAdvice = null)
    {
        Advice = new(status.Cli, hardwareAdvice ?? new SettingsHardwareAdviceState());
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
/// install button each, plus the summary that Settings shows after a check.
/// </summary>
public sealed class SetupViewModel : Observable
{
    private SetupStatus _status = SetupStatus.Empty;
    private SetupStatus _wslStatus = SetupStatus.Empty;
    private bool _checking, _checked, _missingDismissed, _checkFailed;
    private IReadOnlyList<string> _rowLlms = [];

    public SetupStatus Status => _status;
    public bool IsChecking { get => _checking; set { if (Set(ref _checking, value)) { Raise(nameof(Summary)); Raise(nameof(CheckNotice)); Raise(nameof(WslInstallationStatus)); } } }
    public string CheckNotice => IsChecking ? Loc.Get("agents.status.checking")
        : _checkFailed ? Loc.Get(_status.Checked ? "agents.status.failed.previous" : "agents.status.failed")
        : WslVisible ? WslText : !_status.Checked ? Loc.Get("agents.status.unknown") : "";
    public Action? OnStatusChanged { get; set; }

    public SettingsHardwareAdviceState HardwareAdvice { get; } = new();
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
    private Func<string, AgentAccountsViewModel?>? _accountsFactory;
    public void ConfigureAccounts(Func<string, AgentAccountsViewModel?> factory) { _accountsFactory = factory; Refresh(); }

    public SetupViewModel()
    {
        Refresh();
    }

    public bool WslVisible => _wslStatus.Wsl is WslState.Missing or WslState.DistroMissing;
    public bool CanInstallWsl => _wslStatus.Wsl == WslState.Missing;
    public string WslText => _wslStatus.Wsl == WslState.DistroMissing
        ? Loc.Format("setup.wsl.distroMissing", _distro, string.Join(", ", _wslStatus.Distros))
        : Loc.Get("setup.wsl.missing");
    private string _distro = "";

    /// <summary>WSL facts from the last automatic check, separate from AI program detection.</summary>
    public string WslInstallationStatus => IsChecking ? Loc.Get("setup.summary.checking")
        : !_checked ? Loc.Get("setup.summary.none")
        : Loc.Get(_wslStatus.Wsl switch
        {
            WslState.Ok => "wsl.info.ready",
            WslState.Missing => "wsl.info.missing",
            WslState.DistroMissing => "wsl.info.distroMissing",
            _ => "setup.summary.failed",
        });
    public string CheckedWslDistro => _checked && _distro.Length > 0 ? _distro : "—";
    public string InstalledWslDistros => !_checked || _wslStatus.Wsl == WslState.Unknown
        ? Loc.Get("wsl.info.unknown")
        : _wslStatus.Distros.Count == 0 ? Loc.Get("wsl.info.none") : string.Join("\n", _wslStatus.Distros);


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

    /// <summary>What the last check found, for Settings.</summary>
    public string Summary
    {
        get
        {
            if (IsChecking) return Loc.Get("setup.summary.checking");
            if (!_checked) return Loc.Get("setup.summary.none");
            if (_checkFailed) return Loc.Get("setup.summary.failed");
            if (_wslStatus.Wsl is WslState.Missing or WslState.DistroMissing) return WslText;
            if (!_status.Checked) return Loc.Get("setup.summary.failed");
            return string.Join(" · ", Prerequisites.Clis.Select(c => _status.Cli(c) switch
            {
                { Installed: true } => Loc.Format("setup.summary.installed", c),
                _ => Loc.Format("setup.summary.missing", c),
            }));
        }
    }

    public void Apply(SetupStatus status, string distro)
    {
        distro = distro.Trim();
        // WSL diagnostics describe this attempt even when previous CLI facts remain usable.
        _wslStatus = status;
        if (!status.Checked && status.Wsl is WslState.Ok or WslState.Unknown)
        {
            if (_distro != distro || !_status.Checked) _status = status;
            _distro = distro;
            _checked = true;
            _checkFailed = true;
        }
        else
        {
            _status = status;
            _distro = distro;
            _checked = true;
            _checkFailed = false;
        }
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

    private IReadOnlyList<CodexConfigState> _codexConfigs = [];
    /// <summary>Codex's config.toml as last read (#182), shown against its detected alias.</summary>
    public void SetCodexConfig(CodexConfigState? state) => SetCodexConfigs(state is null ? [] : [state]);

    /// <summary>The config.toml of every Codex account (#186), the default account first.</summary>
    public void SetCodexConfigs(IReadOnlyList<CodexConfigState> states)
    {
        _codexConfigs = states;
        foreach (var agent in Agents) agent.SetCodexConfigs(states);
    }

    private void Refresh()
    {
        Missing = Prerequisites.MissingToOffer(_status, _rowLlms)
            .Select(c => new CliOffer(c, Loc.Format("setup.cli.install", c), Loc.Format("setup.cli.install.tip", Prerequisites.InstallScript(c)))).ToList();
        if (Agents.Count == 0)
        {
            Agents = Prerequisites.Clis.Select(c => new AgentInfo(new(c, "none", null), HardwareAdvice)).ToList();
            Raise(nameof(Agents));
        }
        foreach (var agent in Agents)
        {
            var status = _status.Cli(agent.Cli) ?? new CliStatus(agent.Cli, "none", null);
            agent.RefreshDetection(status, _status.Checked);
            agent.SetCodexConfigs(_codexConfigs);
            if (_compactFactory is not null) agent.Compact = _compactFactory(status);
            if (_accountsFactory is not null) agent.Accounts = _accountsFactory(agent.Cli);
        }
        foreach (var name in new[] { nameof(Status), nameof(WslVisible), nameof(CanInstallWsl), nameof(WslText), nameof(Missing), nameof(MissingVisible),
                     nameof(MissingText), nameof(Summary), nameof(CheckNotice), nameof(WslInstallationStatus), nameof(CheckedWslDistro), nameof(InstalledWslDistros) })
            Raise(name);
    }
}

public sealed partial class MainViewModel
{
    /// <summary>The setup bars and the Settings check (#74).</summary>
    public SetupViewModel Setup { get; } = new();

    private bool _setupEnabled, _setupRunning, _setupPending;
    private DateTime _lastSetupCheck = DateTime.MinValue;
    private string? _lastSetupDistro;
    internal Func<DateTime> SetupUtcNow { get; set; } = () => DateTime.UtcNow;
    internal Func<string, Task<SetupStatus>> DetectSetupAsync { get; set; } = distro => Task.Run(() => Prerequisites.Detect(distro));
    internal Task SetupCheckTask { get; private set; } = Task.CompletedTask;
    private readonly HashSet<string> _installing = [];
    private DateTime _installUntil;
    private DispatcherTimer? _installTimer;

    /// <summary>Starts the background checks; only the real app calls it (tests and screenshots never run a shell).</summary>
    public void StartSetupChecks()
    {
        _setupEnabled = true;
        AccountStatusEnabled = true;
        AccountLimitsEnabled = true;
        // #234: the last shown levels are kept next to the config, so the first reading of this run is compared with them.
        LimitAnim = new(Path.Combine(Path.GetDirectoryName(ConfigStore.PathOnDisk) ?? "", "limit-levels.json"));
        ArmLimitAnimations();
        _ = RefreshAccountLimitsAsync(force: true);
        SetupRowsChanged();
        CheckSetup();
    }

    internal void SetupRowsChanged() => Setup.RowsChanged(Tabs.Select(t => t.Llm));

    /// <summary>Activation and opening AI agents share the same quiet, throttled check.</summary>
    public void SetupWindowActive() => RequestSetupCheck(onlyIfStale: true);
    public void SetupAgentsOpened() => RequestSetupCheck(onlyIfStale: true);

    /// <summary>Explicit lifecycle checks (startup, changed environment and installation) bypass the age limit.</summary>
    public void CheckSetup() => RequestSetupCheck(onlyIfStale: false);

    private void RequestSetupCheck(bool onlyIfStale)
    {
        if (!_setupEnabled) return;
        // An explicit check during a running one must not be lost (#176): queue exactly one more; throttled calls are dropped.
        if (_setupRunning) { if (!onlyIfStale) _setupPending = true; return; }
        if (onlyIfStale && _lastSetupDistro == _cfg.WslDistro && SetupUtcNow() - _lastSetupCheck < TimeSpan.FromSeconds(30)) return;
        _setupRunning = true;
        _lastSetupCheck = SetupUtcNow();
        _lastSetupDistro = _cfg.WslDistro;
        SetupCheckTask = RunSetupChecksAsync(_cfg.WslDistro);
    }

    /// <summary>Runs one check, then one more per batch of explicit requests (or a changed distribution) made meanwhile.</summary>
    private async Task RunSetupChecksAsync(string distro)
    {
        await RunSetupCheckAsync(distro);
        while (_setupPending)
        {
            _setupPending = false;
            if (!_setupEnabled) break;
            _setupRunning = true;
            _lastSetupCheck = SetupUtcNow();
            _lastSetupDistro = _cfg.WslDistro;
            await RunSetupCheckAsync(_cfg.WslDistro);
        }
    }

    /// <summary>The detector uses hidden processes off the UI thread; only its result is applied on the UI context.</summary>
    private async Task RunSetupCheckAsync(string distro)
    {
        Setup.IsChecking = true;
        try
        {
            var result = await DetectSetupAsync(distro);
            var codexConfigs = await Task.Run(ReadCodexConfigs);
            if (_cfg.WslDistro != distro) return;
            Setup.SetCodexConfigs(codexConfigs);
            Setup.Apply(result, distro);
            if (result.Cli("codex")?.Installed == true) _ = LoadCompactMetadataAsync(distro);
            SetupRowsChanged();
            var done = _installing.Where(c => result.Cli(c) is { Installed: true }).ToList();
            if (done.Count > 0)
            {
                _installing.ExceptWith(done);
                ShowStatus(Loc.Format("status.cliInstalled", string.Join(", ", done)));
            }
        }
        catch (Exception)
        {
            if (_cfg.WslDistro == distro) Setup.Apply(SetupStatus.Empty, distro);
        }
        finally
        {
            _setupRunning = false;
            Setup.IsChecking = false;
            // A distribution changed while a check was running: discard that result and check the current environment.
            if (_cfg.WslDistro != distro) _setupPending = true;
        }
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
        ShowLaunchOutcome(error, cfg.Tabs, () => InstallCli(cli), Loc.Format("status.cliInstallRunning", cli, Prerequisites.LoginCommand(cli)));
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
            ShowStatus(Loc.Get("status.wslInstallStarted"));
        }
        catch (Exception e)
        {
            // Includes the user's "No" in the UAC prompt.
            ShowError(ErrorKind.WslInstall, Loc.Format("status.wslInstallFailed", e.Message), InstallWsl);
        }
    }

    /// <summary>The ~/.bashrc of the tabs' Linux as this process can read it; null until the WSL home is known.</summary>
    internal string? PermissionConfigPath { get; set; }
    private string? BashrcFile => PermissionConfigPath ?? PathMapping.ToLocalPath("~/.bashrc", _cfg.WslDistro, _wslHome);

    /// <summary>Codex's home directory (~/.codex of the tabs' Linux) as this process can read it; null until the WSL home is known.</summary>
    internal string? CodexHomePath { get; set; }
    private string? CodexHome => CodexHomePath ?? PathMapping.ToLocalPath(CodexConfig.Home, _cfg.WslDistro, _wslHome);

    /// <summary>Maps an account's Linux directory to a path this process can read; tests replace it.</summary>
    internal Func<string, string?>? AccountHomePath { get; set; }

    /// <summary>The CODEX_HOME of the default account and of every Codex account (#186) that this process can reach.</summary>
    private List<string> CodexHomes()
    {
        var homes = new List<string>();
        if (CodexHome is { } home) homes.Add(home);
        foreach (var account in Accounts.For(_cfg, "codex").Where(a => Accounts.IsValidDirectory(a.Directory)))
            if ((AccountHomePath ?? (d => PathMapping.ToLocalPath(d, _cfg.WslDistro, _wslHome)))(account.Directory) is { } local && !homes.Contains(local))
                homes.Add(local);
        return homes;
    }

    /// <summary>What every account's config.toml says now; unreadable ones are left out.</summary>
    internal IReadOnlyList<CodexConfigState> ReadCodexConfigs() =>
        CodexHomes().Select(ReadCodexConfig).OfType<CodexConfigState>().ToList();

    /// <summary>What config.toml says now (#182); null when the path is unknown or the file cannot be read.</summary>
    internal CodexConfigState? ReadCodexConfig() => CodexHome is { } home ? ReadCodexConfig(home) : null;

    private static CodexConfigState? ReadCodexConfig(string home)
    {
        try
        {
            var path = Path.Combine(home, CodexConfig.FileName);
            return CodexConfig.State(File.Exists(path) ? File.ReadAllText(path) : null, File.Exists(Path.Combine(home, CodexConfig.BackupName)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// The flow behind choosing a mode in the list (#187): ask <paramref name="confirm"/> first, write only on yes, and put the
    /// list back on the effective mode on cancel or failure. Ignores a selection equal to the current mode (what a refresh or the
    /// revert sets) and any change while a confirmation is open, so nothing re-opens the dialog. <paramref name="repair"/> asks
    /// for the current mode again (fixing Codex's config.toml). Returns true when something was written.
    /// </summary>
    public async Task<bool> ChooseAgentModeAsync(AgentInfo agent, Func<AgentInfo, Task<bool>> confirm, bool repair = false)
    {
        if (agent.ModeChangePending || !agent.CanApply || (!repair && agent.SelectedMode.Mode == agent.CurrentMode)) return false;
        agent.ModeChangePending = true;
        var applied = false;
        try { applied = await confirm(agent) && ApplyAgentMode(agent, yoloConfirmed: true, replaceCustomConfirmed: true); }
        finally
        {
            if (!applied) agent.RevertSelection();
            agent.ModeChangePending = false;
        }
        return applied;
    }

    /// <summary>Called only after the mode confirmation; never changes a running CLI.</summary>
    public bool ApplyAgentMode(AgentInfo agent, bool yoloConfirmed, bool replaceCustomConfirmed = false)
    {
        if (!agent.CanApply || (agent.SelectedMode.Mode == AgentMode.Yolo && !yoloConfirmed)
            || (agent.HasExternalDefinition && !replaceCustomConfirmed)) return false;
        if (BashrcFile is not { } path) { ShowError(ErrorKind.AgentConfig, Loc.Get("status.wslHomeMissing")); return false; }
        try
        {
            var current = File.Exists(path) ? File.ReadAllText(path) : null;
            var updated = AgentModes.Change(current, agent.Detected, agent.SelectedMode, replaceCustomConfirmed);
            if (updated != (current ?? "")) File.WriteAllText(path, updated);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowError(ErrorKind.AgentConfig, Loc.Format("agents.mode.failed", e.Message));
            return false;
        }
        var applied = Loc.Format("agents.mode.applied", agent.Name, agent.SelectedMode.Label);
        if (agent.Cli == "codex")
        {
            try
            {
                if (ApplyCodexConfig(agent.SelectedMode.Mode) is { } key) applied += " " + Loc.Get(key);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ShowError(ErrorKind.AgentConfig, Loc.Format("agents.codexConfig.failed", e.Message));
                Setup.SetCodexConfigs(ReadCodexConfigs());
                CheckSetup();
                return false;
            }
            Setup.SetCodexConfigs(ReadCodexConfigs());
        }
        ShowStatus(applied);
        CheckSetup();
        return true;
    }

    /// <summary>
    /// Makes Codex's config.toml follow the alias (#182): YOLO backs the file up once and sets both keys; Standard and
    /// Auto Approve restore the backed-up values and remove the backup. Every account's CODEX_HOME follows (#186), since the alias
    /// applies to all of them. Returns the status key of what changed.
    /// </summary>
    private string? ApplyCodexConfig(AgentMode mode)
    {
        if (CodexHome is null) throw new IOException(Loc.Get("status.wslHomeMissing"));
        string? changed = null;
        foreach (var home in CodexHomes()) changed = ApplyCodexConfig(home, mode) ?? changed;
        return changed;
    }

    private static string? ApplyCodexConfig(string home, AgentMode mode)
    {
        var path = Path.Combine(home, CodexConfig.FileName);
        var backup = Path.Combine(home, CodexConfig.BackupName);
        var current = File.Exists(path) ? File.ReadAllText(path) : null;
        if (mode == AgentMode.Yolo)
        {
            System.IO.Directory.CreateDirectory(home);
            // The first backup holds the user's own values; applying YOLO again never replaces it.
            if (!File.Exists(backup))
            {
                if (current is null) File.WriteAllText(backup, "");
                else File.Copy(path, backup);
            }
            var updated = CodexConfig.ApplyYolo(current);
            if (updated == current) return null;
            // Written in place, so the file keeps its permissions.
            File.WriteAllText(path, updated);
            return "agents.codexConfig.applied";
        }
        if (!File.Exists(backup)) return null;
        var original = File.ReadAllText(backup);
        var restored = CodexConfig.Restore(current, original);
        // The file did not exist before YOLO and holds nothing else now: remove it again.
        if (original.Length == 0 && restored.Trim().Length == 0) { if (current is not null) File.Delete(path); }
        else if (restored != current) File.WriteAllText(path, restored);
        File.Delete(backup);
        return "agents.codexConfig.restored";
    }

    /// <summary>Neutral, deterministic versions and permission states; never reads an installed CLI or a user file.</summary>
    public void ShowSampleAgents(bool localClisMissing = false, bool codexConfigMismatch = false)
    {
        const string codexYolo = "alias codex='codex --dangerously-bypass-approvals-and-sandbox'";
        // --section codex-config: the YOLO alias while config.toml still keeps Codex's own sandbox (#182).
        Setup.SetCodexConfig(codexConfigMismatch ? new CodexConfigState("workspace-write", "on-request", false) : null);
        const string modern = "--approve-for-me --ask-for-approval --sandbox --dangerously-bypass-approvals-and-sandbox --oss --config";
        Setup.Apply(new SetupStatus(WslState.Ok, ["Ubuntu"],
        [
            new("claude", "alias", "/sample/bin/claude") { Version = "2.1.304", Help = "--autocompact acceptEdits --permission-mode --dangerously-skip-permissions", AliasDefinition = "alias claude='claude --permission-mode acceptEdits'", ManagedAlias = "alias claude='claude --permission-mode acceptEdits'" },
            codexConfigMismatch
                ? new("codex", "alias", "/sample/bin/codex") { Version = "0.170.0", Help = modern, AliasDefinition = codexYolo, ManagedAlias = codexYolo }
                : new("codex", "file", "/sample/bin/codex") { Version = "0.170.0", Help = modern },
            new("agy", "function", "/sample/bin/agy") { Version = "1.4.0", Help = "--mode auto --dangerously-skip-permissions", FunctionDefinition = "agy () {\n    command agy --mode=auto \"$@\";\n}" },
            new("aider", localClisMissing ? "none" : "file", localClisMissing ? null : "/sample/bin/aider") { Help = "--yes-always" },
            new("interpreter", localClisMissing ? "none" : "file", localClisMissing ? null : "/sample/bin/interpreter") { Version = "0.0.59", Help = modern },
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

    /// <summary>For --screenshot --fake-setup alias: installed CLIs without permission aliases (no setup offer).</summary>
    public void ShowSampleAlias()
    {
        Setup.Apply(new SetupStatus(WslState.Ok, ["Ubuntu"],
            [new("claude", "file", "/home/me/.local/bin/claude"), new("codex", "file", "/home/me/.nvm/bin/codex"), new("agy", "alias", "/home/me/.local/bin/agy")], true, false),
            _cfg.WslDistro);
        Setup.RowsChanged(Tabs.Select(t => t.Llm));
    }
}
