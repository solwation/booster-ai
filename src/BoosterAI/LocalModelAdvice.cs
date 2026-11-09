using System.Text.Json;

namespace BoosterAI;

internal enum ModelBackend { Unknown, Api, Ollama }
internal enum ModelSuitability { Unknown, Remote, Caution, Limited, Fits }
internal sealed record ModelSelection(ModelBackend Backend, string Model)
{
    public static bool Supports(string cli) => cli is "aider" or "interpreter" or "openhands";

    public static ModelSelection From(string cli, string model)
    {
        if (!Supports(cli)) return new(ModelBackend.Unknown, "");
        var prefixes = cli == "aider" ? new[] { "ollama_chat/", "ollama/" } : new[] { "ollama/" };
        foreach (var prefix in prefixes)
            if (model.StartsWith(prefix, StringComparison.Ordinal)) return new(ModelBackend.Ollama, model[prefix.Length..]);
        // Provider names are hints only: external CLI configuration can still replace the endpoint.
        if (new[] { "openai/", "anthropic/", "gemini/", "deepseek/", "azure/" }.Any(p => model.StartsWith(p, StringComparison.Ordinal))
            || model.StartsWith("gpt-", StringComparison.Ordinal) || model.StartsWith("claude-", StringComparison.Ordinal))
            return new(ModelBackend.Api, model);
        return new(ModelBackend.Unknown, model is "" or "standard" or "local" ? "" : model);
    }
}

internal sealed record OllamaModel(string Name, string Digest, long Bytes, bool Remote);
internal sealed record OllamaRuntime(string Name, string Digest, long Bytes, long VramBytes);
internal sealed record ModelAssessment(ModelSuitability Suitability, string Reason);

/// <summary>Only exact installed names or Ollama's implicit :latest alias; never infer size from parameter counts.</summary>
internal static class LocalModelAdvice
{
    internal const long GiB = 1024L * 1024 * 1024;
    internal static string Canonical(string model) => model.LastIndexOf(':') > model.LastIndexOf('/') ? model : model + ":latest";

    public static OllamaModel? Find(string model, IReadOnlyList<OllamaModel> models) =>
        models.FirstOrDefault(m => m.Name == Canonical(model));

    public static ModelAssessment Assess(OllamaModel? model, OllamaRuntime? runtime, HardwareSnapshot hardware, bool cpuOnly)
    {
        if (model is null) return new(ModelSuitability.Unknown, "missingModel");
        if (model.Remote) return new(ModelSuitability.Remote, "remote");
        if (model.Bytes <= 0) return new(ModelSuitability.Unknown, "missingModel");
        // A digest prevents a similarly named or re-tagged model from supplying unrelated runtime evidence.
        var loaded = runtime is { Bytes: > 0 } && model.Digest.Length > 0 && runtime.Name == model.Name && runtime.Digest == model.Digest
            && runtime.VramBytes >= 0 && runtime.VramBytes <= runtime.Bytes;
        if (loaded && runtime!.VramBytes == runtime.Bytes) return new(ModelSuitability.Fits, "gpuLoaded");
        if (loaded && runtime!.VramBytes > 0) return new(ModelSuitability.Caution, "splitLoaded");
        if (loaded) return new(ModelSuitability.Caution, "cpuLoaded");
        if (cpuOnly && hardware.TotalRam is > 0 && model.Bytes >= hardware.TotalRam.Value)
            return new(ModelSuitability.Limited, "ramTotal");
        if (cpuOnly && hardware.AvailableRam is >= 0 && model.Bytes >= hardware.AvailableRam.Value)
            return new(ModelSuitability.Limited, "ramAvailable");
        return new(ModelSuitability.Unknown, cpuOnly ? "cpuUnloaded" : "unloaded");
    }

    public static IReadOnlyList<OllamaModel> ParseTags(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("models").EnumerateArray().Take(4096).Select(m =>
            new OllamaModel(Text(m, "name"), Text(m, "digest"), Number(m, "size"),
                Text(m, "remote_host").Length > 0 || Text(m, "remote_model").Length > 0 || (Text(m, "name").EndsWith(":cloud", StringComparison.Ordinal) || Text(m, "name").EndsWith("-cloud", StringComparison.Ordinal))))
            .Where(m => m.Name.Length > 0).ToArray();
    }

    public static IReadOnlyList<OllamaRuntime> ParseRunning(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("models").EnumerateArray().Take(4096).Select(m =>
            new OllamaRuntime(Text(m, "name"), Text(m, "digest"), Number(m, "size"), Number(m, "size_vram", -1))).ToArray();
    }

    private static string Text(JsonElement value, string key) => value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    private static long Number(JsonElement value, string key, long missing = 0) => value.TryGetProperty(key, out var p) && p.TryGetInt64(out var n) ? n : missing;
}
