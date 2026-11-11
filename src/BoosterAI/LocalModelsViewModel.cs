namespace BoosterAI;

public sealed class DownloadableModelRow : Observable
{
    internal DownloadableModel Model { get; }
    private PlanningFit _fit;
    private bool _installed;
    internal DownloadableModelRow(DownloadableModel model) => Model = model;
    public string Id => Model.Id;
    public string Size => Loc.Format("models.downloadSize", (Model.DownloadBytes / 1_000_000_000.0).ToString("0.0##"));
    public string Purpose => Loc.Get("models.purpose." + Model.Purpose);
    public string Fit => Loc.Get("models.fit." + _fit);
    public bool Installed { get => _installed; private set => Set(ref _installed, value); }
    // Size, planning fit and (only when confirmed) installation in one caption line.
    public string Caption => string.Join(" · ", new[] { Size, Fit, Installed ? Loc.Get("models.installedShort") : null }.OfType<string>());
    internal void Refresh(HardwareSnapshot hardware, IReadOnlyList<OllamaModel> models)
    {
        _fit = DownloadableModels.Assess(Model, hardware);
        Installed = models.Any(m => m.Name == Id && !m.Remote && m.Bytes > 0);
        Raise(nameof(Fit)); Raise(nameof(Caption));
    }
}

/// <summary>Transient local-model management. Never writes the agent's model or endpoint configuration.</summary>
public sealed class LocalModelsViewModel : Observable, IDisposable
{
    private readonly SettingsHardwareAdviceState _hardware;
    private readonly Func<Uri, CancellationToken, Task<OllamaSnapshot>> _readModels;
    private readonly Func<Uri, DownloadableModel, Action<ModelDownloadProgress>, CancellationToken, Task> _pull;
    private DownloadableModelRow _selected;
    private IReadOnlyList<OllamaModel> _installed = [];
    private string _endpoint = "http://127.0.0.1:11434";
    private string _status = Loc.Get("models.serverUnchecked"), _downloadStatus = "";
    private bool _busy, _connected, _serverFailed, _downloading, _confirmed, _disposed;
    private double _percent;
    private CancellationTokenSource? _operation;
    public IReadOnlyList<DownloadableModelRow> Models { get; } = DownloadableModels.All.Select(m => new DownloadableModelRow(m)).ToArray();
    public LocalModelsViewModel() : this(new SettingsHardwareAdviceState()) { }
    internal LocalModelsViewModel(SettingsHardwareAdviceState hardware,
        Func<Uri, CancellationToken, Task<OllamaSnapshot>>? readModels = null,
        Func<Uri, DownloadableModel, Action<ModelDownloadProgress>, CancellationToken, Task>? pull = null)
    {
        _hardware = hardware; _readModels = readModels ?? LocalOllamaProbe.ReadAsync;
        _pull = pull ?? OllamaModelDownload.PullAsync;
        _selected = Models[1];
        hardware.PropertyChanged += OnHardwareChanged;
        Refresh();
    }
    public DownloadableModelRow Selected
    {
        get => _selected;
        set { if (!Busy && value is not null && Models.Contains(value) && Set(ref _selected, value)) { DownloadStatus = ""; Refresh(); } }
    }
    public string Endpoint
    {
        get => _endpoint;
        set
        {
            if (Busy || !Set(ref _endpoint, value?.Trim() ?? "")) return;
            _connected = false; _serverFailed = false; _installed = []; _confirmed = false;
            Status = Loc.Get("models.serverUnchecked"); DownloadStatus = ""; Raise(nameof(Confirmed)); Refresh();
        }
    }
    public bool Confirmed { get => _confirmed; set { if (!Busy && Set(ref _confirmed, value)) Refresh(); } }
    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) Refresh(); } }
    public bool CanEdit => !Busy && !_disposed;
    public bool CanDownload => !_disposed && !Busy && _connected && Confirmed && !Selected.Installed;
    public bool CanCancel => Busy && !_disposed;
    public string Status { get => _status; private set => Set(ref _status, value); }
    // Step 1's ✓/✗: the server answered, or the last check failed. Neither while unchecked or checking.
    public bool ServerOk => _connected;
    public bool ServerFailed => !_connected && _serverFailed;
    public string DownloadStatus { get => _downloadStatus; private set => Set(ref _downloadStatus, value); }
    public double Percent { get => _percent; private set => Set(ref _percent, value); }
    public string Hardware => _hardware.HasReading ? LocalModelViewModel.HardwareText(_hardware.Snapshot) : Loc.Get("localAdvice.notMeasured");
    private DownloadableModel? Recommended => _hardware.HasReading && _hardware.Snapshot.AvailableRam is not null ? DownloadableModels.Recommend(_hardware.Snapshot) : null;
    /// <summary>The suggested exact tag for the callout (mono), or empty when there is none.</summary>
    public string RecommendedId => Recommended?.Id ?? "";
    public bool HasRecommendation => Recommended is not null;
    public string Recommendation
    {
        get
        {
            if (!_hardware.HasReading || _hardware.Snapshot.AvailableRam is null) return Loc.Get("models.recommendationUnknown");
            var model = Recommended;
            return model is null ? Loc.Get("models.recommendationNone")
                : Loc.Get("models.recommendation." + DownloadableModels.Assess(model, _hardware.Snapshot));
        }
    }
    public string SourceDate => Loc.Format("models.sourceDate", DownloadableModels.Reviewed);
    public string SelectedPlan => Loc.Format("models.plan", LocalModelViewModel.Memory(Selected.Model.PlanningBytes));
    /// <summary>Why <see cref="CanDownload"/> is false, shown beside the inactive button; empty exactly when it is true.</summary>
    public string DownloadBlockedReason => CanDownload ? ""
        : _disposed ? Loc.Get("models.blocked.closed")
        : Busy ? Loc.Get(_downloading ? "models.blocked.downloading" : "models.blocked.checking")
        : !_connected ? Loc.Get("models.blocked.server")
        : Selected.Installed ? Loc.Get("models.blocked.installed")
        : Loc.Get("models.blocked.consent");
    public string DownloadTarget => Loc.Format("models.target", Selected.Id, Selected.Size, Endpoint);
    public string Compatibility => Loc.Get(Selected.Model.OpenHandsExample ? "models.compatibility.openhands" : "models.compatibility.general");
    public string InstalledModels => !_connected ? Loc.Get("models.installedUnknown") : _installed.Count == 0 ? Loc.Get("models.installedEmpty")
        : string.Join("\n", _installed.Select(m => m.Name + (m.Remote ? " · " + Loc.Get("models.cloud") : " · " + LocalModelViewModel.Memory(m.Bytes))));

    public async Task RefreshAsync(CancellationToken cancellation)
    {
        if (Busy || _disposed) return;
        Busy = true;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        _operation = operation;
        try
        {
            _connected = false; _serverFailed = false; _installed = [];
            await _hardware.EnsureAsync(operation.Token);
            if (LocalOllamaProbe.Endpoint(Endpoint) is not { } uri) { _serverFailed = true; Status = Loc.Get("localAdvice.invalidEndpoint"); return; }
            Status = Loc.Get("models.connecting");
            _connected = false;
            var snapshot = await _readModels(uri, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            _installed = snapshot.Models;
            _connected = true; Status = Loc.Get("models.connected");
        }
        catch (OperationCanceledException)
        {
            var cancelled = cancellation.IsCancellationRequested || operation.IsCancellationRequested;
            _serverFailed = !cancelled;
            Status = Loc.Get(cancelled ? "models.checkCancelled" : "models.unreachable");
        }
        catch (Exception ex) when (IsReadFailure(ex)) { _serverFailed = true; Status = Loc.Get("models.unreachable"); }
        finally { _operation = null; Busy = false; }
    }

    public async Task DownloadAsync(CancellationToken cancellation)
    {
        if (!CanDownload || LocalOllamaProbe.Endpoint(Endpoint) is not { } endpoint) return;
        var model = Selected.Model;
        _downloading = true; Busy = true; Percent = 0; DownloadStatus = Loc.Get("models.downloading");
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        _operation = operation;
        try
        {
            await _pull(endpoint, model, p =>
            {
                Percent = p.Percent;
                DownloadStatus = p.Total > 0 ? Loc.Format("models.progress", p.Percent.ToString("0"), LocalModelViewModel.Memory(p.Completed), LocalModelViewModel.Memory(p.Total))
                    : Loc.Get("models.downloading");
            }, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            // The pull succeeded even if refreshing the inventory later fails.
            DownloadStatus = Loc.Format("models.downloaded", model.Id);
            try
            {
                _installed = (await _readModels(endpoint, operation.Token)).Models;
                operation.Token.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (IsReadFailure(ex) || ex is OperationCanceledException)
            { _connected = false; Status = Loc.Get("models.refreshAfterDownload"); }
        }
        catch (OperationCanceledException)
        { DownloadStatus = Loc.Get(operation.IsCancellationRequested ? "models.downloadCancelled" : "models.downloadFailed"); }
        catch (Exception ex) when (IsReadFailure(ex)) { DownloadStatus = Loc.Get("models.downloadFailed"); }
        finally { _operation = null; _downloading = false; Busy = false; }
    }
    private static bool IsReadFailure(Exception ex) => ex is HttpRequestException or IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException;
    public void Cancel() => _operation?.Cancel();
    private void OnHardwareChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hardware.PropertyChanged -= OnHardwareChanged;
        Cancel();
    }
    private void Refresh()
    {
        if (_disposed) return;
        foreach (var row in Models) row.Refresh(_hardware.Snapshot, _installed);
        foreach (var name in new[] { nameof(CanEdit), nameof(CanDownload), nameof(CanCancel), nameof(Hardware), nameof(Recommendation), nameof(RecommendedId), nameof(HasRecommendation), nameof(SelectedPlan), nameof(DownloadTarget), nameof(DownloadBlockedReason), nameof(Compatibility), nameof(InstalledModels), nameof(ServerOk), nameof(ServerFailed) }) Raise(name);
    }
    internal void ShowSample(string? section)
    {
        const long gb = LocalModelAdvice.GiB;
        _hardware.SetSnapshot(section == "unknown" ? HardwareSnapshot.Empty : new(32 * gb, 20 * gb, "Example CPU", 12,
            section == "cpu" ? [] : [new("Example GPU", 12 * gb, 10 * gb)]));
        _connected = section != "missing";
        _serverFailed = !_connected;
        _confirmed = _connected;
        _installed = _connected ? [new(Models[1].Id, "sample-digest", Models[1].Model.DownloadBytes, false)] : [];
        Status = Loc.Get(_connected ? "models.connected" : "models.unreachable");
        if (section == "download")
        {
            // A download of a model that is not installed yet: the recommended GPU example.
            _selected = Models[3]; _busy = _downloading = true; Percent = 42;
            DownloadStatus = Loc.Format("models.progress", "42", LocalModelViewModel.Memory(Models[3].Model.DownloadBytes * 42 / 100), LocalModelViewModel.Memory(Models[3].Model.DownloadBytes));
        }
        Refresh(); Raise(nameof(Selected)); Raise(nameof(Confirmed)); Raise(nameof(Busy));
    }
}
