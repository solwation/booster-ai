using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace BoosterAI;

internal sealed record HardwareSnapshot(long? TotalRam, long? AvailableRam, string Cpu, int Processors, IReadOnlyList<GpuMemory> Gpus)
{
    public static readonly HardwareSnapshot Empty = new(null, null, "", 0, []);
}
internal sealed record GpuMemory(string Name, long TotalBytes, long FreeBytes);

/// <summary>One explicit, read-only snapshot of the OS running BoosterAI. No model, shell, driver install or benchmark.</summary>
internal static class LocalHardwareProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended;
    }
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    public static async Task<HardwareSnapshot> ReadAsync(CancellationToken cancellation)
    {
        long? total = null, available = null;
        var cpu = "";
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                if (GlobalMemoryStatusEx(ref status) && status.TotalPhysical is > 0 and <= long.MaxValue && status.AvailablePhysical <= status.TotalPhysical)
                { total = (long)status.TotalPhysical; available = (long)status.AvailablePhysical; }
                cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "";
            }
            else if (OperatingSystem.IsLinux())
            {
                (total, available) = ParseMemInfo(await File.ReadAllTextAsync("/proc/meminfo", cancellation));
                cpu = (await File.ReadAllLinesAsync("/proc/cpuinfo", cancellation)).FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal))?.Split(':', 2).Last().Trim() ?? "";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException) { }
        return new(total, available, cpu, Environment.ProcessorCount, await NvidiaAsync(cancellation));
    }

    internal static (long? Total, long? Available) ParseMemInfo(string text)
    {
        long? Read(string name, bool allowZero = false)
        {
            var parts = text.Split('\n').FirstOrDefault(l => l.StartsWith(name + ":", StringComparison.Ordinal))?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return parts is { Length: 3 } && parts[2] == "kB" && long.TryParse(parts[1], out var kb) && (kb > 0 || allowZero && kb == 0) && kb <= long.MaxValue / 1024 ? kb * 1024 : null;
        }
        var total = Read("MemTotal");
        var available = Read("MemAvailable", allowZero: true);
        return (total, available <= total ? available : null);
    }

    internal static IReadOnlyList<GpuMemory> ParseNvidia(string output)
    {
        var result = new List<GpuMemory>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(',');
            if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[0])
                || !long.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var total)
                || !long.TryParse(parts[2].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var free)
                || total <= 0 || total > long.MaxValue / (1024 * 1024) || free < 0 || free > total) return [];
            result.Add(new(parts[0].Trim(), total * 1024 * 1024, free * 1024 * 1024));
        }
        return result;
    }

    private static string NvidiaExecutable()
    {
        string[] fallbacks = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"),
               Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")]
            : ["/usr/bin/nvidia-smi", "/usr/lib/wsl/lib/nvidia-smi"];
        return fallbacks.FirstOrDefault(File.Exists) ?? "nvidia-smi";
    }

    private static async Task<IReadOnlyList<GpuMemory>> NvidiaAsync(CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        using var process = new Process { StartInfo = new ProcessStartInfo(NvidiaExecutable())
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "--query-gpu=name,memory.total,memory.free", "--format=csv,noheader,nounits" }
        } };
        try
        {
            process.Start();
            // Bound both pipes; even a replaced executable cannot retain unbounded output or deadlock the UI.
            var output = ReadBounded(process.StandardOutput, 16_384, deadline.Token);
            var error = ReadBounded(process.StandardError, 16_384, deadline.Token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(deadline.Token));
            return process.ExitCode == 0 ? ParseNvidia(await output) : [];
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or OperationCanceledException or InvalidOperationException) { return []; }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            try { await process.WaitForExitAsync(); } catch (InvalidOperationException) { }
        }
    }

    private static async Task<string> ReadBounded(StreamReader reader, int maximum, CancellationToken cancellation)
    {
        var buffer = new char[maximum + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await reader.ReadAsync(buffer.AsMemory(read), cancellation);
            if (n == 0) return new string(buffer, 0, read);
            read += n;
        }
        throw new IOException("Probe output exceeded its limit.");
    }
}

internal sealed record OllamaSnapshot(IReadOnlyList<OllamaModel> Models, IReadOnlyList<OllamaRuntime> Running, bool RuntimeKnown);

/// <summary>Only loopback HTTP and metadata endpoints. Redirects/proxies cannot turn this into a remote server query.</summary>
internal static class LocalOllamaProbe
{
    internal static Uri? Endpoint(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "http" || !uri.IsLoopback
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/") return null;
        return uri;
    }

    public static async Task<OllamaSnapshot> ReadAsync(Uri endpoint, CancellationToken cancellation)
    {
        if (Endpoint(endpoint.AbsoluteUri) is null) throw new ArgumentException("Only loopback HTTP endpoints are supported.");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler) { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(4) };
        return await ReadAsync(client, cancellation);
    }

    internal static async Task<OllamaSnapshot> ReadAsync(HttpClient client, CancellationToken cancellation)
    {
        var models = LocalModelAdvice.ParseTags(await Metadata(client, "api/tags", cancellation));
        try { return new(models, LocalModelAdvice.ParseRunning(await Metadata(client, "api/ps", cancellation)), true); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or OperationCanceledException)
        { cancellation.ThrowIfCancellationRequested(); return new(models, [], false); }
    }

    private static async Task<string> Metadata(HttpClient client, string path, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        cancellation = deadline.Token;
        using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        var bytes = new byte[1_048_577];
        var count = 0;
        while (count < bytes.Length)
        {
            var n = await stream.ReadAsync(bytes.AsMemory(count), cancellation);
            if (n == 0) return System.Text.Encoding.UTF8.GetString(bytes, 0, count);
            count += n;
        }
        throw new IOException("Metadata exceeded its limit.");
    }
}
