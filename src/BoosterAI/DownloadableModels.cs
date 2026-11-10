namespace BoosterAI;

internal sealed record DownloadableModel(string Id, long DownloadBytes, string Purpose, bool OpenHandsExample = false)
{
    public string Source => "https://ollama.com/library/" + Id;
    // This is a deliberately visible planning heuristic, not a measured runtime requirement.
    public long PlanningBytes => (long)Math.Ceiling(DownloadBytes * 1.25) + 2 * LocalModelAdvice.GiB;
}

internal enum PlanningFit { Unknown, GpuCandidate, CpuCandidate, Limited }
internal static class DownloadableModels
{
    public const string Reviewed = "2026-11-10";
    // Rounded decimal sizes from the exact tag's total Details, not a parameter-count estimate.
    public static readonly IReadOnlyList<DownloadableModel> All = Array.AsReadOnly<DownloadableModel>([
        new("qwen2.5-coder:1.5b-instruct-q4_K_M", 986_000_000, "smallCode"),
        new("qwen3.5:4b-q4_K_M", 3_300_000_000, "generalTools"),
        new("qwen2.5-coder:7b-instruct-q4_K_M", 4_700_000_000, "code"),
        new("qwen3.5:9b-q4_K_M", 6_600_000_000, "generalTools"),
        new("qwen3.6:35b-a3b-q4_K_M", 24_000_000_000, "agentCode", true),
    ]);

    public static PlanningFit Assess(DownloadableModel model, HardwareSnapshot hardware)
    {
        // Each GPU is a separate candidate. Never pool cards or infer GPU support from its name.
        if (hardware.Gpus.Any(g => g.FreeBytes >= model.PlanningBytes)
            && hardware.AvailableRam is >= 2 * LocalModelAdvice.GiB) return PlanningFit.GpuCandidate;
        if (hardware.AvailableRam >= model.PlanningBytes) return PlanningFit.CpuCandidate;
        return hardware.AvailableRam.HasValue ? PlanningFit.Limited : PlanningFit.Unknown;
    }

    public static DownloadableModel? Recommend(HardwareSnapshot hardware)
    {
        var gpu = All.LastOrDefault(m => Assess(m, hardware) == PlanningFit.GpuCandidate);
        if (gpu is not null) return gpu;
        // A small modern example first on CPU; avoid suggesting a huge CPU model merely because RAM fits.
        return All.FirstOrDefault(m => m.Id == "qwen3.5:4b-q4_K_M" && Assess(m, hardware) == PlanningFit.CpuCandidate)
            ?? All.FirstOrDefault(m => Assess(m, hardware) == PlanningFit.CpuCandidate);
    }
}
