namespace BoosterAI;

/// <summary>
/// The autoMemoryReclaim choice beside the memory cap (#183): shows what .wslconfig says, recommends gradual and
/// writes, switches or removes the key only after an explicit confirmation. WSL is never restarted from here.
/// </summary>
public sealed class MemoryReclaimViewModel : Observable
{
    private MemoryReclaimSetting _setting = new(MemoryReclaimMode.NotSet, null);
    private Version? _version;
    private bool _versionKnown;
    private bool _checking;
    private string? _statusMessage;
    private bool _statusIsError;
    private bool _fake;

    /// <summary>Hidden <c>wsl --version</c> by default; tests replace it.</summary>
    internal Func<Version?> DetectVersion { get; set; } = WslMemoryReclaim.DetectWslVersion;

    public MemoryReclaimSetting Setting => _setting;
    public MemoryReclaimMode Mode => _setting.Mode;

    public string CurrentText => _setting.Mode switch
    {
        MemoryReclaimMode.NotSet => Loc.Get("settings.optimization.reclaim.notSet"),
        MemoryReclaimMode.Disabled => Loc.Get("settings.optimization.reclaim.disabled"),
        MemoryReclaimMode.Gradual => Loc.Get("settings.optimization.reclaim.gradual"),
        MemoryReclaimMode.DropCache => Loc.Get("settings.optimization.reclaim.dropCache"),
        _ => Loc.Format("settings.optimization.reclaim.other", _setting.Raw ?? ""),
    };

    public bool VersionSupported => WslMemoryReclaim.Supports(_version);

    /// <summary>Shown once the check has finished; nothing (not a warning) while it runs the first time.</summary>
    public string VersionText => !_versionKnown
        ? (_checking ? Loc.Get("settings.optimization.reclaim.checking") : "")
        : _version is null
            ? Loc.Format("settings.optimization.reclaim.versionUnknown", Min)
            : VersionSupported
                ? Loc.Format("settings.optimization.reclaim.versionOk", _version.ToString(3))
                : Loc.Format("settings.optimization.reclaim.versionOld", _version.ToString(3), Min);
    public bool HasVersionText => VersionText.Length > 0;
    public bool VersionBlocks => _versionKnown && !VersionSupported;
    private static string Min => WslMemoryReclaim.MinimumWslVersion.ToString(3);

    public bool CanSetGradual => VersionSupported && _setting.Mode != MemoryReclaimMode.Gradual;
    public bool CanSetDropCache => VersionSupported && _setting.Mode != MemoryReclaimMode.DropCache;
    /// <summary>Removing is allowed whatever the WSL version: it only takes a line out.</summary>
    public bool CanRemove => _setting.Mode != MemoryReclaimMode.NotSet;

    public string? StatusMessage => _statusMessage;
    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);
    public bool StatusIsError => _statusIsError;

    public void Refresh()
    {
        if (_fake) return;
        _setting = WslMemoryReclaim.Load();
        RaiseState();
    }

    /// <summary>Checks the installed WSL version off the UI thread; a running check is not started twice.</summary>
    public async Task CheckVersionAsync()
    {
        if (_checking || _fake) return;
        _checking = true;
        RaiseVersion();
        Version? version;
        try { version = await Task.Run(DetectVersion); }
        catch (Exception) { version = null; }
        // Awaited from the UI thread, this continues there (Avalonia's synchronization context).
        SetVersion(version);
    }

    internal void SetVersion(Version? version)
    {
        _checking = false;
        _version = version;
        _versionKnown = true;
        RaiseVersion();
    }

    /// <summary>The value an edit writes: null removes the key.</summary>
    public static string? ValueFor(string? tag) => tag switch
    {
        "gradual" => WslMemoryReclaim.Gradual,
        "dropCache" => WslMemoryReclaim.DropCache,
        _ => null,
    };

    /// <summary>The exact lines the edit removes and adds in .wslconfig, for the confirmation.</summary>
    public string Preview(string? value)
    {
        var content = _fake ? FakeContent() : WslMemoryReclaim.Read().Content;
        return WslMemoryReclaim.Preview(content, WslMemoryReclaim.Apply(content, value));
    }

    /// <summary>False with a message when the WSL version cannot use the key; nothing is asked or written then.</summary>
    public bool CanWrite(string? value)
    {
        if (value is null || VersionSupported) return true;
        Status(_versionKnown ? VersionText : Loc.Get("settings.optimization.reclaim.checking"), error: true);
        return false;
    }

    /// <summary>Writes after confirmation only; the WSL version gate is checked again here.</summary>
    public void Write(string? value, bool confirmed)
    {
        if (!confirmed || !CanWrite(value)) return;
        try
        {
            WslMemoryReclaim.Write(value);
            _fake = false;
            Refresh();
            Status(Loc.Get(value is null ? "settings.optimization.reclaim.removed" : "settings.optimization.reclaim.saved"), error: false);
        }
        catch (Exception ex)
        {
            Status(Loc.Format("settings.optimization.saveFailed", ex.Message), error: true);
        }
    }

    private void Status(string message, bool error)
    {
        _statusMessage = message;
        _statusIsError = error;
        Raise(nameof(StatusMessage));
        Raise(nameof(HasStatusMessage));
        Raise(nameof(StatusIsError));
    }

    /// <summary>Screenshots only: a neutral sample state, no file or process is read.</summary>
    internal void ShowFake(MemoryReclaimSetting setting, Version? version)
    {
        _fake = true;
        _setting = setting;
        RaiseState();
        SetVersion(version);
    }

    private string FakeContent() => _setting.Raw is { } raw
        ? $"[wsl2]\r\nmemory=24GB\r\n\r\n[experimental]\r\n{WslMemoryReclaim.Key}={raw}\r\n"
        : "[wsl2]\r\nmemory=24GB\r\n";

    private void RaiseState()
    {
        foreach (var p in new[] { nameof(Setting), nameof(Mode), nameof(CurrentText), nameof(CanSetGradual), nameof(CanSetDropCache), nameof(CanRemove) })
            Raise(p);
    }

    private void RaiseVersion()
    {
        foreach (var p in new[] { nameof(VersionSupported), nameof(VersionText), nameof(HasVersionText), nameof(VersionBlocks),
            nameof(CanSetGradual), nameof(CanSetDropCache) }) Raise(p);
    }
}
