namespace BoosterAI;

/// <summary>An ephemeral assessment for a selected tab. It never changes the CLI's model, endpoint or environment.</summary>
internal sealed class LocalModelViewModel : Observable
{
    private readonly ModelSelection _selection;
    private string _model, _endpoint = "http://127.0.0.1:11434";
    private bool _confirmed, _cpuOnly, _busy;
    private string _result, _hardware = Loc.Get("localAdvice.notMeasured"), _metadata = "";
    public LocalModelViewModel(string cli, string model)
    {
        _selection = ModelSelection.From(cli, model);
        _model = _selection.Model;
        Selection = Loc.Format("localAdvice.selection", Loc.Display("llm", cli), model);
        _result = Loc.Get("localAdvice.selection." + _selection.Backend);
    }
    public string Selection { get; }
    public string Model { get => _model; set { if (Set(ref _model, value?.Trim() ?? "")) Invalidate(); } }
    public string Endpoint { get => _endpoint; set { if (Set(ref _endpoint, value?.Trim() ?? "")) Invalidate(); } }
    public bool Confirmed { get => _confirmed; set { if (Set(ref _confirmed, value)) Invalidate(); } }
    public bool CpuOnly { get => _cpuOnly; set { if (Set(ref _cpuOnly, value)) Invalidate(); } }
    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) Raise(nameof(CanCheck)); } }
    public bool CanCheck => !Busy;
    public string Result { get => _result; private set => Set(ref _result, value); }
    public string Hardware { get => _hardware; private set => Set(ref _hardware, value); }
    public string Metadata { get => _metadata; private set => Set(ref _metadata, value); }
    private void Invalidate() { Result = Loc.Get("localAdvice.selection." + _selection.Backend); Metadata = ""; }

    public async Task CheckAsync(CancellationToken cancellation)
    {
        if (Busy) return;
        Busy = true;
        // Capture the evidence inputs. Edits during the asynchronous read invalidate the old result.
        var (model, endpoint, confirmed, cpuOnly) = (Model, Endpoint, Confirmed, CpuOnly);
        try
        {
            var hardware = await LocalHardwareProbe.ReadAsync(cancellation);
            if (cancellation.IsCancellationRequested) return;
            Hardware = HardwareText(hardware);
            if (!confirmed) { Result = Loc.Get("localAdvice.confirmRequired"); return; }
            if (LocalOllamaProbe.Endpoint(endpoint) is not { } uri) { Result = Loc.Get("localAdvice.invalidEndpoint"); return; }
            if (model.Length == 0 || !Catalog.IsSafeModel(model)) { Result = Loc.Get("localAdvice.exactModelRequired"); return; }
            Result = Loc.Get("localAdvice.checking");
            var snapshot = await LocalOllamaProbe.ReadAsync(uri, cancellation);
            if ((model, endpoint, confirmed, cpuOnly) != (Model, Endpoint, Confirmed, CpuOnly) || cancellation.IsCancellationRequested) return;
            Show(hardware, snapshot, model, cpuOnly);
        }
        catch (OperationCanceledException) { if (!cancellation.IsCancellationRequested) Result = Loc.Get("localAdvice.failed"); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        { Result = Loc.Get("localAdvice.failed"); }
        finally { Busy = false; }
    }

    private void Show(HardwareSnapshot hardware, OllamaSnapshot snapshot, string model, bool cpuOnly)
    {
        var installed = LocalModelAdvice.Find(model, snapshot.Models);
        var running = installed is null ? null : snapshot.Running.FirstOrDefault(r => r.Name == installed.Name && r.Digest == installed.Digest);
        var assessment = LocalModelAdvice.Assess(installed, running, hardware, cpuOnly);
        Result = Loc.Get("localAdvice.verdict." + assessment.Suitability) + "\n" + Loc.Get("localAdvice.reason." + assessment.Reason);
        if (installed is { Remote: false } && assessment.Suitability == ModelSuitability.Unknown)
            foreach (var gpu in hardware.Gpus.Where(g => g.TotalBytes < installed.Bytes))
                Result += "\n" + Loc.Format("localAdvice.gpuSmall", gpu.Name, Memory(gpu.TotalBytes), Memory(installed.Bytes));
        Metadata = installed is null ? "" : installed.Remote ? Loc.Format("localAdvice.remoteMetadata", installed.Name) : Loc.Format("localAdvice.metadata", installed.Name, Memory(installed.Bytes))
            + (snapshot.RuntimeKnown ? "" : "\n" + Loc.Get("localAdvice.runtimeUnknown"));
    }

    internal static string Memory(long? bytes) => bytes.HasValue ? $"{bytes.Value / (double)LocalModelAdvice.GiB:0.0} GiB" : Loc.Get("localAdvice.unknown");
    private static string HardwareText(HardwareSnapshot hardware) =>
        Loc.Format("localAdvice.ram", Memory(hardware.TotalRam), Memory(hardware.AvailableRam)) + "\n"
        + Loc.Format("localAdvice.cpu", hardware.Cpu.Length > 0 ? hardware.Cpu : Loc.Get("localAdvice.unknown"), hardware.Processors) + "\n"
        + (hardware.Gpus.Count == 0 ? Loc.Get("localAdvice.gpuUnknown") : string.Join("\n", hardware.Gpus.Select(g => Loc.Format("localAdvice.gpu", g.Name, Memory(g.TotalBytes), Memory(g.FreeBytes)))));

    internal void ShowSample(string state)
    {
        const long gb = LocalModelAdvice.GiB;
        _model = "example-coder:7b-q4";
        _confirmed = true;
        _cpuOnly = state == "limited";
        var hardware = new HardwareSnapshot(16 * gb, 3 * gb, "Example CPU", 8, []);
        Hardware = HardwareText(hardware);
        Show(hardware, new([new(_model, "sample-digest", 5 * gb, state == "remote")],
            state == "fits" ? [new(_model, "sample-digest", 6 * gb, 6 * gb)] : [], true), _model, _cpuOnly);
        foreach (var name in new[] { nameof(Model), nameof(Confirmed), nameof(CpuOnly) }) Raise(name);
    }
}
