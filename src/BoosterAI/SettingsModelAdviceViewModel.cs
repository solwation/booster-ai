namespace BoosterAI;

/// <summary>Shared across agent pages, including missing CLIs. Construction never probes the machine.</summary>
public sealed class SettingsHardwareAdviceState : Observable
{
    private readonly Func<CancellationToken, Task<HardwareSnapshot>> _read;
    private readonly TimeProvider _clock;
    private DateTimeOffset? _readAt;
    private bool _reading;
    internal HardwareSnapshot Snapshot { get; private set; } = HardwareSnapshot.Empty;
    public bool HasReading => _readAt.HasValue;
    public bool IsReading { get => _reading; private set => Set(ref _reading, value); }

    public SettingsHardwareAdviceState() : this(LocalHardwareProbe.ReadAsync, TimeProvider.System) { }
    internal SettingsHardwareAdviceState(Func<CancellationToken, Task<HardwareSnapshot>> read, TimeProvider? clock = null)
    { _read = read; _clock = clock ?? TimeProvider.System; }

    internal async Task EnsureAsync(CancellationToken cancellation)
    {
        if (IsReading || _readAt is { } previous && _clock.GetUtcNow() - previous < TimeSpan.FromMinutes(2)) return;
        IsReading = true;
        try
        {
            var snapshot = await _read(cancellation);
            cancellation.ThrowIfCancellationRequested();
            SetSnapshot(snapshot);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { SetSnapshot(HardwareSnapshot.Empty); }
        finally { IsReading = false; }
    }

    internal void SetSnapshot(HardwareSnapshot snapshot)
    {
        Snapshot = snapshot;
        _readAt = _clock.GetUtcNow();
        Raise(nameof(HasReading));
    }
}

public sealed record PlanningModelProfile(string Id, string Model, string Source)
{
    public string Label => Loc.Get("settingsAdvice.profile." + Id);
}

/// <summary>Planning advice is explicit about its model and assumptions; never a rating of the CLI itself.</summary>
public sealed class SettingsModelAdviceViewModel : Observable
{
    private static readonly PlanningModelProfile None = new("none", "", "");
    private static readonly PlanningModelProfile CpuExample = new("cpu7b", "qwen2.5-coder:7b-instruct-q4_K_M", "https://ollama.com/library/qwen2.5-coder:7b-instruct-q4_K_M");
    private static readonly PlanningModelProfile GpuExample = new("gpu35b", "Qwen3.6-35B-A3B", "https://docs.openhands.dev/openhands/usage/llms/local-llms");
    // Primary sources publish rounded decimal GB, not exact runtime bytes. Never infer these from a parameter count.
    internal const long CpuExampleFileBytes = 4_700_000_000;
    internal const long GpuExampleVramBytes = 24_000_000_000;
    private readonly SettingsHardwareAdviceState _hardware;
    private PlanningModelProfile _profile = None;
    public bool IsSupported { get; }
    public IReadOnlyList<PlanningModelProfile> Profiles { get; }
    public PlanningModelProfile Profile
    {
        get => _profile;
        set
        {
            if (value is null || !Profiles.Contains(value) || !Set(ref _profile, value)) return;
            Exact.ClearAssessment();
            Refresh();
        }
    }
    internal LocalModelViewModel Exact { get; }
    internal HardwareSnapshot? MeasuredHardware => _hardware.HasReading ? _hardware.Snapshot : null;
    internal SettingsModelAdviceViewModel(string cli, SettingsHardwareAdviceState hardware, Func<Uri, CancellationToken, Task<OllamaSnapshot>>? readModels = null)
    {
        _hardware = hardware;
        IsSupported = ModelSelection.Supports(cli);
        Profiles = cli == "openhands" ? [None, GpuExample] : [None, CpuExample];
        Exact = new(cli, Catalog.StandardModel, readModels);
        hardware.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(hardware.HasReading)) Exact.ClearAssessment();
            Refresh();
        };
        Exact.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Exact.Assessment) && Exact.Assessment is not null)
            {
                _profile = None;
                Raise(nameof(Profile));
            }
            Refresh();
        };
    }

    private string State => Exact.Assessment is { } exact ? "exact." + exact.Suitability
        : !_hardware.HasReading ? _hardware.IsReading ? "checking" : "pending"
        : Profile == None ? "modelDependent"
        : Profile == GpuExample ? _hardware.Snapshot.Gpus.Count == 0 ? "gpuUnknown"
            : _hardware.Snapshot.Gpus.Any(g => g.TotalBytes >= GpuExampleVramBytes) ? "gpuLevel" : "gpuSmall"
        : _hardware.Snapshot.AvailableRam is null ? "ramUnknown"
        : _hardware.Snapshot.AvailableRam < CpuExampleFileBytes ? "cpuRamSmall" : "cpuRoom";
    public string CardStatus => Loc.Get("settingsAdvice.status." + State);
    public string Heading => Exact.Assessment is { } exact ? Loc.Get("localAdvice.verdict." + exact.Suitability) : CardStatus;
    public string Reason => Exact.Assessment is { } exact ? Loc.Get("localAdvice.reason." + exact.Reason) : Loc.Get("settingsAdvice.reason." + State);
    public string NextStep => Loc.Get(Exact.Assessment is null && Profile == None ? "settingsAdvice.next.choose" : "settingsAdvice.next.model");
    public bool HasProfile => Profile != None;
    public string ProfileEvidence => Profile == None ? "" : Loc.Format("settingsAdvice.evidence." + Profile.Id, Profile.Model);
    public string HardwareSummary => !_hardware.HasReading ? Loc.Get(_hardware.IsReading ? "settingsAdvice.measuring" : "settingsAdvice.notMeasured")
        : Loc.Format("settingsAdvice.hardwareSummary", LocalModelViewModel.Memory(_hardware.Snapshot.AvailableRam),
            _hardware.Snapshot.Gpus.Count == 0 ? Loc.Get("settingsAdvice.gpuUnknown")
            : string.Join(", ", _hardware.Snapshot.Gpus.Select(g => LocalModelViewModel.Memory(g.TotalBytes))));
    public string HardwareDetails => !_hardware.HasReading ? HardwareSummary : LocalModelViewModel.HardwareText(_hardware.Snapshot);
    public bool HasModelContext => Exact.Assessment is not null;
    public string ModelContext => Loc.Format("settingsAdvice.actualModel", Exact.Model);
    public string Source => Profile.Source;
    private void Refresh()
    {
        foreach (var name in new[] { nameof(CardStatus), nameof(Heading), nameof(Reason), nameof(NextStep), nameof(HasProfile), nameof(ProfileEvidence), nameof(HardwareSummary), nameof(HardwareDetails), nameof(Source), nameof(HasModelContext), nameof(ModelContext) }) Raise(name);
    }
}
