using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BoosterAI;

/// <summary>A release version: major.minor.patch plus an optional pre-release suffix (build metadata is dropped).</summary>
public sealed record AppVersion(int Major, int Minor, int Patch, string Suffix) : IComparable<AppVersion>
{
    /// <summary>Parses "1.2.3", "v1.2.3", "1.2", "1.2.3-beta.1" and "1.2.3+abc"; null when it is not a version.</summary>
    public static AppVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        var dash = s.IndexOf('-');
        var suffix = dash >= 0 ? s[(dash + 1)..] : "";
        var core = (dash >= 0 ? s[..dash] : s).Split('.');
        if (core.Length is < 1 or > 4 || (dash >= 0 && suffix == "")) return null;
        var n = new int[3];
        for (var i = 0; i < core.Length; i++)
        {
            if (!int.TryParse(core[i], System.Globalization.NumberStyles.None, null, out var v)) return null;
            // A fourth .NET-style revision part (0.2.0.0) is accepted and ignored.
            if (i < 3) n[i] = v;
        }
        return new AppVersion(n[0], n[1], n[2], suffix);
    }

    public bool IsPreRelease => Suffix != "";

    /// <summary>SemVer precedence: numbers first; a pre-release sorts before its release; suffix parts compare numerically when both are numbers.</summary>
    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (Suffix == other.Suffix) return 0;
        if (Suffix == "") return 1;
        if (other.Suffix == "") return -1;
        var a = Suffix.Split('.');
        var b = other.Suffix.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var an = int.TryParse(a[i], out var ai);
            var bn = int.TryParse(b[i], out var bi);
            c = an && bn ? ai.CompareTo(bi) : an ? -1 : bn ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return Math.Sign(c);
        }
        return a.Length.CompareTo(b.Length);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Suffix == "" ? "" : "-" + Suffix);
}

public sealed record ReleaseAsset(string Name, string Url);

/// <summary>What GitHub's releases/latest returned, reduced to what the update needs.</summary>
public sealed record ReleaseInfo(string Tag, AppVersion Version, bool PreRelease, string PageUrl, IReadOnlyList<ReleaseAsset> Assets,
    string? SummarySv = null, string? SummaryEn = null)
{
    /// <summary>The one-line summary in the given language ("sv"/"en"), falling back to the other; null when the release has none.</summary>
    public string? SummaryFor(string language) => language == "sv" ? SummarySv ?? SummaryEn : SummaryEn ?? SummarySv;
}

/// <summary>The pure decisions of the update check, separate from network and UI so they can be tested.</summary>
public static class UpdateLogic
{
    public const string Repository = "solwation/booster-ai";
    public const string LatestApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases/latest";
    public const string SumsName = "SHA256SUMS";
    /// <summary>How often the main window checks while it is active (GitHub allows 60 unauthenticated calls an hour).</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    /// <summary>How long "Senare" hides the offered version; a newer one shows at once.</summary>
    public static readonly TimeSpan Snooze = TimeSpan.FromDays(1);

    /// <summary>The running build's version (release builds get it from the tag via -p:Version).</summary>
    public static AppVersion Current { get; } =
        AppVersion.Parse(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? AppVersion.Parse(Assembly.GetExecutingAssembly().GetName().Version?.ToString()) ?? new AppVersion(0, 0, 0, "");

    /// <summary>A check is due when none has been made, the last one is an Interval old, or the clock went backwards.</summary>
    public static bool IsDue(DateTime? lastUtc, DateTime nowUtc) =>
        lastUtc is not { } last || nowUtc - last >= Interval || last > nowUtc + TimeSpan.FromMinutes(5);

    /// <summary>Whether "Senare" on <paramref name="laterVersion"/> at <paramref name="laterUtc"/> still hides <paramref name="latest"/>.</summary>
    public static bool IsSnoozed(AppVersion latest, AppVersion? laterVersion, DateTime? laterUtc, DateTime nowUtc) =>
        laterVersion is not null && laterUtc is { } at && latest.CompareTo(laterVersion) == 0
        && nowUtc - at < Snooze && at <= nowUtc + TimeSpan.FromMinutes(5);

    /// <summary>Whether to offer <paramref name="latest"/>: a newer full release that the user has not skipped.</summary>
    public static bool ShouldOffer(ReleaseInfo? latest, AppVersion current, string? skipped) =>
        latest is { PreRelease: false } && !latest.Version.IsPreRelease && latest.Version.CompareTo(current) > 0
        && !(AppVersion.Parse(skipped) is { } s && s.CompareTo(latest.Version) == 0);

    /// <summary>The runtime id of a published build for this OS and CPU, or null where none is published.</summary>
    public static string? RuntimeId(bool windows, Architecture arch) => arch switch
    {
        Architecture.X64 => windows ? "win-x64" : "linux-x64",
        _ => null,
    };

    /// <summary>The archive for <paramref name="rid"/> as tools/package.sh names it (BoosterAI-&lt;ver&gt;-&lt;rid&gt;.zip|.tar.gz).</summary>
    public static ReleaseAsset? SelectAsset(IEnumerable<ReleaseAsset> assets, AppVersion version, string? rid)
    {
        if (rid is null) return null;
        var ext = rid.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.gz";
        return assets.FirstOrDefault(a => a.Name == $"BoosterAI-{version}-{rid}{ext}");
    }

    /// <summary>Parses `sha256sum` output ("hash  name" or "hash *name") into name → lower-case hash.</summary>
    public static IReadOnlyDictionary<string, string> ParseSums(string text)
    {
        var sums = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var space = line.IndexOf(' ');
            if (space != 64) continue;
            var hash = line[..64];
            if (!hash.All(Uri.IsHexDigit)) continue;
            var name = line[(space + 1)..].TrimStart(' ');
            if (name.StartsWith('*')) name = name[1..];
            if (name != "") sums[name] = hash.ToLowerInvariant();
        }
        return sums;
    }

    public static string Sha256Hex(Stream s) => Convert.ToHexStringLower(SHA256.HashData(s));

    /// <summary>True when the file's SHA-256 equals <paramref name="expected"/> (hex, any case).</summary>
    public static bool Verify(string file, string expected)
    {
        using var s = File.OpenRead(file);
        return string.Equals(Sha256Hex(s), expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Where tools/install-*.(ps1|sh) put the executable; only a copy there updates itself.</summary>
    public static string InstallPath(bool windows, string localAppData, string home) => windows
        ? Path.Combine(localAppData, "Programs", "BoosterAI", "BoosterAI.exe")
        : Path.Combine(home, ".local", "bin", "BoosterAI");

    public static bool IsInstalledCopy(string? processPath, bool windows, string localAppData, string home)
    {
        if (string.IsNullOrEmpty(processPath)) return false;
        var expected = InstallPath(windows, localAppData, home);
        try { processPath = Path.GetFullPath(processPath); } catch (Exception) { return false; }
        return string.Equals(processPath, expected, windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>The executable's name inside the archive (in its BoosterAI-&lt;ver&gt;-&lt;rid&gt;/ folder).</summary>
    public static string ExeName(bool windows) => windows ? "BoosterAI.exe" : "BoosterAI";

    /// <summary>Reads GitHub's release JSON; null when it is not one.</summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("tag_name", out var tagEl)) return null;
        var tag = tagEl.GetString() ?? "";
        if (AppVersion.Parse(tag) is not { } version) return null;
        bool Flag(string n) => r.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.True;
        var page = r.TryGetProperty("html_url", out var h) && h.GetString() is { } u && u.StartsWith("https://github.com/", StringComparison.Ordinal) ? u : ReleasesPage;
        var assets = new List<ReleaseAsset>();
        if (r.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var a in list.EnumerateArray())
                if (a.TryGetProperty("name", out var n) && a.TryGetProperty("browser_download_url", out var d)
                    && n.GetString() is { } name && d.GetString() is { } url
                    && url.StartsWith("https://github.com/" + Repository + "/releases/download/", StringComparison.Ordinal))
                    assets.Add(new ReleaseAsset(name, url));
        var body = r.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
        return new ReleaseInfo(tag, version, Flag("prerelease") || Flag("draft"), page, assets,
            ParseSummary(body, "sv"), ParseSummary(body, "en"));
    }

    /// <summary>Longest summary kept; the release workflow writes about 100 characters.</summary>
    public const int MaxSummary = 200;

    private static readonly System.Text.RegularExpressions.Regex SummaryMarker =
        new(@"<!--\s*summary:(?<lang>[a-z]{2})\s*:(?<text>.*?)-->", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Reads the <c>&lt;!-- summary:sv: … --&gt;</c> marker of a language from a release body. The text is
    /// stripped of tags and control characters, whitespace is collapsed and it is cut at <see cref="MaxSummary"/>
    /// with an ellipsis. Null when the marker is missing or nothing readable remains.
    /// </summary>
    public static string? ParseSummary(string? body, string language)
    {
        if (string.IsNullOrEmpty(body)) return null;
        foreach (System.Text.RegularExpressions.Match m in SummaryMarker.Matches(body))
        {
            if (!m.Groups["lang"].Value.Equals(language, StringComparison.OrdinalIgnoreCase)) continue;
            var t = System.Text.RegularExpressions.Regex.Replace(m.Groups["text"].Value, "<[^>]*>", " ");
            t = new string([.. t.Select(c => char.IsControl(c) ? ' ' : c)]);
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
            if (t.Length == 0) continue;
            return t.Length <= MaxSummary ? t : t[..(MaxSummary - 1)].TrimEnd() + "…";
        }
        return null;
    }

    /// <summary>
    /// The "Show more" target: GitHub's compare page from the installed version to the new tag, or the
    /// release page when the installed version is unknown (0.0.0) or not older than the release.
    /// </summary>
    public static string MoreUrl(ReleaseInfo release, AppVersion current) =>
        current.CompareTo(new AppVersion(0, 0, 0, "")) > 0 && current.CompareTo(release.Version) < 0
            ? $"https://github.com/{Repository}/compare/v{current}...{release.Tag}"
            : release.PageUrl;

    /// <summary>Single-quoted PowerShell literal.</summary>
    public static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>Single-quoted bash literal.</summary>
    public static string ShQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>
    /// Windows helper, run hidden by powershell.exe: waits for BoosterAI (pid) to exit, moves the old exe
    /// aside, moves the verified new one in (restoring the old one if that fails), starts BoosterAI again.
    /// </summary>
    public static string WindowsHelper(int pid, string target, string staged, string log, bool relaunch) => $$"""
        $ErrorActionPreference = 'Stop'
        $target = {{PsQuote(target)}}
        $staged = {{PsQuote(staged)}}
        $log = {{PsQuote(log)}}
        $old = $target + '.old'
        $exited = $true
        function Log($m) { try { Add-Content -LiteralPath $log -Value ((Get-Date -Format o) + ' ' + $m) } catch {} }
        try {
          $p = Get-Process -Id {{pid}} -ErrorAction SilentlyContinue
          if ($p -and -not $p.WaitForExit(120000)) { $exited = $false; throw 'BoosterAI did not exit' }
          $moved = $false
          for ($i = 0; $i -lt 40 -and -not $moved; $i++) {
            try {
              if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force }
              Move-Item -LiteralPath $target -Destination $old -Force
              $moved = $true
            } catch { Start-Sleep -Milliseconds 500 }
          }
          if (-not $moved) { throw ('could not move ' + $target) }
          # Same bounded retry for the second move (AV scanners / indexers can hold the file briefly);
          # only when it still fails is the old exe restored.
          $placed = $false
          for ($i = 0; $i -lt 10 -and -not $placed; $i++) {
            try { Move-Item -LiteralPath $staged -Destination $target -Force; $placed = $true }
            catch { Start-Sleep -Milliseconds 500 }
          }
          if (-not $placed) { Move-Item -LiteralPath $old -Destination $target -Force; throw ('could not move ' + $staged) }
          Log 'updated'
        } catch {
          Log ('failed: ' + $_)
          if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged -Force -ErrorAction SilentlyContinue }
        }
        {{(relaunch ? "if ($exited) { Start-Process -FilePath $target -WorkingDirectory (Split-Path -LiteralPath $target) }" : "Log 'no relaunch'")}}
        Start-Sleep -Seconds 3
        Remove-Item -LiteralPath $old -Force -ErrorAction SilentlyContinue

        """;

    /// <summary>
    /// Linux helper, run detached by bash: waits for BoosterAI (pid) to exit, renames the verified new
    /// binary over the old one (atomic in one directory), starts BoosterAI again.
    /// </summary>
    public static string LinuxHelper(int pid, string target, string staged, string log, bool relaunch) => $$"""
        #!/usr/bin/env bash
        set -u
        target={{ShQuote(target)}}
        staged={{ShQuote(staged)}}
        log={{ShQuote(log)}}
        say() { printf '%s %s\n' "$(date -Iseconds)" "$1" >> "$log" 2>/dev/null || true; }
        for _ in $(seq 1 1200); do kill -0 {{pid}} 2>/dev/null || break; sleep 0.1; done
        if kill -0 {{pid}} 2>/dev/null; then
          say 'failed: BoosterAI did not exit'; rm -f -- "$staged"; exit 1
        fi
        if chmod 755 -- "$staged" && mv -f -- "$staged" "$target"; then say 'updated'; else say 'failed: could not replace binary'; rm -f -- "$staged"; fi
        {{(relaunch ? "setsid \"$target\" </dev/null >/dev/null 2>&1 &" : "say 'no relaunch'")}}

        """;
}

/// <summary>Talks to GitHub: the background check and the verified download.</summary>
public sealed class UpdateClient(HttpClient http)
{
    public static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"BoosterAI/{UpdateLogic.Current}");
        return http;
    }

    /// <summary>The latest full release, or null on any network or format problem (the check is silent).</summary>
    public async Task<ReleaseInfo?> LatestAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            using var req = new HttpRequestMessage(HttpMethod.Get, UpdateLogic.LatestApi);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            return UpdateLogic.ParseRelease(await resp.Content.ReadAsStringAsync(cts.Token));
        }
        catch (Exception) { return null; }
    }

    private async Task DownloadAsync(string url, string file, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(10));
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(cts.Token);
        await using var dst = File.Create(file);
        await src.CopyToAsync(dst, cts.Token);
    }

    /// <summary>
    /// Downloads <paramref name="asset"/> and SHA256SUMS into <paramref name="workDir"/>, verifies the archive,
    /// and writes only the executable to <paramref name="staged"/> (next to the installed one, so the final
    /// swap is a rename). Throws <see cref="UpdateException"/> with a user-facing message.
    /// </summary>
    public async Task StageAsync(ReleaseInfo release, ReleaseAsset asset, bool windows, string workDir, string staged, CancellationToken ct = default)
    {
        Directory.CreateDirectory(workDir);
        var sumsAsset = release.Assets.FirstOrDefault(a => a.Name == UpdateLogic.SumsName)
            ?? throw new UpdateException(Loc.Format("update.error.noSums", asset.Name));
        var sumsFile = Path.Combine(workDir, UpdateLogic.SumsName);
        var archive = Path.Combine(workDir, asset.Name);
        await DownloadAsync(sumsAsset.Url, sumsFile, ct);
        if (!UpdateLogic.ParseSums(await File.ReadAllTextAsync(sumsFile, ct)).TryGetValue(asset.Name, out var expected))
            throw new UpdateException(Loc.Format("update.error.noSums", asset.Name));
        await DownloadAsync(asset.Url, archive, ct);
        if (!UpdateLogic.Verify(archive, expected))
            throw new UpdateException(Loc.Format("update.error.checksum", asset.Name));
        ExtractExe(archive, windows, staged);
    }

    /// <summary>Copies the single executable entry out of the archive; other entries are never written.</summary>
    public static void ExtractExe(string archive, bool windows, string staged)
    {
        var exe = UpdateLogic.ExeName(windows);
        var tmp = staged + ".part";
        var found = false;
        try
        {
            if (windows)
            {
                using var zip = ZipFile.OpenRead(archive);
                var entry = zip.Entries.FirstOrDefault(e => IsExeEntry(e.FullName, exe));
                if (entry is not null)
                {
                    using var src = entry.Open();
                    using var dst = File.Create(tmp);
                    src.CopyTo(dst);
                    found = true;
                }
            }
            else
            {
                using var file = File.OpenRead(archive);
                using var gz = new GZipStream(file, CompressionMode.Decompress);
                using var tar = new TarReader(gz);
                while (tar.GetNextEntry() is { } entry)
                {
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null
                        || !IsExeEntry(entry.Name, exe)) continue;
                    using (var dst = File.Create(tmp)) entry.DataStream.CopyTo(dst);
                    found = true;
                    break;
                }
            }
            if (!found) throw new UpdateException(Loc.Format("update.error.noExe", exe, Path.GetFileName(archive)));
            if (!windows && !OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, (UnixFileMode)0b111_101_101);
            File.Move(tmp, staged, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>"BoosterAI-x.y.z-rid/BoosterAI(.exe)": the exe one folder down, as tools/package.sh lays it out.</summary>
    private static bool IsExeEntry(string name, string exe)
    {
        var parts = name.Replace('\\', '/').TrimStart('.', '/').Split('/');
        return parts.Length == 2 && parts[0].StartsWith("BoosterAI-", StringComparison.Ordinal) && parts[1] == exe;
    }

    /// <summary>Writes the helper script and starts it without a window; it outlives this process.</summary>
    public static void LaunchHelper(bool windows, int pid, string target, string staged, string workDir, string log, bool relaunch = true)
    {
        Directory.CreateDirectory(workDir);
        ProcessStartInfo psi;
        if (windows)
        {
            var script = Path.Combine(workDir, "update.ps1");
            // BOM: Windows PowerShell 5.1 reads a BOM-less script as ANSI and would garble non-ASCII paths.
            File.WriteAllText(script, UpdateLogic.WindowsHelper(pid, target, staged, log, relaunch), new UTF8Encoding(true));
            psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script })
                psi.ArgumentList.Add(a);
        }
        else
        {
            var script = Path.Combine(workDir, "update.sh");
            File.WriteAllText(script, UpdateLogic.LinuxHelper(pid, target, staged, log, relaunch));
            psi = new ProcessStartInfo("setsid") { UseShellExecute = false };
            psi.ArgumentList.Add("bash");
            psi.ArgumentList.Add(script);
        }
        psi.WorkingDirectory = workDir;
        using var _ = Process.Start(psi) ?? throw new UpdateException(Loc.Format("update.error.helper", psi.FileName));
    }
}

/// <summary>An update failure whose message is shown to the user as is.</summary>
public sealed class UpdateException(string message) : Exception(message);
