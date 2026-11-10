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
    public string InstalledText => Loc.Get(Installed ? "models.installed" : "models.notInstalled");
    internal void Refresh(HardwareSnapshot hardware, IReadOnlyList<OllamaModel> models)
    {
        _fit = DownloadableModels.Assess(Model, hardware);
        Installed = models.Any(m => m.Name == Id && !m.Remote && m.Bytes > 0);
        Raise(nameof(Fit)); Raise(nameof(InstalledText));
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
    private bool _busy, _connected, _confirmed, _disposed;
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
            _connected = false; _installed = []; _confirmed = false;
            Status = Loc.Get("models.serverUnchecked"); DownloadStatus = ""; Raise(nameof(Confirmed)); Refresh();
        }
    }
    public bool Confirmed { get => _confirmed; set { if (!Busy && Set(ref _confirmed, value)) Refresh(); } }
    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) Refresh(); } }
    public bool CanEdit => !Busy && !_disposed;
    public bool CanDownload => !_disposed && !Busy && _connected && Confirmed && !Selected.Installed;
    public bool CanCancel => Busy && !_disposed;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string DownloadStatus { get => _downloadStatus; private set => Set(ref _downloadStatus, value); }
    public double Percent { get => _percent; private set => Set(ref _percent, value); }
    public string Hardware => _hardware.HasReading ? LocalModelViewModel.HardwareText(_hardware.Snapshot) : Loc.Get("localAdvice.notMeasured");
    public string Recommendation
    {
        get
        {
            if (!_hardware.HasReading || _hardware.Snapshot.AvailableRam is null) return Loc.Get("models.recommendationUnknown");
            var model = DownloadableModels.Recommend(_hardware.Snapshot);
            return model is null ? Loc.Get("models.recommendationNone")
                : Loc.Format("models.recommendation." + DownloadableModels.Assess(model, _hardware.Snapshot), model.Id);
        }
    }
    public string SourceDate => Loc.Format("models.sourceDate", DownloadableModels.Reviewed);
    public string SelectedPlan => Loc.Format("models.plan", LocalModelViewModel.Memory(Selected.Model.PlanningBytes));
    public string DownloadHint => _connected && Selected.Installed ? Loc.Get("models.installed")
        : !_connected ? Loc.Get("models.needServer") : !Confirmed ? Loc.Get("models.needConsent") : "";
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
            _connected = false; _installed = [];
            await _hardware.EnsureAsync(operation.Token);
            if (LocalOllamaProbe.Endpoint(Endpoint) is not { } uri) { Status = Loc.Get("localAdvice.invalidEndpoint"); return; }
            Status = Loc.Get("models.connecting");
            _connected = false;
            var snapshot = await _readModels(uri, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            _installed = snapshot.Models;
            _connected = true; Status = Loc.Get("models.connected");
        }
        catch (OperationCanceledException) { Status = Loc.Get(cancellation.IsCancellationRequested || operation.IsCancellationRequested ? "models.checkCancelled" : "models.unreachable"); }
        catch (Exception ex) when (IsReadFailure(ex)) { Status = Loc.Get("models.unreachable"); }
        finally { _operation = null; Busy = false; }
    }

    public async Task DownloadAsync(CancellationToken cancellation)
    {
        if (!CanDownload || LocalOllamaProbe.Endpoint(Endpoint) is not { } endpoint) return;
        var model = Selected.Model;
        Busy = true; Percent = 0; DownloadStatus = Loc.Get("models.downloading");
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
        finally { _operation = null; Busy = false; }
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
        foreach (var name in new[] { nameof(CanEdit), nameof(CanDownload), nameof(CanCancel), nameof(Hardware), nameof(Recommendation), nameof(SelectedPlan), nameof(DownloadTarget), nameof(DownloadHint), nameof(Compatibility), nameof(InstalledModels) }) Raise(name);
    }
    internal void ShowSample(string? section)
    {
        const long gb = LocalModelAdvice.GiB;
        _hardware.SetSnapshot(section == "unknown" ? HardwareSnapshot.Empty : new(32 * gb, 20 * gb, "Example CPU", 12,
            section == "cpu" ? [] : [new("Example GPU", 12 * gb, 10 * gb)]));
        _connected = section != "missing";
        _confirmed = _connected;
        _installed = _connected ? [new(Models[1].Id, "sample-digest", Models[1].Model.DownloadBytes, false)] : [];
        Status = Loc.Get(_connected ? "models.connected" : "models.unreachable");
        if (section == "download") { DownloadStatus = Loc.Format("models.progress", "42", "1.4 GiB", "3.3 GiB"); _busy = true; Percent = 42; }
        Refresh(); Raise(nameof(Confirmed)); Raise(nameof(Busy));
    }
}
