using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace BoosterAI;

/// <summary>
/// Tools and calculations for WSL2 memory optimization and hardware recommendations (#76).
/// </summary>
public static partial class WslOptimization
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>Reads total physical host RAM in bytes. Falls back to GC memory info if OS queries fail.</summary>
    public static ulong GetTotalPhysicalMemoryBytes()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var stat = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref stat) && stat.ullTotalPhys > 0)
                {
                    return stat.ullTotalPhys;
                }
            }
            catch
            {
                // Fall back to GC memory info
            }
        }
        else if (OperatingSystem.IsLinux() && File.Exists("/proc/meminfo"))
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && ulong.TryParse(parts[1], CultureInfo.InvariantCulture, out var kb))
                        {
                            return kb * 1024UL;
                        }
                    }
                }
            }
            catch
            {
                // Fall back
            }
        }

        var gcTotal = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return gcTotal > 0 ? (ulong)gcTotal : 16UL * 1024 * 1024 * 1024;
    }

    /// <summary>Returns total host RAM rounded to nearest integer gigabyte (at least 2 GB).</summary>
    public static int GetTotalMemoryGb()
    {
        var bytes = GetTotalPhysicalMemoryBytes();
        var gb = (double)bytes / (1024 * 1024 * 1024);
        return Math.Max(2, (int)Math.Round(gb));
    }

    /// <summary>
    /// A cautious starting range based only on total RAM, not other virtual machines' measured needs.
    /// Keep roughly 3/8–1/2 for WSL; users sharing the host with VMs may need a lower cap (#80).
    /// </summary>
    public static (int lowerGb, int upperGb) RecommendMemoryRangeGb(int totalGb)
    {
        var (min, max) = GetSliderBounds(totalGb);
        var lower = Math.Clamp((int)Math.Floor(totalGb * 0.375), min, max);
        var upper = Math.Clamp((int)Math.Floor(totalGb * 0.5), lower, max);
        return (lower, upper);
    }

    /// <summary>The cautious starting value; existing saved and manually chosen caps remain independent.</summary>
    public static int RecommendMemoryGb(int totalGb) => RecommendMemoryRangeGb(totalGb).lowerGb;

    /// <summary>Determines sensible lower and upper slider bounds for WSL memory allocation.</summary>
    public static (int minGb, int maxGb) GetSliderBounds(int totalGb)
    {
        var minGb = totalGb >= 16 ? 4 : 2;
        var maxGb = Math.Max(minGb, totalGb - 2);
        return (minGb, maxGb);
    }

    /// <summary>Position of <paramref name="valueGb"/> between the slider bounds, clamped to 0..1.</summary>
    public static double SliderFraction(int valueGb, int minGb, int maxGb)
    {
        if (maxGb <= minGb) return 0.5;
        return Math.Clamp((double)(valueGb - minGb) / (maxGb - minGb), 0, 1);
    }

    /// <summary>Invokes <c>wsl.exe --shutdown</c> synchronously (Windows only).</summary>
    public static void ShutdownWsl()
    {
        if (!OperatingSystem.IsWindows()) return;
        var psi = new ProcessStartInfo("wsl.exe")
        {
            ArgumentList = { "--shutdown" },
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        proc?.WaitForExit(5000);
    }
}

/// <summary>
/// Parser and writer for Windows WSL configuration (%USERPROFILE%\.wslconfig).
/// Preserves comments, whitespace, and existing sections while updating or adding [wsl2] memory.
/// </summary>
public static class WslConfigFile
{
    private static string? _customPath;

    public static string DefaultConfigPath => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig")
        : "";

    public static string PathOnDisk => _customPath ?? DefaultConfigPath;

    /// <summary>Overrides the config file path (useful for testing and screenshot configs).</summary>
    public static void UseCustomPath(string? path) => _customPath = path;

    /// <summary>Loads .wslconfig and extracts any configured WSL2 memory allocation.</summary>
    public static (bool Exists, int? MemoryGb, string Content) Load(string? path = null)
    {
        var file = path ?? PathOnDisk;
        if (string.IsNullOrEmpty(file) || !File.Exists(file)) return (false, null, "");

        try
        {
            var content = File.ReadAllText(file);
            var mem = ParseMemoryFromIni(content);
            return (true, mem, content);
        }
        catch
        {
            return (false, null, "");
        }
    }

    /// <summary>Updates or creates .wslconfig with the specified memory in GB under [wsl2].</summary>
    public static void Save(int memoryGb, string? path = null)
    {
        var file = path ?? PathOnDisk;
        if (string.IsNullOrEmpty(file))
            throw new InvalidOperationException("No .wslconfig path available.");

        var (_, _, content) = Load(file);
        var updated = ApplyMemory(content, memoryGb);
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(file, updated);
    }

    /// <summary>Parses the memory value configured under the [wsl2] section, if present.</summary>
    public static int? ParseMemoryFromIni(string iniContent)
    {
        if (string.IsNullOrWhiteSpace(iniContent)) return null;

        var inWsl2 = false;
        var lines = iniContent.Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.StartsWith('#') || line.StartsWith(';')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var section = line[1..^1].Trim();
                inWsl2 = string.Equals(section, "wsl2", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inWsl2)
            {
                var eq = line.IndexOf('=');
                if (eq > 0)
                {
                    var key = line[..eq].Trim();
                    if (string.Equals(key, "memory", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = line[(eq + 1)..].Trim();
                        return ParseMemoryString(val);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Parses values like "16GB", "16384MB", "8gb", "32G" into integer GB.</summary>
    public static int? ParseMemoryString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var val = value.Trim();

        var match = Regex.Match(val, @"^([0-9]+(?:\.[0-9]+)?)\s*([a-zA-Z]+)?$", RegexOptions.CultureInvariant);
        if (!match.Success) return null;

        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            return null;

        var unit = match.Groups[2].Success ? match.Groups[2].Value.ToUpperInvariant() : "GB";
        return unit switch
        {
            "MB" or "M" => Math.Max(1, (int)Math.Round(num / 1024.0, MidpointRounding.AwayFromZero)),
            "GB" or "G" => Math.Max(1, (int)Math.Round(num, MidpointRounding.AwayFromZero)),
            "TB" or "T" => Math.Max(1, (int)Math.Round(num * 1024.0, MidpointRounding.AwayFromZero)),
            _ => (int)Math.Round(num, MidpointRounding.AwayFromZero)
        };
    }

    /// <summary>
    /// Modifies or inserts the <c>memory=&lt;memoryGb&gt;GB</c> setting inside <c>[wsl2]</c>,
    /// preserving other lines, comments, and line ending styles.
    /// </summary>
    public static string ApplyMemory(string iniContent, int memoryGb)
    {
        var targetLine = $"memory={memoryGb}GB";
        var eol = iniContent.Contains("\r\n") ? "\r\n" : "\n";

        if (string.IsNullOrWhiteSpace(iniContent))
        {
            return $"[wsl2]{eol}{targetLine}{eol}";
        }

        var lines = iniContent.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var inWsl2 = false;
        var wsl2SectionIndex = -1;
        var memoryReplaced = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var section = line[1..^1].Trim();
                if (string.Equals(section, "wsl2", StringComparison.OrdinalIgnoreCase))
                {
                    inWsl2 = true;
                    wsl2SectionIndex = i;
                    continue;
                }
                else if (inWsl2)
                {
                    // Leaving [wsl2] section without finding memory
                    lines.Insert(i, targetLine);
                    memoryReplaced = true;
                    break;
                }
            }

            if (inWsl2)
            {
                var eq = line.IndexOf('=');
                if (eq > 0)
                {
                    var key = line[..eq].Trim();
                    if (string.Equals(key, "memory", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = targetLine;
                        memoryReplaced = true;
                        break;
                    }
                }
            }
        }

        if (!memoryReplaced)
        {
            if (wsl2SectionIndex >= 0)
            {
                // [wsl2] was found, insert directly after header
                lines.Insert(wsl2SectionIndex + 1, targetLine);
            }
            else
            {
                // No [wsl2] section existed; append it
                if (lines.Count > 0 && lines[^1].Trim() != "")
                {
                    lines.Add("");
                }
                lines.Add("[wsl2]");
                lines.Add(targetLine);
            }
        }

        var result = string.Join(eol, lines);
        if (!result.EndsWith(eol)) result += eol;
        return result;
    }
}
