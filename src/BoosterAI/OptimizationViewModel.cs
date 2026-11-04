using Avalonia.Media;
using Avalonia.Threading;

namespace BoosterAI;

/// <summary>
/// Manages WSL memory tuning, hardware recommendations, and optimization banner state (#76).
/// </summary>
public sealed class OptimizationViewModel : Observable
{
    private readonly AppConfig _cfg;
    private int _totalSystemMemoryGb;
    private int? _currentAllocatedGb;
    private int _sliderMemoryGb;
    private bool _isOptimized;
    private bool _bannerVisible;
    private string? _statusMessage;
    private bool _statusIsError;
    private bool _fakeWindows;
    private WslMemoryMonitor? _memoryMonitor;
    private DispatcherTimer? _memoryRefresh;
    private bool _memoryStopped;
    private WslMemorySnapshot _memory = new(WslMemoryState.Unavailable, null, null, null, null, TimeSpan.Zero, 0);

    public OptimizationViewModel(AppConfig cfg)
    {
        _cfg = cfg;
        Refresh();
    }

    public int TotalSystemMemoryGb => _totalSystemMemoryGb;
    public int? CurrentAllocatedGb => _currentAllocatedGb;

    public int SliderMemoryGb
    {
        get => _sliderMemoryGb;
        set
        {
            var clamped = Math.Clamp(value, MinMemoryGb, MaxMemoryGb);
            if (Set(ref _sliderMemoryGb, clamped))
            {
                Raise(nameof(SliderMemoryText));
            }
        }
    }

    public string SliderMemoryText => Loc.Format("settings.optimization.selectedCap", _sliderMemoryGb);

    public int RecommendedGb => RecommendedLowerGb;
    public int RecommendedLowerGb => WslOptimization.RecommendMemoryRangeGb(_totalSystemMemoryGb).lowerGb;
    public int RecommendedUpperGb => WslOptimization.RecommendMemoryRangeGb(_totalSystemMemoryGb).upperGb;
    public int MinMemoryGb => WslOptimization.GetSliderBounds(_totalSystemMemoryGb).minGb;
    public int MaxMemoryGb => WslOptimization.GetSliderBounds(_totalSystemMemoryGb).maxGb;

    public string TotalMemoryText => $"{_totalSystemMemoryGb} GB";

    public string CurrentConfigText => _currentAllocatedGb.HasValue
        ? $"{_currentAllocatedGb.Value} GB"
        : Loc.Get("settings.optimization.defaultUnset");

    public string RecommendationText => Loc.Format("settings.optimization.recommendation", RecommendedLowerGb, RecommendedUpperGb);
    public string RecommendationValue => $"{RecommendedLowerGb}–{RecommendedUpperGb} GB";

    public double RecommendationLowerFraction =>
        WslOptimization.SliderFraction(RecommendedLowerGb, MinMemoryGb, MaxMemoryGb);
    public double RecommendationUpperFraction =>
        WslOptimization.SliderFraction(RecommendedUpperGb, MinMemoryGb, MaxMemoryGb);
    public double RecommendationCentreFraction => (RecommendationLowerFraction + RecommendationUpperFraction) / 2;

    private const double BytesPerGb = 1024d * 1024 * 1024;
    public bool HasMemoryHistory => _memory.SampleCount > 0;
    public bool HasCurrentMemory => _memory.CurrentBytes.HasValue;
    public string MemoryCurrentValue => MemoryValue(_memory.CurrentBytes);
    public string MemoryMinimumValue => MemoryValue(_memory.MinimumBytes);
    public string MemoryAverageValue => MemoryValue(_memory.AverageBytes);
    public string MemoryMaximumValue => MemoryValue(_memory.MaximumBytes);
    public string MemoryScaleText => Loc.Format("settings.optimization.scale", TotalMemoryText);
    public string MemoryQuarterText => ScaleValue(0.25);
    public string MemoryHalfText => ScaleValue(0.5);
    public string MemoryThreeQuarterText => ScaleValue(0.75);
    public string MemoryCapText => _currentAllocatedGb is { } cap
        ? $"{cap} GB" : Loc.Format("settings.optimization.defaultCap", _totalSystemMemoryGb / 2d);
    public double MemoryCurrentFraction => MemoryFraction(_memory.CurrentBytes ?? 0);
    public double MemoryCapFraction => HostFraction(_currentAllocatedGb ?? _totalSystemMemoryGb / 2d);
    public double SuggestedLowerFraction => HostFraction(RecommendedLowerGb);
    public double SuggestedUpperFraction => HostFraction(RecommendedUpperGb);
    public bool MemoryOutsideScale => _memory.MaximumBytes > _totalSystemMemoryGb * BytesPerGb
        || _memory.CurrentBytes > _totalSystemMemoryGb * BytesPerGb || _currentAllocatedGb > _totalSystemMemoryGb;
    public string MemoryCurrentText => _memory.CurrentBytes.HasValue
        ? Loc.Format("settings.optimization.usageCurrent", Gb(_memory.CurrentBytes.Value))
        : Loc.Get(_memory.State switch
        {
            WslMemoryState.NotRunning => "settings.optimization.usageNotRunning",
            WslMemoryState.LegacyProcess => "settings.optimization.usageLegacy",
            WslMemoryState.Stale => "settings.optimization.usageStale",
            _ => "settings.optimization.usageUnavailable",
        });
    public string MemoryStatisticsText => HasMemoryHistory
        ? Loc.Format("settings.optimization.usageStatistics", Gb(_memory.AverageBytes!.Value),
            Gb(_memory.MinimumBytes!.Value), Gb(_memory.MaximumBytes!.Value)) : "";
    public string MemoryPeriodText => HasMemoryHistory
        ? Loc.Format("settings.optimization.usagePeriod", _memory.CollectedTime.TotalMinutes.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), _memory.SampleCount)
        : Loc.Get("settings.optimization.usageWaiting");
    public double MemoryLowerFraction => MemoryFraction(_memory.MinimumBytes ?? 0);
    public double MemoryUpperFraction => MemoryFraction(_memory.MaximumBytes ?? 0);
    public double MemoryAverageFraction => MemoryFraction(_memory.AverageBytes ?? 0);
    // The overview uses the whole host's RAM, from zero. The editable slider keeps its own bounds.
    private double MemoryFraction(double bytes) => HostFraction(bytes / BytesPerGb);
    private double HostFraction(double gb) => _totalSystemMemoryGb <= 0 ? 0 : Math.Clamp(gb / _totalSystemMemoryGb, 0, 1);
    private static string MemoryValue(double? bytes) => bytes is { } value ? Gb(value) + " GB" : "—";
    private string ScaleValue(double fraction) => (_totalSystemMemoryGb * fraction).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " GB";
    private static string Gb(double bytes) => (bytes / BytesPerGb).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Only App startup calls this; tests and headless screenshots never read live memory.</summary>
    public void StartMemoryMonitoring()
    {
        if (!OperatingSystem.IsWindows() || _memoryMonitor is not null) return;
        _memoryStopped = false;
        _memoryMonitor = new();
        _memoryMonitor.Updated += snapshot => Dispatcher.UIThread.Post(() =>
        {
            if (!_memoryStopped) SetMemorySnapshot(snapshot);
        });
        _memoryMonitor.Start();
        _memoryRefresh = new DispatcherTimer { Interval = WslMemoryHistory.Interval };
        _memoryRefresh.Tick += (_, _) =>
        {
            if (_memoryMonitor is not null) SetMemorySnapshot(_memoryMonitor.Snapshot());
        };
        _memoryRefresh.Start();
    }

    public void StopMemoryMonitoring()
    {
        _memoryStopped = true;
        _memoryRefresh?.Stop();
        _memoryRefresh = null;
        _memoryMonitor?.Dispose();
        _memoryMonitor = null;
    }

    internal void SetMemorySnapshot(WslMemorySnapshot snapshot)
    {
        _memory = snapshot;
        foreach (var property in new[] { nameof(HasMemoryHistory), nameof(MemoryCurrentText), nameof(MemoryStatisticsText),
            nameof(MemoryPeriodText), nameof(MemoryLowerFraction), nameof(MemoryUpperFraction), nameof(MemoryAverageFraction),
            nameof(HasCurrentMemory), nameof(MemoryCurrentValue), nameof(MemoryMinimumValue), nameof(MemoryAverageValue),
            nameof(MemoryMaximumValue), nameof(MemoryCurrentFraction), nameof(MemoryOutsideScale) }) Raise(property);
    }

    private void RaiseMemoryScale()
    {
        foreach (var property in new[] { nameof(MemoryScaleText), nameof(MemoryQuarterText), nameof(MemoryHalfText),
            nameof(MemoryThreeQuarterText), nameof(MemoryCapText), nameof(MemoryCapFraction), nameof(SuggestedLowerFraction),
            nameof(SuggestedUpperFraction), nameof(MemoryCurrentFraction), nameof(MemoryLowerFraction), nameof(MemoryUpperFraction),
            nameof(MemoryAverageFraction), nameof(MemoryOutsideScale), nameof(RecommendationValue) }) Raise(property);
    }

    public bool IsWindows => _fakeWindows || OperatingSystem.IsWindows();
    public bool IsLinux => !_fakeWindows && !OperatingSystem.IsWindows();

    public bool BannerVisible
    {
        get => _bannerVisible;
        set => Set(ref _bannerVisible, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (Set(ref _statusMessage, value))
            {
                Raise(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);
    public bool StatusIsError => _statusIsError;

    public IBrush StatusColorBrush => _statusIsError
        ? new SolidColorBrush(Color.Parse("#E5484D"))
        : new SolidColorBrush(Color.Parse("#46A758"));

    public void Refresh()
    {
        _totalSystemMemoryGb = WslOptimization.GetTotalMemoryGb();
        var (_, mem, _) = WslConfigFile.Load();
        _currentAllocatedGb = mem;
        _sliderMemoryGb = mem ?? RecommendedGb;
        _isOptimized = mem.HasValue;

        // Banner shows on Windows when no explicit memory configuration is set and user hasn't dismissed it
        _bannerVisible = OperatingSystem.IsWindows() && !_isOptimized && !_cfg.DismissedWslOptimization;

        Raise(nameof(TotalSystemMemoryGb));
        Raise(nameof(TotalMemoryText));
        Raise(nameof(CurrentAllocatedGb));
        Raise(nameof(CurrentConfigText));
        Raise(nameof(SliderMemoryGb));
        Raise(nameof(SliderMemoryText));
        Raise(nameof(RecommendedGb));
        Raise(nameof(RecommendationText));
        Raise(nameof(RecommendedLowerGb));
        Raise(nameof(RecommendedUpperGb));
        Raise(nameof(RecommendationLowerFraction));
        Raise(nameof(RecommendationUpperFraction));
        Raise(nameof(RecommendationCentreFraction));
        Raise(nameof(MinMemoryGb));
        Raise(nameof(MaxMemoryGb));
        Raise(nameof(BannerVisible));
        RaiseMemoryScale();
    }

    public void ApplyRecommendation()
    {
        SliderMemoryGb = RecommendedGb;
    }

    public void SaveConfig()
    {
        try
        {
            WslConfigFile.Save(SliderMemoryGb);
            _currentAllocatedGb = SliderMemoryGb;
            _isOptimized = true;
            BannerVisible = false;
            _statusIsError = false;
            StatusMessage = Loc.Get("settings.optimization.saved");
            Raise(nameof(CurrentAllocatedGb));
            Raise(nameof(CurrentConfigText));
            RaiseMemoryScale();
            Raise(nameof(StatusColorBrush));
        }
        catch (Exception ex)
        {
            _statusIsError = true;
            StatusMessage = Loc.Format("settings.optimization.saveFailed", ex.Message);
            Raise(nameof(StatusColorBrush));
        }
        Raise(nameof(StatusIsError));
    }

    public void RestartWsl()
    {
        try
        {
            WslOptimization.ShutdownWsl();
            _statusIsError = false;
            StatusMessage = Loc.Get("settings.optimization.restarted");
            Raise(nameof(StatusColorBrush));
        }
        catch (Exception ex)
        {
            _statusIsError = true;
            StatusMessage = Loc.Format("settings.optimization.restartFailed", ex.Message);
            Raise(nameof(StatusColorBrush));
        }
        Raise(nameof(StatusIsError));
    }

    public void DismissBanner()
    {
        _cfg.DismissedWslOptimization = true;
        ConfigStore.Save(_cfg);
        BannerVisible = false;
    }

    /// <summary>Forces the optimization banner to display for screenshot verification.</summary>
    public void ShowFakeUnoptimized(int totalGb = 64, int? selectedGb = null)
    {
        _fakeWindows = true;
        _totalSystemMemoryGb = totalGb;
        _currentAllocatedGb = null;
        _sliderMemoryGb = selectedGb ?? RecommendedLowerGb;
        _bannerVisible = true;
        Raise(nameof(IsWindows));
        Raise(nameof(IsLinux));
        Raise(nameof(TotalSystemMemoryGb));
        Raise(nameof(TotalMemoryText));
        Raise(nameof(MinMemoryGb));
        Raise(nameof(MaxMemoryGb));
        Raise(nameof(CurrentAllocatedGb));
        Raise(nameof(CurrentConfigText));
        Raise(nameof(SliderMemoryGb));
        Raise(nameof(SliderMemoryText));
        Raise(nameof(RecommendedGb));
        Raise(nameof(RecommendationText));
        Raise(nameof(RecommendedLowerGb));
        Raise(nameof(RecommendedUpperGb));
        Raise(nameof(RecommendationLowerFraction));
        Raise(nameof(RecommendationUpperFraction));
        Raise(nameof(RecommendationCentreFraction));
        Raise(nameof(BannerVisible));
        RaiseMemoryScale();
        SetMemorySnapshot(new(WslMemoryState.Available, (long)(12.4 * BytesPerGb), 11.7 * BytesPerGb,
            (long)(8.2 * BytesPerGb), (long)(17.6 * BytesPerGb), TimeSpan.FromMinutes(30), 61));
    }
}
