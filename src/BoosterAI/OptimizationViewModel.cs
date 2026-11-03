using Avalonia.Media;

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

    public double RecommendationLowerFraction =>
        WslOptimization.SliderFraction(RecommendedLowerGb, MinMemoryGb, MaxMemoryGb);
    public double RecommendationUpperFraction =>
        WslOptimization.SliderFraction(RecommendedUpperGb, MinMemoryGb, MaxMemoryGb);
    public double RecommendationCentreFraction => (RecommendationLowerFraction + RecommendationUpperFraction) / 2;

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
    }
}
