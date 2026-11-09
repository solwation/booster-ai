using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;

namespace BoosterAI;

/// <summary>What a start of the executable does before the normal GUI (#131).</summary>
public enum SetupMode { None, Install, Uninstall }

/// <summary>One Windows shortcut the installer writes.</summary>
public sealed record ShortcutPlan(string Path, string Target, string WorkingDirectory, string Description);

/// <summary>One value under the uninstall key; <see cref="Dword"/> values are REG_DWORD, the rest REG_SZ.</summary>
public sealed record RegistryValue(string Name, object Value)
{
    public bool Dword => Value is int;
}

/// <summary>
/// Every path, shortcut and registry value of a per-user Windows install, computed from the user's
/// folders so it can be printed (<c>--dry-run-install</c>), tested and executed by the same code.
/// Paths always use Windows separators, also when computed elsewhere for a dry run or a test.
/// </summary>
public sealed record InstallPlan(
    string Source,
    string InstallDir,
    string Exe,
    string LegacyDir,
    IReadOnlyList<string> LegacyShortcuts,
    IReadOnlyList<ShortcutPlan> Shortcuts,
    string UninstallKey,
    IReadOnlyList<RegistryValue> RegistryValues,
    string ConfigPath)
{
    /// <summary>True when this process already is the installed copy (no copy, never stop itself).</summary>
    public bool RunsFromInstall => string.Equals(Source, Exe, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The wizard's choices. <see cref="Settings"/> null means the settings step was skipped (nothing changes).</summary>
public sealed record SetupSettings(string Language, string AppTheme, string Llm);

/// <summary>The per-user self-install of the Windows executable: the C# port of tools/install-windows.ps1 plus an uninstall entry.</summary>
public static class Installer
{
    public const string SetupPrefix = "BoosterAI-Setup";
    public const string InstallFlag = "--install";
    public const string UninstallFlag = "--uninstall";
    public const string DryRunFlag = "--dry-run-install";
    public const string AppName = "BoosterAI";
    public const string Publisher = "Solwation";
    public const string AboutUrl = "https://github.com/" + UpdateLogic.Repository;
    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName;
    /// <summary>How the script retries the copy while the old exe is still locked.</summary>
    public const int CopyAttempts = 10;
    public static readonly TimeSpan CopyDelay = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(10);
    /// <summary>A started copy that exits 0 within this time handed over to a running BoosterAI (the single-instance guard).</summary>
    public static readonly TimeSpan StartWait = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Setup mode from the command line and the executable's name: <c>--uninstall</c>, then <c>--install</c> or a
    /// file name starting with BoosterAI-Setup (any case). Every other start, portable or installed, is <see cref="SetupMode.None"/>.
    /// </summary>
    public static SetupMode Detect(string[] args, string? processPath)
    {
        if (args.Contains(UninstallFlag)) return SetupMode.Uninstall;
        if (args.Contains(InstallFlag) || IsSetupName(processPath)) return SetupMode.Install;
        return SetupMode.None;
    }

    public static bool IsSetupName(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath)) return false;
        var name = processPath[(processPath.LastIndexOfAny(['/', '\\']) + 1)..];
        return name.StartsWith(SetupPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Joins Windows path parts with one backslash each.</summary>
    public static string WinJoin(string first, params string[] rest) =>
        rest.Aggregate(first.TrimEnd('\\', '/'), (a, b) => a + "\\" + b.Trim('\\', '/'));

    /// <param name="localAppData">%LOCALAPPDATA%.</param>
    /// <param name="programs">The Start menu's Programs folder.</param>
    /// <param name="desktop">The user's desktop.</param>
    /// <param name="source">The running executable, copied into place.</param>
    /// <param name="configPath">The config the wizard's choices are merged into.</param>
    public static InstallPlan Plan(string localAppData, string programs, string desktop, string source, string configPath, AppVersion version, long exeBytes)
    {
        var dir = WinJoin(localAppData, "Programs", AppName);
        var exe = WinJoin(dir, AppName + ".exe");
        var description = Loc.Get("setup.shortcut.description");
        return new InstallPlan(
            source, dir, exe,
            WinJoin(localAppData, "Programs", LegacyNames.OldName),
            [WinJoin(programs, LegacyNames.OldName + ".lnk"), WinJoin(desktop, LegacyNames.OldName + ".lnk")],
            [new(WinJoin(programs, AppName + ".lnk"), exe, dir, description), new(WinJoin(desktop, AppName + ".lnk"), exe, dir, description)],
            UninstallKeyPath,
            RegistryValues(dir, exe, version, exeBytes),
            configPath);
    }

    /// <summary>The "Apps & features" entry; UninstallString runs the installed copy with --uninstall.</summary>
    public static IReadOnlyList<RegistryValue> RegistryValues(string dir, string exe, AppVersion version, long exeBytes) =>
    [
        new("DisplayName", AppName),
        new("DisplayVersion", version.ToString()),
        new("Publisher", Publisher),
        new("InstallLocation", dir),
        new("DisplayIcon", exe + ",0"),
        new("UninstallString", $"\"{exe}\" {UninstallFlag}"),
        new("URLInfoAbout", AboutUrl),
        new("EstimatedSize", (int)Math.Clamp((exeBytes + 1023) / 1024, 0, int.MaxValue)),
        new("NoModify", 1),
        new("NoRepair", 1),
    ];

    /// <summary>The plan for this user and this executable (real folders on Windows, %VAR% placeholders elsewhere, for dry runs).</summary>
    public static InstallPlan CurrentPlan()
    {
        var windows = OperatingSystem.IsWindows();
        var source = Environment.ProcessPath ?? AppName + ".exe";
        long bytes = 0;
        try { bytes = new FileInfo(source).Length; } catch (Exception) { }
        return Plan(
            windows ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) : "%LOCALAPPDATA%",
            windows ? Environment.GetFolderPath(Environment.SpecialFolder.Programs) : @"%APPDATA%\Microsoft\Windows\Start Menu\Programs",
            windows ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : @"%USERPROFILE%\Desktop",
            source, windows ? ConfigStore.PathOnDisk : @"%APPDATA%\BoosterAI\config.json", UpdateLogic.Current, bytes);
    }

    /// <summary>A neutral plan for screenshots: no real user names or paths.</summary>
    public static InstallPlan SamplePlan() => Plan(@"C:\Users\me\AppData\Local", @"C:\Users\me\AppData\Roaming\Microsoft\Windows\Start Menu\Programs",
        @"C:\Users\me\Desktop", @"C:\Users\me\Downloads\BoosterAI-Setup-" + UpdateLogic.Current + "-win-x64.exe",
        @"C:\Users\me\AppData\Roaming\BoosterAI\config.json", UpdateLogic.Current, 80L * 1024 * 1024);

    /// <summary>
    /// Merges the wizard's choices into the config on disk (null: none yet). Every existing setting and project
    /// stays; the settings step changes only language and appearance, and the picked projects are appended
    /// (a folder already present is not added twice) with the chosen AI CLI. A non-null <paramref name="searchRoot"/>
    /// becomes the "Hitta projekt" base folder (empty: the default). Returns a new object.
    /// </summary>
    public static AppConfig MergeConfig(AppConfig? existing, SetupSettings? settings, IReadOnlyList<FoundProject> projects, string? searchRoot = null)
    {
        var cfg = existing is null ? new AppConfig() : ConfigStore.Copy(existing);
        if (searchRoot is not null) cfg.ProjectSearchRoot = searchRoot.Trim();
        if (settings is not null)
        {
            if (settings.Language is "auto" or "sv" or "en") cfg.Language = settings.Language;
            if (settings.AppTheme is "auto" or "light" or "dark") cfg.AppTheme = settings.AppTheme;
        }
        var llm = settings is not null && Catalog.Llms.Contains(settings.Llm) ? settings.Llm : Catalog.DefaultLlm;
        if (projects.Count == 0) return cfg;
        // The blank row Settings adds for a first project is replaced, as "Hitta projekt" does.
        if (cfg.Tabs is [{ } only] && only.SubTabs.Count == 0 && only.Directory.Trim() is "" or "~" && Loc.IsDefaultTabName(only.Name)) cfg.Tabs.Clear();
        var present = cfg.Tabs.Select(t => ProjectScanner.Key(t.Directory)).ToHashSet();
        foreach (var p in projects)
        {
            var dir = PathMapping.ToTabPath(p.Path);
            if (!present.Add(ProjectScanner.Key(dir))) continue;
            cfg.Tabs.Add(new TabConfig
            {
                Id = Guid.NewGuid().ToString("N"), Name = p.Name, Directory = dir, ColorIndex = cfg.Tabs.Count,
                Color = Catalog.Theme(cfg.ColorTheme).ColorAt(cfg.Tabs.Count), Llm = llm,
            });
        }
        return cfg;
    }

    /// <summary>The CLI most of the existing projects use (the wizard's preselection), else the default.</summary>
    public static string PreferredLlm(AppConfig? cfg) =>
        cfg?.Tabs.Select(t => t.Llm).Where(l => Catalog.Llms.Contains(l) && l != Catalog.ShellOnly)
            .GroupBy(l => l).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? Catalog.DefaultLlm;

    /// <summary>The --dry-run-install text: every install and uninstall action, nothing is changed.</summary>
    public static string DryRun(InstallPlan p)
    {
        var sb = new StringBuilder();
        void L(string s) => sb.Append(s).Append('\n');
        L($"install (file name {SetupPrefix}*.exe or {InstallFlag}; wizard first; per user, no admin rights):");
        L($"  source: {p.Source}");
        L($"  legacy: stop BoosterStart processes whose path is under {p.LegacyDir}\\, wait up to {StopWait.TotalSeconds:0} s, delete {p.LegacyDir} (files only if it stays locked)");
        foreach (var lnk in p.LegacyShortcuts) L($"  legacy: delete {lnk}");
        L($"  create folder: {p.InstallDir}");
        L(p.RunsFromInstall
            ? "  copy: skipped (this process is the installed copy)"
            : $"  stop: BoosterAI processes whose path equals {p.Exe} (never this process), wait up to {StopWait.TotalSeconds:0} s");
        if (!p.RunsFromInstall)
            L($"  copy: {p.Source} -> {p.Exe} ({CopyAttempts} attempts {CopyDelay.TotalMilliseconds:0} ms apart while the file is locked)");
        foreach (var s in p.Shortcuts) L($"  shortcut: {s.Path} -> {s.Target} (working directory {s.WorkingDirectory}, \"{s.Description}\")");
        L($"  registry: HKCU\\{p.UninstallKey}");
        foreach (var v in p.RegistryValues) L($"    {v.Name} = {v.Value} ({(v.Dword ? "REG_DWORD" : "REG_SZ")})");
        L($"  config: {p.ConfigPath}: merge the wizard's language, appearance, found projects and Find projects base folder; other settings and projects are kept (skipped steps change nothing)");
        L($"  start: \"{p.Exe}\" --background (hidden, working directory {p.InstallDir}); exit 0 within {StartWait.TotalMilliseconds:0} ms = a running BoosterAI acknowledged it (single-instance guard), other exit codes fail");
        L($"uninstall ({UninstallFlag}, from \"Apps & features\" or the command line, after confirmation):");
        L($"  stop: BoosterAI processes whose path equals {p.Exe} (never this process)");
        foreach (var s in p.Shortcuts) L($"  delete: {s.Path}");
        L($"  registry: delete HKCU\\{p.UninstallKey}; HKCU\\Software\\Classes\\AppUserModelId\\{Notifier.AppId}; HKCU\\Software\\Classes\\{Notifier.Protocol} when it opens {p.Exe}");
        L($"  delete folder: {p.InstallDir} (by a hidden helper after this process exits when it runs from there)");
        L($"  keep: {p.ConfigPath} and the session data");
        return sb.ToString();
    }

    /// <summary>The helper that removes the install folder once the uninstalling process (pid) has exited.</summary>
    public static string RemoveFolderHelper(int pid, string dir) => $$"""
        $ErrorActionPreference = 'SilentlyContinue'
        $dir = {{UpdateLogic.PsQuote(dir)}}
        $p = Get-Process -Id {{pid}} -ErrorAction SilentlyContinue
        if ($p) { $null = $p.WaitForExit(60000) }
        for ($i = 0; $i -lt 20 -and (Test-Path -LiteralPath $dir); $i++) {
          Remove-Item -LiteralPath $dir -Recurse -Force
          if (Test-Path -LiteralPath $dir) { Start-Sleep -Milliseconds 500 }
        }

        """;

    // ---- Execution (Windows) ----

    /// <summary>Installs per user as the script does, then starts the installed copy in the background.</summary>
    /// <returns>A message for the finish page when the start was handed to an already running BoosterAI, else null.</returns>
    [SupportedOSPlatform("windows")]
    public static string? Install(InstallPlan p, SetupSettings? settings, IReadOnlyList<FoundProject> projects, string? searchRoot, IProgress<string> progress)
    {
        progress.Report(Loc.Get("setup.progress.legacy"));
        RemoveLegacy(p);

        progress.Report(Loc.Get("setup.progress.copy"));
        Directory.CreateDirectory(p.InstallDir);
        if (!p.RunsFromInstall)
        {
            StopCopiesAt(p.Exe);
            CopyWithRetry(p.Source, p.Exe);
        }

        progress.Report(Loc.Get("setup.progress.shortcuts"));
        foreach (var s in p.Shortcuts) WriteShortcut(s);
        WriteUninstallEntry(p);

        progress.Report(Loc.Get("setup.progress.config"));
        WriteConfig(p.ConfigPath, settings, projects, searchRoot);

        progress.Report(Loc.Get("setup.progress.start"));
        return StartInstalled(p);
    }

    /// <summary>Stops processes named <paramref name="name"/> whose executable lies at <paramref name="match"/>; never this process.</summary>
    private static void Stop(string name, Func<string, bool> match)
    {
        var stopped = new List<Process>();
        foreach (var proc in Process.GetProcessesByName(name))
        {
            string? path = null;
            try { if (proc.Id != Environment.ProcessId) path = proc.MainModule?.FileName; }
            catch (Exception) { /* not ours to inspect: left alone, like a process whose Path is unknown */ }
            if (path is not null && match(path))
            {
                try { proc.Kill(entireProcessTree: false); stopped.Add(proc); }
                catch (Exception) { proc.Dispose(); }
            }
            else proc.Dispose();
        }
        foreach (var proc in stopped)
            using (proc)
                try { proc.WaitForExit(StopWait); } catch (Exception) { }
    }

    public static void StopCopiesAt(string exe) =>
        Stop(AppName, path => string.Equals(path, exe, StringComparison.OrdinalIgnoreCase));

    private static void RemoveLegacy(InstallPlan p)
    {
        Stop(LegacyNames.OldName, path => path.StartsWith(p.LegacyDir + "\\", StringComparison.OrdinalIgnoreCase));
        // The folder can stay locked as the working directory of a terminal the old app started.
        try { if (Directory.Exists(p.LegacyDir)) Directory.Delete(p.LegacyDir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (var lnk in p.LegacyShortcuts)
            try { File.Delete(lnk); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The file can stay locked for a moment after the old process exits (AV scan, handle teardown).</summary>
    public static void CopyWithRetry(string source, string dest, int attempts = CopyAttempts, TimeSpan? delay = null)
    {
        for (var i = 1; ; i++)
        {
            try { File.Copy(source, dest, overwrite: true); return; }
            catch (IOException) when (i < attempts) { Thread.Sleep(delay ?? CopyDelay); }
            catch (IOException e) { throw new IOException(Loc.Format("setup.error.locked", dest, attempts), e); }
        }
    }

    /// <summary>
    /// Merges the wizard's choices into the config on disk. A file that exists but cannot be read is kept as
    /// config.json.bak first; nothing is written when nothing changes.
    /// </summary>
    public static void WriteConfig(string path, SetupSettings? settings, IReadOnlyList<FoundProject> projects, string? searchRoot = null)
    {
        AppConfig? existing = null;
        if (File.Exists(path))
        {
            try { existing = ConfigStore.Parse(File.ReadAllText(path)); }
            catch (Exception e) when (e is System.Text.Json.JsonException or IOException) { }
            if (existing is null) File.Copy(path, path + ".bak", overwrite: true);
        }
        var merged = MergeConfig(existing, settings, projects, searchRoot);
        if (existing is not null && Json(existing) == Json(merged)) return;
        if (existing is null && settings is null && projects.Count == 0 && string.IsNullOrWhiteSpace(searchRoot)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Json(merged));
        File.Move(tmp, path, overwrite: true);
    }

    private static string Json(AppConfig cfg) => System.Text.Json.JsonSerializer.Serialize(cfg, ConfigJsonContext.Default.AppConfig);

    [SupportedOSPlatform("windows")]
    private static void WriteUninstallEntry(InstallPlan p)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(p.UninstallKey);
        foreach (var v in p.RegistryValues)
            key.SetValue(v.Name, v.Value, v.Dword ? Microsoft.Win32.RegistryValueKind.DWord : Microsoft.Win32.RegistryValueKind.String);
    }

    /// <summary>
    /// The installed copy keeps "Apps & features" current after a self-update: when the uninstall entry exists
    /// and names another version or location, its version and size are rewritten. Never creates the entry.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void RefreshUninstallEntry()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(UninstallKeyPath, writable: true);
            if (key is null) return;
            var p = CurrentPlan();
            if (!p.RunsFromInstall) return;
            foreach (var v in p.RegistryValues.Where(v => v.Name is "DisplayVersion" or "EstimatedSize"))
                if (!Equals(key.GetValue(v.Name), v.Value))
                    key.SetValue(v.Name, v.Value, v.Dword ? Microsoft.Win32.RegistryValueKind.DWord : Microsoft.Win32.RegistryValueKind.String);
        }
        catch (Exception) { /* cosmetic: the entry still uninstalls */ }
    }

    /// <summary>Starts the installed copy hidden with --background, as the script does.</summary>
    [SupportedOSPlatform("windows")]
    private static string? StartInstalled(InstallPlan p)
    {
        var psi = new ProcessStartInfo(p.Exe) { UseShellExecute = false, WorkingDirectory = p.InstallDir, WindowStyle = ProcessWindowStyle.Hidden };
        psi.ArgumentList.Add("--background");
        using var started = Process.Start(psi) ?? throw new IOException(Loc.Format("setup.error.start", -1));
        if (!started.WaitForExit(StartWait)) return null;
        // Another portable copy can own the user-wide GUI guard. Its acknowledgement is a successful start.
        if (started.ExitCode != 0) throw new IOException(Loc.Format("setup.error.start", started.ExitCode));
        return Loc.Get("setup.done.otherRunning");
    }

    /// <summary>Opens the installed app normally (the finish page's button); a running copy shows its window.</summary>
    [SupportedOSPlatform("windows")]
    public static void Open(InstallPlan p)
    {
        using var _ = Process.Start(new ProcessStartInfo(p.Exe) { UseShellExecute = false, WorkingDirectory = p.InstallDir });
    }

    /// <summary>Removes the install; the config and session data stay. Returns the problems met (empty when all went).</summary>
    [SupportedOSPlatform("windows")]
    public static List<string> Uninstall(InstallPlan p)
    {
        var problems = new List<string>();
        void Try(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { problems.Add(what + ": " + e.Message); }
        }
        Try("stop", () => StopCopiesAt(p.Exe));
        foreach (var s in p.Shortcuts) Try(s.Path, () => File.Delete(s.Path));
        Try(p.UninstallKey, () => Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(p.UninstallKey, throwOnMissingSubKey: false));
        Try("AppUserModelId", () => Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\" + Notifier.AppId, throwOnMissingSubKey: false));
        Try(Notifier.Protocol, () =>
        {
            using (var open = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + Notifier.Protocol + @"\shell\open\command"))
                if (open?.GetValue("") is not string command || !command.Contains(p.Exe, StringComparison.OrdinalIgnoreCase)) return;
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + Notifier.Protocol, throwOnMissingSubKey: false);
        });
        var self = Environment.ProcessPath ?? "";
        if (self.StartsWith(p.InstallDir + "\\", StringComparison.OrdinalIgnoreCase))
            Try(p.InstallDir, () => LaunchRemoveHelper(p.InstallDir));
        else
            Try(p.InstallDir, () => { if (Directory.Exists(p.InstallDir)) Directory.Delete(p.InstallDir, recursive: true); });
        return problems;
    }

    /// <summary>A running exe cannot delete itself: a hidden PowerShell removes the folder after this process exits.</summary>
    [SupportedOSPlatform("windows")]
    private static void LaunchRemoveHelper(string dir)
    {
        var script = Path.Combine(Path.GetTempPath(), $"BoosterAI-uninstall-{Environment.ProcessId}.ps1");
        // BOM: Windows PowerShell 5.1 reads a BOM-less script as ANSI and would garble non-ASCII paths.
        File.WriteAllText(script, RemoveFolderHelper(Environment.ProcessId, dir) + $"Remove-Item -LiteralPath {UpdateLogic.PsQuote(script)} -Force\n",
            new UTF8Encoding(true));
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script })
            psi.ArgumentList.Add(a);
        using var _ = Process.Start(psi) ?? throw new IOException("powershell.exe");
    }

    [SupportedOSPlatform("windows")]
    private static void WriteShortcut(ShortcutPlan s)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            link.SetPath(s.Target);
            link.SetWorkingDirectory(s.WorkingDirectory);
            link.SetDescription(s.Description);
            link.SetIconLocation(s.Target, 0);
            Directory.CreateDirectory(Path.GetDirectoryName(s.Path)!);
            ((IPersistFile)link).Save(s.Path, true);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
