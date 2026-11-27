using Avalonia.Threading;

namespace BoosterAI;

public enum StartChoiceKind { None, Orkester, InstallOrkester, Header, Command, Missing }

/// <summary>
/// One entry of a row's start command list (#255): no command, /orkester (or its install offer), a group header
/// (Project, User, Plugin; not selectable) or a discovered command. <see cref="Missing"/> keeps a saved command that the
/// last discovery did not find visible and selected, as the Agent box does with a missing account.
/// </summary>
public sealed record StartChoice(StartChoiceKind Kind, string Command, string Label, string? Description = null)
{
    public bool IsSelectable => Kind != StartChoiceKind.Header;
    public bool IsHeader => Kind == StartChoiceKind.Header;
    public bool IsEntry => Kind != StartChoiceKind.Header;
    /// <summary>Commands are shown in the mono font, like the folder; the words (no command, install, headers) in the UI font.</summary>
    public bool IsCode => Kind is StartChoiceKind.Orkester or StartChoiceKind.Command or StartChoiceKind.Missing;
    public bool IsWords => !IsCode && !IsHeader;
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    /// <summary>The accessible name and tooltip: the label and, when there is one, the description.</summary>
    public string Name => HasDescription ? Label + " – " + Description : Label;
}

public sealed partial class TabViewModel
{
    /// <summary>The command chosen instead of /orkester (#255), as typed; "" for none.</summary>
    public string Command
    {
        get => _command;
        set { if (Set(ref _command, StartCommands.OneLine(value))) StartCommandChanged(); }
    }

    /// <summary>Optional arguments after the start command, one line.</summary>
    public string CommandArgs
    {
        get => _commandArgs;
        set
        {
            // Not trimmed while typing (a space before the next word must stay); ToConfig and the launch use the one-line form.
            var v = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            if (Set(ref _commandArgs, v)) RaiseCommandMarkers();
        }
    }

    /// <summary>The commands discovered for this row's agent, folder and account; empty until the first discovery answered.</summary>
    public IReadOnlyList<StartCommand> Commands => _owner.CommandsOf(this);

    /// <summary>A command the tab starts with: /orkester where the folder has the orchestra, or a chosen command.</summary>
    public bool HasStartCommand => IsAi && (_orkester && CanOrkester || !_orkester && _command.Length > 0);

    /// <summary>The list shows where there is something to choose: the orchestra, a found command or a saved one.</summary>
    public bool ShowStartCommands => IsAi && (CanOrkester || _command.Length > 0 || Commands.Count > 0);

    /// <summary>Today's install link (#152) where the list would only offer it: no orchestra here and no other command.</summary>
    public bool ShowInstallLink => CanInstallOrkester && !ShowStartCommands;

    /// <summary>Run once (#255) for the chosen command; shown with the arguments only while a command is chosen.</summary>
    public bool RunOnce
    {
        get => _nonInteractive && HasStartCommand;
        set => NonInteractive = value;
    }

    /// <summary>The row menu's Arguments entry (#260): the current one-line value is part of the label.</summary>
    public string ArgumentsMenu
    {
        get
        {
            var args = StartCommands.OneLine(_commandArgs);
            if (args.Length == 0) return Loc.Get("command.menu.args");
            return Loc.Format("command.menu.args.value", args.Length > 24 ? args[..23] + "…" : args);
        }
    }

    public string ArgumentsTip
    {
        get
        {
            var args = StartCommands.OneLine(_commandArgs);
            return Loc.Get("command.args.tip") + (args.Length > 0 ? "\n" + Loc.Format("command.args.current", args) : "");
        }
    }

    /// <summary>A small marker on the row (#260): "1×" when it runs once, "arg" when it has arguments.</summary>
    public string CommandBadge
    {
        get
        {
            var parts = new List<string>();
            if (RunOnce) parts.Add(Loc.Get("command.badge.once"));
            if (HasStartCommand && StartCommands.OneLine(_commandArgs).Length > 0) parts.Add(Loc.Get("command.badge.args"));
            return string.Join(" ", parts);
        }
    }

    public bool HasCommandBadge => CommandBadge.Length > 0;

    private void RaiseCommandMarkers()
    {
        foreach (var n in new[] { nameof(StartCommandTip), nameof(ArgumentsMenu), nameof(ArgumentsTip), nameof(CommandBadge), nameof(HasCommandBadge) }) Raise(n);
    }

    /// <summary>The closed box's tooltip: what the tab starts with, its arguments and Run once.</summary>
    public string StartCommandTip
    {
        get
        {
            var name = _orkester && CanOrkester ? StartCommands.Orkester : !_orkester && _command.Length > 0 ? _command : null;
            if (name is null) return Loc.Get("command.tip");
            var args = StartCommands.OneLine(_commandArgs);
            var text = args.Length > 0 ? name + " " + args : name;
            var found = Commands.FirstOrDefault(c => c.Name == name)?.Description;
            return Loc.Format(RunOnce ? "command.tip.once" : "command.tip.chosen", text) + (found is null ? "" : "\n" + found);
        }
    }

    /// <summary>
    /// The row's entries: no command; /orkester (or, on a project whose folder lacks it, the install offer); then the found
    /// commands grouped under Project, User and Plugin; a saved command that was not found last, as its own entry.
    /// </summary>
    public IReadOnlyList<StartChoice> StartChoices
    {
        get
        {
            var list = new List<StartChoice> { new(StartChoiceKind.None, "", Loc.Get("command.none")) };
            if (CanOrkester || _orkester && IsAi && _hasOrkester is null)
                list.Add(new(StartChoiceKind.Orkester, StartCommands.Orkester, StartCommands.Orkester, Loc.Get("command.orkester.description")));
            else if (CanInstallOrkester)
                list.Add(new(StartChoiceKind.InstallOrkester, StartCommands.Orkester, Loc.Get("command.install"), Loc.Get("install.tip")));
            var commands = Commands;
            foreach (var group in commands.GroupBy(c => c.Source).OrderBy(g => g.Key))
            {
                list.Add(new(StartChoiceKind.Header, "", Loc.Get("command.source." + group.Key.ToString().ToLowerInvariant())));
                list.AddRange(group.Select(c => new StartChoice(StartChoiceKind.Command, c.Name, c.Name, c.Description)));
            }
            if (_command.Length > 0 && commands.All(c => c.Name != _command))
                list.Add(new(StartChoiceKind.Missing, _command, _command, Loc.Get(_owner.CommandsKnown(this) ? "command.missing" : "command.unchecked")));
            return list;
        }
    }

    /// <summary>The entry for the saved choice; choosing one sets <see cref="Orkester"/> and <see cref="Command"/>.</summary>
    public StartChoice? SelectedStartChoice
    {
        get
        {
            var choices = StartChoices;
            if (_orkester) return choices.FirstOrDefault(c => c.Kind == StartChoiceKind.Orkester) ?? choices[0];
            return _command.Length > 0 ? choices.FirstOrDefault(c => c.Kind is StartChoiceKind.Command or StartChoiceKind.Missing && c.Command == _command) ?? choices[0]
                : choices[0];
        }
        set
        {
            if (value is null) return;
            switch (value.Kind)
            {
                case StartChoiceKind.None:
                    Orkester = false;
                    Command = "";
                    break;
                case StartChoiceKind.Orkester:
                    Command = "";
                    Orkester = true;
                    break;
                case StartChoiceKind.Command or StartChoiceKind.Missing:
                    Orkester = false;
                    Command = value.Command;
                    break;
                case StartChoiceKind.InstallOrkester:
                    // The entry opens the same confirmation as the install link; the choice itself stays as it was.
                    _owner.RequestInstallOrkester(this);
                    break;
            }
            // A header or the install entry leaves the choice; show the saved one again after the selection commits.
            Dispatcher.UIThread.Post(() => Raise(nameof(SelectedStartChoice)));
        }
    }

    /// <summary>The choice, the folder's orchestra or the agent changed: everything the column shows follows.</summary>
    internal void StartCommandChanged()
    {
        foreach (var n in new[] { nameof(Orkester), nameof(Command), nameof(NonInteractive), nameof(RunOnce), nameof(HasStartCommand),
                     nameof(ShowStartCommands), nameof(ShowInstallLink), nameof(StartCommandTip),
                     nameof(ArgumentsMenu), nameof(ArgumentsTip), nameof(CommandBadge), nameof(HasCommandBadge) }) Raise(n);
        // Replacing the box's items while it commits a selection re-enters it (see Llm); refresh them afterwards.
        Dispatcher.UIThread.Post(() => { Raise(nameof(StartChoices)); Raise(nameof(SelectedStartChoice)); });
        RaiseRestart();
        _owner.OrchestraColumnChanged();
    }

    /// <summary>A discovery for this row's agent and folder answered.</summary>
    internal void CommandsChanged()
    {
        Raise(nameof(Commands));
        Raise(nameof(StartChoices));
        Raise(nameof(SelectedStartChoice));
        foreach (var n in new[] { nameof(ShowStartCommands), nameof(ShowInstallLink), nameof(StartCommandTip), nameof(CommandBadge), nameof(HasCommandBadge) }) Raise(n);
        _owner.OrchestraColumnChanged();
    }
}

public sealed partial class MainViewModel
{
    private readonly Dictionary<StartCommandRequest, IReadOnlyList<StartCommand>> _commands = [];
    private readonly List<StartCommandRequest> _commandQueue = [];
    private bool _commandsEnabled, _commandsRunning;
    private DispatcherTimer? _commandTimer;
    private DateTime _lastCommandRefresh = DateTime.MinValue;

    /// <summary>Runs one discovery; tests replace it (no shell runs in tests or screenshots).</summary>
    internal Func<string, IReadOnlyList<StartCommandRequest>, Task<IReadOnlyList<IReadOnlyList<StartCommand>?>>> DiscoverCommandsAsync { get; set; } =
        (distro, requests) => Task.Run(() => StartCommands.Discover(distro, requests));

    /// <summary>The last discovery, for tests.</summary>
    internal Task CommandsTask { get; private set; } = Task.CompletedTask;

    /// <summary>Asks the window to offer the orchestra install for <paramref name="tab"/> (the list's install entry, #255).</summary>
    public event Action<TabViewModel>? InstallOrkesterRequested;
    internal void RequestInstallOrkester(TabViewModel tab) => InstallOrkesterRequested?.Invoke(tab);

    /// <summary>What to discover for the row; null for an agent without command files.</summary>
    internal StartCommandRequest? CommandRequest(TabViewModel tab)
    {
        if (!StartCommands.Supports(tab.Llm)) return null;
        var dir = tab.Directory.Trim();
        if (OperatingSystem.IsWindows()) dir = PathMapping.ToWslPath(dir);
        return new StartCommandRequest(tab.Llm, dir.Length == 0 ? "~" : dir, AccountDirectory(tab.Llm, tab.Account));
    }

    public IReadOnlyList<StartCommand> CommandsOf(TabViewModel tab) =>
        CommandRequest(tab) is { } r && _commands.TryGetValue(r, out var list) ? list : [];

    /// <summary>Whether a discovery answered for the row, so a saved command that is not in the list is really missing.</summary>
    internal bool CommandsKnown(TabViewModel tab) => CommandRequest(tab) is not { } r || _commands.ContainsKey(r);

    /// <summary>Starts command discovery; only the real app calls it (with the setup checks), tests opt in.</summary>
    internal void EnableCommandDiscovery()
    {
        _commandsEnabled = true;
        RefreshCommands(force: true);
    }

    /// <summary>Discovers again for every row (window activation), at most every 30 seconds unless forced.</summary>
    public void RefreshCommands(bool force = false)
    {
        if (!_commandsEnabled || !force && DateTime.UtcNow - _lastCommandRefresh < TimeSpan.FromSeconds(30)) return;
        _lastCommandRefresh = DateTime.UtcNow;
        foreach (var t in Tabs.ToArray()) CheckCommands(t);
    }

    /// <summary>
    /// Queues a discovery for the row's agent, folder and account. Requests made within a moment (typing a folder, the
    /// first load) are batched into one hidden bash.
    /// </summary>
    public void CheckCommands(TabViewModel tab)
    {
        if (!_commandsEnabled || CommandRequest(tab) is not { } request) return;
        if (!_commandQueue.Contains(request)) _commandQueue.Add(request);
        _commandTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) =>
        {
            _commandTimer!.Stop();
            CommandsTask = FlushCommandsAsync();
        });
        _commandTimer.Stop();
        _commandTimer.Start();
    }

    /// <summary>Runs the queued requests now; a run in progress takes the rest when it ends.</summary>
    internal async Task FlushCommandsAsync()
    {
        if (_commandsRunning || _commandQueue.Count == 0) return;
        _commandsRunning = true;
        try
        {
            while (_commandQueue.Count > 0)
            {
                var batch = _commandQueue.ToArray();
                _commandQueue.Clear();
                IReadOnlyList<IReadOnlyList<StartCommand>?> results;
                try { results = await DiscoverCommandsAsync(_cfg.WslDistro, batch); }
                catch (Exception) { results = []; }
                var changed = new HashSet<StartCommandRequest>();
                for (var i = 0; i < batch.Length && i < results.Count; i++)
                {
                    if (results[i] is not { } found) continue;
                    if (_commands.TryGetValue(batch[i], out var old) && old.SequenceEqual(found)) continue;
                    _commands[batch[i]] = found;
                    changed.Add(batch[i]);
                }
                foreach (var t in Tabs.ToArray())
                    if (CommandRequest(t) is { } r && changed.Contains(r)) t.CommandsChanged();
            }
        }
        finally { _commandsRunning = false; }
    }

    /// <summary>For --screenshot: invented commands for every row instead of a discovery (nothing is read).</summary>
    internal void ShowSampleCommands()
    {
        foreach (var t in Tabs)
        {
            t.HasOrkester ??= true;
            if (CommandRequest(t) is { } r) _commands[r] = StartCommands.Samples(t.Llm);
            t.CommandsChanged();
        }
    }
}
