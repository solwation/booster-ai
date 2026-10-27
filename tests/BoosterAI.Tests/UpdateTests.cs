using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

public class UpdateTests
{
    private static AppVersion V(string s) => AppVersion.Parse(s) ?? throw new ArgumentException(s);

    [Theory]
    [InlineData("0.2.0", 0, 2, 0, "")]
    [InlineData("v0.3.1", 0, 3, 1, "")]
    [InlineData("V1.2", 1, 2, 0, "")]
    [InlineData("1.2.3-beta.1", 1, 2, 3, "beta.1")]
    [InlineData("1.2.3+abcdef", 1, 2, 3, "")]
    [InlineData("0.2.0-test+abc", 0, 2, 0, "test")]
    [InlineData("0.2.0.0", 0, 2, 0, "")]
    public void ParsesVersions(string text, int major, int minor, int patch, string suffix) =>
        Assert.Equal(new AppVersion(major, minor, patch, suffix), AppVersion.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("1.x")]
    [InlineData("1.2.3-")]
    [InlineData("-1.2.3")]
    public void RejectsNonVersions(string? text) => Assert.Null(AppVersion.Parse(text));

    [Theory]
    [InlineData("0.2.1", "0.2.0")]
    [InlineData("0.10.0", "0.9.9")]
    [InlineData("1.0.0", "0.99.99")]
    [InlineData("v0.3.0", "0.2.0")]
    [InlineData("1.0.0", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.10", "1.0.0-rc.9")]
    [InlineData("1.0.0-beta", "1.0.0-alpha")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha")]
    public void ComparesSemantically(string newer, string older)
    {
        Assert.True(V(newer).CompareTo(V(older)) > 0, $"{newer} > {older}");
        Assert.True(V(older).CompareTo(V(newer)) < 0, $"{older} < {newer}");
    }

    [Fact]
    public void EqualVersionsCompareEqual() => Assert.Equal(0, V("v0.2.0").CompareTo(V("0.2.0+abc")));

    [Fact]
    public void CurrentVersionComesFromTheBuild() =>
        // Local builds use <Version> from the csproj (0.2.0 or later); release builds get the tag's version.
        Assert.True(UpdateLogic.Current.CompareTo(V("0.2.0")) >= 0, UpdateLogic.Current.ToString());

    private static ReleaseInfo Release(string tag, bool pre = false) => new(tag, V(tag), pre, UpdateLogic.ReleasesPage, []);

    [Theory]
    [InlineData("v0.3.0", false, null, true)]
    [InlineData("v0.2.0", false, null, false)]       // same version
    [InlineData("v0.1.9", false, null, false)]       // older
    [InlineData("v0.3.0", true, null, false)]        // flagged pre-release
    [InlineData("v0.3.0-rc.1", false, null, false)]  // pre-release by its suffix
    [InlineData("v0.3.0", false, "0.3.0", false)]    // skipped
    [InlineData("v0.3.0", false, "v0.3.0", false)]   // skipped, stored with prefix
    [InlineData("v0.4.0", false, "0.3.0", true)]     // a newer one than the skipped one
    public void OffersOnlyNewerUnskippedReleases(string tag, bool pre, string? skipped, bool offer) =>
        Assert.Equal(offer, UpdateLogic.ShouldOffer(Release(tag, pre), V("0.2.0"), skipped));

    [Fact]
    public void NoReleaseIsNoOffer() => Assert.False(UpdateLogic.ShouldOffer(null, V("0.2.0"), null));

    [Fact]
    public void DueLogicIsEveryFiveMinutes()
    {
        var now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(UpdateLogic.IsDue(null, now));
        Assert.False(UpdateLogic.IsDue(now.AddMinutes(-1), now));
        Assert.False(UpdateLogic.IsDue(now.AddMinutes(-4.9), now));
        Assert.True(UpdateLogic.IsDue(now.AddMinutes(-5), now));
        Assert.True(UpdateLogic.IsDue(now.AddDays(-30), now));
        // A timestamp from the future (clock was wrong) must not block checks forever.
        Assert.True(UpdateLogic.IsDue(now.AddDays(2), now));
        Assert.False(UpdateLogic.IsDue(now.AddMinutes(1), now));
    }

    [Fact]
    public void LaterHidesThatVersionForADay()
    {
        var now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(UpdateLogic.IsSnoozed(V("0.5.0"), null, null, now));
        Assert.True(UpdateLogic.IsSnoozed(V("0.5.0"), V("0.5.0"), now.AddHours(-23), now));
        Assert.False(UpdateLogic.IsSnoozed(V("0.5.0"), V("0.5.0"), now.AddDays(-1), now));
        // A newer release than the one put off shows at once.
        Assert.False(UpdateLogic.IsSnoozed(V("0.6.0"), V("0.5.0"), now.AddMinutes(-10), now));
        // A wrong clock does not hide it forever.
        Assert.False(UpdateLogic.IsSnoozed(V("0.5.0"), V("0.5.0"), now.AddDays(3), now));
    }

    [Fact]
    public void ConfigDefaultsToChecking()
    {
        var cfg = new AppConfig();
        Assert.True(cfg.CheckForUpdates);
        Assert.Null(cfg.SkippedVersion);
        Assert.Null(cfg.LastUpdateCheck);
    }

    private static readonly ReleaseAsset[] Assets =
    [
        new("BoosterAI-0.3.0-linux-x64.tar.gz", "https://x/l"),
        new("BoosterAI-0.3.0-win-x64.zip", "https://x/w"),
        new("SHA256SUMS", "https://x/s"),
        new("BoosterAI-0.2.0-win-x64.zip", "https://x/old"),
    ];

    [Theory]
    [InlineData(true, Architecture.X64, "BoosterAI-0.3.0-win-x64.zip")]
    [InlineData(false, Architecture.X64, "BoosterAI-0.3.0-linux-x64.tar.gz")]
    [InlineData(true, Architecture.Arm64, null)]
    [InlineData(false, Architecture.Arm64, null)]
    public void SelectsTheAssetForThisPlatform(bool windows, Architecture arch, string? expected) =>
        Assert.Equal(expected, UpdateLogic.SelectAsset(Assets, V("0.3.0"), UpdateLogic.RuntimeId(windows, arch))?.Name);

    [Fact]
    public void AssetOfAnotherVersionIsNotChosen() =>
        Assert.Null(UpdateLogic.SelectAsset(Assets, V("0.4.0"), "win-x64"));

    private const string HashA = "8f1c1b1b0e0f6e5b8e4b2a6f3f2b7c1d9a0e3f4b5c6d7e8f9a0b1c2d3e4f5a6b";
    private const string HashB = "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF";

    [Fact]
    public void ParsesSha256Sums()
    {
        var text = $"{HashA}  BoosterAI-0.3.0-linux-x64.tar.gz\r\n{HashB} *BoosterAI-0.3.0-win-x64.zip\n\nnot a line\n{HashA[..60]}  short.zip\n";
        var sums = UpdateLogic.ParseSums(text);
        Assert.Equal(2, sums.Count);
        Assert.Equal(HashA, sums["BoosterAI-0.3.0-linux-x64.tar.gz"]);
        Assert.Equal(HashB.ToLowerInvariant(), sums["BoosterAI-0.3.0-win-x64.zip"]);
    }

    [Fact]
    public void VerifiesChecksums()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "a.bin");
        File.WriteAllText(file, "hello\n");
        // sha256sum of "hello\n".
        const string hello = "5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03";
        Assert.True(UpdateLogic.Verify(file, hello));
        Assert.True(UpdateLogic.Verify(file, hello.ToUpperInvariant()));
        Assert.False(UpdateLogic.Verify(file, HashA));
        File.AppendAllText(file, "x");
        Assert.False(UpdateLogic.Verify(file, hello));
    }

    [Theory]
    [InlineData(true, @"C:\Users\u\AppData\Local\Programs\BoosterAI\BoosterAI.exe", true)]
    [InlineData(true, @"c:\users\u\appdata\local\programs\boosterai\boosterai.exe", true)]
    [InlineData(true, @"C:\src\booster-ai\src\BoosterAI\bin\Debug\net10.0\BoosterAI.exe", false)]
    [InlineData(true, @"C:\Program Files\dotnet\dotnet.exe", false)]
    [InlineData(true, null, false)]
    public void OnlyTheInstalledWindowsCopyUpdatesItself(bool windows, string? path, bool expected)
    {
        if (!OperatingSystem.IsWindows()) return; // Windows paths only normalise on Windows.
        Assert.Equal(expected, UpdateLogic.IsInstalledCopy(path, windows, @"C:\Users\u\AppData\Local", @"C:\Users\u"));
    }

    [Theory]
    [InlineData("/home/u/.local/bin/BoosterAI", true)]
    [InlineData("/home/u/.local/bin/../bin/BoosterAI", true)]
    [InlineData("/home/u/.local/bin/boosterai", false)]
    [InlineData("/home/u/booster-ai/src/BoosterAI/bin/Release/net10.0/BoosterAI", false)]
    [InlineData("/usr/lib/dotnet/dotnet", false)]
    [InlineData("", false)]
    public void OnlyTheInstalledLinuxCopyUpdatesItself(string path, bool expected)
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(expected, UpdateLogic.IsInstalledCopy(path, false, "/unused", "/home/u"));
    }

    [Fact]
    public void ParsesGitHubReleaseJson()
    {
        const string json = """
            {"tag_name":"v0.3.0","prerelease":false,"draft":false,
             "html_url":"https://github.com/solwation/booster-ai/releases/tag/v0.3.0",
             "assets":[
               {"name":"SHA256SUMS","browser_download_url":"https://github.com/solwation/booster-ai/releases/download/v0.3.0/SHA256SUMS"},
               {"name":"BoosterAI-0.3.0-win-x64.zip","browser_download_url":"https://github.com/solwation/booster-ai/releases/download/v0.3.0/BoosterAI-0.3.0-win-x64.zip"},
               {"name":"evil.zip","browser_download_url":"https://example.com/evil.zip"}]}
            """;
        var r = UpdateLogic.ParseRelease(json)!;
        Assert.Equal(V("0.3.0"), r.Version);
        Assert.False(r.PreRelease);
        Assert.Equal("https://github.com/solwation/booster-ai/releases/tag/v0.3.0", r.PageUrl);
        // Downloads only from this repository's release URLs.
        Assert.Equal(["SHA256SUMS", "BoosterAI-0.3.0-win-x64.zip"], r.Assets.Select(a => a.Name));
    }

    [Fact]
    public void DraftOrPreReleaseJsonIsFlagged()
    {
        Assert.True(UpdateLogic.ParseRelease("""{"tag_name":"v0.3.0","prerelease":true}""")!.PreRelease);
        Assert.True(UpdateLogic.ParseRelease("""{"tag_name":"v0.3.0","draft":true}""")!.PreRelease);
        Assert.Null(UpdateLogic.ParseRelease("""{"tag_name":"nightly"}"""));
        Assert.Null(UpdateLogic.ParseRelease("""{"message":"Not Found"}"""));
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "boosterai-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void ExtractsOnlyTheExeFromTheLinuxTarball()
    {
        var dir = TempDir();
        var archive = Path.Combine(dir, "BoosterAI-0.3.0-linux-x64.tar.gz");
        using (var f = File.Create(archive))
        using (var gz = new GZipStream(f, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax))
        {
            void Add(string name, string body)
            {
                var e = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(body)) };
                tar.WriteEntry(e);
            }
            Add("../BoosterAI", "traversal");
            Add("BoosterAI-0.3.0-linux-x64/install-linux.sh", "script");
            Add("BoosterAI-0.3.0-linux-x64/BoosterAI", "new binary");
        }
        var staged = Path.Combine(dir, "BoosterAI.new");
        UpdateClient.ExtractExe(archive, windows: false, staged);
        Assert.Equal("new binary", File.ReadAllText(staged));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(dir)!, "BoosterAI")));
        if (!OperatingSystem.IsWindows()) Assert.True(File.GetUnixFileMode(staged).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public void ExtractsTheExeFromTheWindowsZip()
    {
        var dir = TempDir();
        var archive = Path.Combine(dir, "BoosterAI-0.3.0-win-x64.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            void Add(string name, string body)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(body);
            }
            Add("BoosterAI-0.3.0-win-x64/install-windows.ps1", "script");
            Add("BoosterAI-0.3.0-win-x64/BoosterAI.exe", "new exe");
        }
        var staged = Path.Combine(dir, "BoosterAI.exe.new");
        UpdateClient.ExtractExe(archive, windows: true, staged);
        Assert.Equal("new exe", File.ReadAllText(staged));
    }

    [Fact]
    public void ArchiveWithoutExeFails()
    {
        var dir = TempDir();
        var archive = Path.Combine(dir, "x.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) zip.CreateEntry("BoosterAI-0.3.0-win-x64/readme.txt");
        var staged = Path.Combine(dir, "BoosterAI.exe.new");
        Assert.Throws<UpdateException>(() => UpdateClient.ExtractExe(archive, windows: true, staged));
        Assert.False(File.Exists(staged));
        Assert.False(File.Exists(staged + ".part"));
    }

    [Theory]
    [InlineData("plain", "'plain'", "'plain'")]
    [InlineData("it's", "'it''s'", @"'it'\''s'")]
    [InlineData(@"C:\Users\Åsa O'Neil\x", @"'C:\Users\Åsa O''Neil\x'", @"'C:\Users\Åsa O'\''Neil\x'")]
    public void QuotesPathsForTheHelpers(string raw, string ps, string sh)
    {
        Assert.Equal(ps, UpdateLogic.PsQuote(raw));
        Assert.Equal(sh, UpdateLogic.ShQuote(raw));
    }

    [Fact]
    public void WindowsHelperWaitsSwapsAndRelaunches()
    {
        var s = UpdateLogic.WindowsHelper(4242, @"C:\P\BoosterAI\BoosterAI.exe", @"C:\P\BoosterAI\BoosterAI.exe.new", @"C:\L\update.log", relaunch: true);
        Assert.Contains("Get-Process -Id 4242", s);
        Assert.Contains(@"$target = 'C:\P\BoosterAI\BoosterAI.exe'", s);
        // The old exe is restored when the new one cannot be moved in.
        Assert.Contains("Move-Item -LiteralPath $old -Destination $target -Force", s);
        Assert.Contains("if ($exited) { Start-Process -FilePath $target", s);
        Assert.DoesNotContain("Start-Process", UpdateLogic.WindowsHelper(1, "a", "b", "c", relaunch: false));
    }

    /// <summary>Runs the real bash helper against a fake install: it waits for the process, then swaps the binary.</summary>
    [Fact]
    public void LinuxHelperSwapsAfterTheProcessExits()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = TempDir();
        var target = Path.Combine(dir, "Booster AI's bin");
        var staged = target + ".new";
        var log = Path.Combine(dir, "update.log");
        File.WriteAllText(target, "old");
        File.WriteAllText(staged, "new");
        using var app = Process.Start("sleep", "0.5")!;
        UpdateClient.LaunchHelper(windows: false, app.Id, target, staged, dir, log, relaunch: false);
        // Still running: nothing has been swapped yet.
        Thread.Sleep(150);
        Assert.Equal("old", File.ReadAllText(target));
        for (var i = 0; i < 100 && !(File.Exists(log) && File.ReadAllText(log).Contains("no relaunch")); i++) Thread.Sleep(50);
        Assert.Contains("updated", File.ReadAllText(log));
        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(File.Exists(staged));
        Assert.True(File.GetUnixFileMode(target).HasFlag(UnixFileMode.UserExecute));
    }

    /// <summary>Runs the real PowerShell helper (hidden, no relaunch) against a fake install in %TEMP%.</summary>
    [Fact]
    public void WindowsHelperSwapsAfterTheProcessExits()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = TempDir();
        var target = Path.Combine(dir, "Booster AI's", "BoosterAI.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var staged = target + ".new";
        var log = Path.Combine(dir, "update.log");
        File.WriteAllText(target, "old");
        File.WriteAllText(staged, "new");
        var wait = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-WindowStyle", "Hidden", "-Command", "Start-Sleep -Milliseconds 1500" }) wait.ArgumentList.Add(a);
        using var app = Process.Start(wait)!;
        UpdateClient.LaunchHelper(windows: true, app.Id, target, staged, dir, log, relaunch: false);
        for (var i = 0; i < 400 && !(File.Exists(log) && File.ReadAllText(log).Contains("no relaunch")); i++) Thread.Sleep(50);
        Assert.Contains("updated", File.ReadAllText(log));
        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(File.Exists(staged));
        for (var i = 0; i < 100 && File.Exists(target + ".old"); i++) Thread.Sleep(50);
        Assert.False(File.Exists(target + ".old"));
    }

    /// <summary>
    /// End to end against the real latest release (network, ~40 MB): set BOOSTERAI_UPDATE_E2E to a scratch
    /// directory. Downloads the linux tarball and SHA256SUMS with the app's own client, verifies, stages,
    /// swaps a fake install with the bash helper and runs the new binary's --dry-run. Skipped (passes) otherwise.
    /// </summary>
    [Fact]
    public async Task EndToEndLinuxUpdateFromGitHub()
    {
        var scratch = Environment.GetEnvironmentVariable("BOOSTERAI_UPDATE_E2E");
        if (string.IsNullOrEmpty(scratch) || OperatingSystem.IsWindows()) return;
        var client = new UpdateClient(UpdateClient.CreateHttp());
        var latest = await client.LatestAsync();
        Assert.NotNull(latest);
        // Pretend to be an old build so the real release counts as newer.
        Assert.True(UpdateLogic.ShouldOffer(latest, V("0.0.1"), null));
        var asset = UpdateLogic.SelectAsset(latest!.Assets, latest.Version, "linux-x64");
        Assert.NotNull(asset);

        var install = Path.Combine(scratch, "fake-install", ".local", "bin");
        Directory.CreateDirectory(install);
        var target = Path.Combine(install, "BoosterAI");
        File.WriteAllText(target, "#!/bin/sh\necho old\n");
        var staged = target + ".new";
        var work = Path.Combine(scratch, "download");
        await client.StageAsync(latest, asset!, windows: false, work, staged);
        Assert.Contains("echo old", File.ReadAllText(target));

        var log = Path.Combine(scratch, "update.log");
        File.Delete(log);
        using var app = Process.Start("sleep", "1")!;
        UpdateClient.LaunchHelper(windows: false, app.Id, target, staged, work, log, relaunch: false);
        for (var i = 0; i < 200 && !(File.Exists(log) && File.ReadAllText(log).Contains("no relaunch")); i++) await Task.Delay(50);
        Assert.Contains("updated", File.ReadAllText(log));

        // The installed file is now byte-for-byte the tarball's binary, and it runs.
        string inArchive;
        await using (var f = File.OpenRead(Path.Combine(work, asset!.Name)))
        await using (var gz = new GZipStream(f, CompressionMode.Decompress))
        {
            using var tar = new TarReader(gz);
            TarEntry? e;
            while ((e = tar.GetNextEntry()) is not null && !e.Name.EndsWith("/BoosterAI", StringComparison.Ordinal)) { }
            inArchive = UpdateLogic.Sha256Hex(e!.DataStream!);
        }
        await using (var installed = File.OpenRead(target)) Assert.Equal(inArchive, UpdateLogic.Sha256Hex(installed));
        var cfg = Path.Combine(scratch, "empty.json");
        File.WriteAllText(cfg, "{}");
        var psi = new ProcessStartInfo(target) { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in new[] { "--dry-run", "--config", cfg }) psi.ArgumentList.Add(a);
        using var run = Process.Start(psi)!;
        var output = await run.StandardOutput.ReadToEndAsync();
        await run.WaitForExitAsync();
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("wt.exe", output);
        File.WriteAllText(Path.Combine(scratch, "e2e-result.txt"),
            $"release {latest.Tag}\nasset {asset.Name}\nsha256 {inArchive}\nlog:\n{File.ReadAllText(log)}\ndry-run:\n{output}");
    }
}
