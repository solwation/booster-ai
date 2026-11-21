using System.Text;
using System.Text.RegularExpressions;

namespace BoosterAI;

/// <summary>What <c>[experimental] autoMemoryReclaim</c> in .wslconfig says (#183).</summary>
public enum MemoryReclaimMode { NotSet, Disabled, Gradual, DropCache, Other }

/// <summary>The parsed setting; <paramref name="Raw"/> is the value as written (null when the key is absent).</summary>
public readonly record struct MemoryReclaimSetting(MemoryReclaimMode Mode, string? Raw);

/// <summary>
/// Reads and edits <c>autoMemoryReclaim</c> under <c>[experimental]</c> in %USERPROFILE%\.wslconfig (#183).
/// Edits touch only that key's line (and an <c>[experimental]</c> header this feature added): every other byte,
/// including comments, other sections, line endings and a byte order mark, stays as it was.
/// </summary>
public static partial class WslMemoryReclaim
{
    public const string SectionName = "experimental";
    public const string Key = "autoMemoryReclaim";
    public const string Gradual = "gradual";
    public const string DropCache = "dropCache";

    /// <summary>
    /// autoMemoryReclaim shipped with the Store WSL 2.0.0 release (September 2023). Microsoft's .wslconfig docs list the key
    /// without naming a minimum version, so this is the release where it first appeared outside pre-releases.
    /// </summary>
    public static readonly Version MinimumWslVersion = new(2, 0, 0);

    public static bool Supports(Version? wsl) => wsl is not null && wsl >= MinimumWslVersion;

    private struct Line(string text, string eol)
    {
        public string Text = text;
        public string Eol = eol;
        public readonly string Trimmed => Text.Trim();
        public readonly bool IsBlank => Trimmed.Length == 0;
        public readonly bool IsComment => Trimmed.StartsWith('#') || Trimmed.StartsWith(';');
        public readonly string? Section => !IsComment && Trimmed.Length >= 2 && Trimmed[0] == '[' && Trimmed[^1] == ']' ? Trimmed[1..^1].Trim() : null;
        public readonly bool IsKey => !IsComment && Section is null && Trimmed.IndexOf('=') is > 0 and var eq
            && string.Equals(Trimmed[..eq].Trim(), Key, StringComparison.OrdinalIgnoreCase);
        public readonly string Value => Trimmed[(Trimmed.IndexOf('=') + 1)..].Trim().Trim('"').Trim();
    }

    /// <summary>Splits into lines that keep their own terminator ("\r\n", "\n" or "" for an unterminated last line).</summary>
    private static List<Line> Split(string content)
    {
        var lines = new List<Line>();
        var start = 0;
        while (start < content.Length)
        {
            var nl = content.IndexOf('\n', start);
            if (nl < 0) { lines.Add(new(content[start..], "")); break; }
            var end = nl > start && content[nl - 1] == '\r' ? nl - 1 : nl;
            lines.Add(new(content[start..end], content[end..(nl + 1)]));
            start = nl + 1;
        }
        return lines;
    }

    private static string Join(IEnumerable<Line> lines)
    {
        var sb = new StringBuilder();
        foreach (var l in lines) sb.Append(l.Text).Append(l.Eol);
        return sb.ToString();
    }

    private static bool IsExperimental(string? section) => string.Equals(section, SectionName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The key lines inside [experimental] sections, each with its section's header index.</summary>
    private static List<(int Header, int Key)> FindKeys(List<Line> lines, out int firstHeader)
    {
        var found = new List<(int, int)>();
        firstHeader = -1;
        var header = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Section is { } section)
            {
                header = IsExperimental(section) ? i : -1;
                if (header >= 0 && firstHeader < 0) firstHeader = i;
            }
            else if (header >= 0 && lines[i].IsKey) found.Add((header, i));
        }
        return found;
    }

    /// <summary>Reads the setting; with several key lines the last one counts, as in an ordinary INI reader.</summary>
    public static MemoryReclaimSetting Parse(string iniContent)
    {
        var lines = Split(iniContent ?? "");
        var keys = FindKeys(lines, out _);
        if (keys.Count == 0) return new(MemoryReclaimMode.NotSet, null);
        var raw = lines[keys[^1].Key].Value;
        var mode = raw.ToLowerInvariant() switch
        {
            "disabled" => MemoryReclaimMode.Disabled,
            "gradual" => MemoryReclaimMode.Gradual,
            "dropcache" => MemoryReclaimMode.DropCache,
            _ => MemoryReclaimMode.Other,
        };
        return new(mode, raw);
    }

    /// <summary>
    /// Sets <c>autoMemoryReclaim=<paramref name="value"/></c>, or removes it when <paramref name="value"/> is null.
    /// Setting replaces the first key line in place (later duplicates go) or adds the line right after the first
    /// <c>[experimental]</c> header, appending that section when there is none. Removing deletes the key lines and an
    /// <c>[experimental]</c> section that is left with nothing but blank lines, together with the blank separator
    /// before it when it was the last section, so set-then-remove restores the original file.
    /// </summary>
    public static string Apply(string iniContent, string? value)
    {
        var content = iniContent ?? "";
        var lines = Split(content);
        // New lines follow the file; a new or single-line file is a Windows file, so CRLF.
        var eol = content.Contains("\r\n") ? "\r\n" : content.Contains('\n') ? "\n" : "\r\n";
        var keys = FindKeys(lines, out var firstHeader);

        if (value is not null)
        {
            var target = $"{Key}={value}";
            if (keys.Count > 0)
            {
                var first = keys[0].Key;
                for (var k = keys.Count - 1; k >= 1; k--) lines.RemoveAt(keys[k].Key);
                lines[first] = new(target, lines[first].Eol == "" ? "" : lines[first].Eol);
            }
            else if (firstHeader >= 0)
            {
                var header = lines[firstHeader];
                if (header.Eol == "") lines[firstHeader] = new(header.Text, eol);
                lines.Insert(firstHeader + 1, new(target, eol));
            }
            else
            {
                if (lines.Count > 0)
                {
                    if (lines[^1].Eol == "") lines[^1] = new(lines[^1].Text, eol);
                    if (!lines[^1].IsBlank) lines.Add(new("", eol));
                }
                lines.Add(new("[" + SectionName + "]", eol));
                lines.Add(new(target, eol));
            }
            return Join(lines);
        }

        if (keys.Count == 0) return content;
        foreach (var section in keys.GroupBy(k => k.Header).OrderByDescending(g => g.Key))
        {
            foreach (var (_, key) in section.OrderByDescending(k => k.Key)) lines.RemoveAt(key);
            var start = section.Key;
            var end = start + 1;
            while (end < lines.Count && lines[end].Section is null && lines[end].IsBlank) end++;
            // Something besides blank lines remains (another key or a comment): keep the section.
            if (end < lines.Count && lines[end].Section is null) continue;
            if (end == lines.Count) while (start > 0 && lines[start - 1].IsBlank) start--;
            lines.RemoveRange(start, end - start);
        }
        return Join(lines);
    }

    /// <summary>
    /// The lines an edit removes ("- ") and adds ("+ "), from the first to the last differing line. This is what the
    /// confirmation shows before anything is written.
    /// </summary>
    public static string Preview(string before, string after)
    {
        var a = Split(before ?? "");
        var b = Split(after ?? "");
        var prefix = 0;
        while (prefix < a.Count && prefix < b.Count && a[prefix].Text == b[prefix].Text) prefix++;
        var suffix = 0;
        while (suffix < a.Count - prefix && suffix < b.Count - prefix
            && a[a.Count - 1 - suffix].Text == b[b.Count - 1 - suffix].Text) suffix++;
        var removed = a.Skip(prefix).Take(a.Count - prefix - suffix).Select(l => "- " + l.Text);
        var added = b.Skip(prefix).Take(b.Count - prefix - suffix).Select(l => "+ " + l.Text);
        return string.Join("\n", removed.Concat(added)).TrimEnd();
    }

    /// <summary>Reads the file as text with the encoding it was saved in (UTF-8 without BOM when missing or unmarked).</summary>
    public static (bool Exists, string Content, Encoding Encoding) Read(string? path = null)
    {
        var file = path ?? WslConfigFile.PathOnDisk;
        Encoding plain = new UTF8Encoding(false);
        if (string.IsNullOrEmpty(file) || !File.Exists(file)) return (false, "", plain);
        var bytes = File.ReadAllBytes(file);
        var (encoding, skip) = bytes switch
        {
            [0xEF, 0xBB, 0xBF, ..] => ((Encoding)new UTF8Encoding(true), 3),
            [0xFF, 0xFE, ..] => (new UnicodeEncoding(false, true), 2),
            [0xFE, 0xFF, ..] => (new UnicodeEncoding(true, true), 2),
            _ => (plain, 0),
        };
        return (true, encoding.GetString(bytes, skip, bytes.Length - skip), encoding);
    }

    public static MemoryReclaimSetting Load(string? path = null)
    {
        try { return Parse(Read(path).Content); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(MemoryReclaimMode.NotSet, null); }
    }

    /// <summary>Writes the edit (null removes the key); an unchanged file is not rewritten.</summary>
    public static void Write(string? value, string? path = null)
    {
        var file = path ?? WslConfigFile.PathOnDisk;
        if (string.IsNullOrEmpty(file)) throw new InvalidOperationException("No .wslconfig path available.");
        var (exists, content, encoding) = Read(file);
        var updated = Apply(content, value);
        if (exists && updated == content) return;
        if (!exists && value is null) return;
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(file, updated, encoding);
    }

    [GeneratedRegex(@"(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.CultureInvariant)]
    private static partial Regex VersionNumber();

    /// <summary>
    /// The WSL version from <c>wsl --version</c>: the first line, in any UI language ("WSL version: 2.6.3.0",
    /// "WSL-version: …"). The output may arrive as UTF-16 read as UTF-8, so NULs are dropped first. An inbox WSL that
    /// does not know <c>--version</c> prints help text instead, which gives null.
    /// </summary>
    public static Version? ParseWslVersion(string? output)
    {
        if (string.IsNullOrEmpty(output)) return null;
        var first = output.Replace("\0", "").TrimStart('﻿').Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (first is null || VersionNumber().Match(first) is not { Success: true } m) return null;
        int Part(int g) => int.TryParse(m.Groups[g].Value, out var n) ? n : 0;
        return new Version(Part(1), Part(2), Part(3));
    }

    /// <summary>Runs <c>wsl.exe --version</c> hidden (no window) on Windows; null when it fails or is unknown.</summary>
    public static Version? DetectWslVersion()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var psi = Prerequisites.Hidden("wsl.exe", ["--version"]);
        psi.Environment["WSL_UTF8"] = "1";
        var (started, code, output) = Prerequisites.Run(psi, TimeSpan.FromSeconds(10));
        return started && code == 0 ? ParseWslVersion(output) : null;
    }
}
