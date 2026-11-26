using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BoosterAI;

/// <summary>The sound effects (#240); each has a bundled WAV named <see cref="SoundCatalog.FileName"/>.</summary>
public enum SoundEffect { Finished, Error, QuotaLow, QuotaBack }

/// <summary>Plays a sound effect without blocking; tests replace it so nothing is ever audible.</summary>
public interface ISoundPlayer
{
    void Play(SoundEffect effect);
}

public static class SoundCatalog
{
    /// <summary>At most one sound per effect within this time (#240).</summary>
    public static readonly TimeSpan Throttle = TimeSpan.FromSeconds(4);

    public static string FileName(SoundEffect e) => e switch
    {
        SoundEffect.Finished => "finished.wav",
        SoundEffect.Error => "error.wav",
        SoundEffect.QuotaLow => "quota-low.wav",
        _ => "quota-back.wav",
    };

    /// <summary>The effect for a name given to <c>--play-sound</c> (file name without .wav), or null.</summary>
    public static SoundEffect? Parse(string name) =>
        Enum.GetValues<SoundEffect>().Cast<SoundEffect?>().FirstOrDefault(e => FileName(e!.Value) == name + ".wav");

    internal static byte[] Bytes(SoundEffect e)
    {
        var asm = typeof(SoundCatalog).Assembly;
        using var s = asm.GetManifestResourceStream("Sounds." + FileName(e))!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}

/// <summary>Silent: the default, so tests and screenshots never make a sound.</summary>
public sealed class NoSoundPlayer : ISoundPlayer
{
    public void Play(SoundEffect effect) { }
}

/// <summary>
/// The real player, no extra dependencies: winmm <c>PlaySound</c> from memory on Windows; <c>paplay</c> or <c>aplay</c> on Linux when
/// on PATH (one short child process that is waited for); otherwise silent.
/// </summary>
public sealed class SystemSoundPlayer : ISoundPlayer
{
    private const uint SndAsync = 0x1, SndNoDefault = 0x2, SndMemory = 0x4;

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    private readonly Dictionary<SoundEffect, IntPtr> _memory = [];
    private readonly Dictionary<SoundEffect, string> _files = [];

    public void Play(SoundEffect effect)
    {
        try
        {
            if (OperatingSystem.IsWindows()) PlayWindows(effect);
            else PlayLinux(effect);
        }
        catch (Exception)
        {
            // A sound is a courtesy; never disturb the app.
        }
    }

    private void PlayWindows(SoundEffect effect)
    {
        IntPtr p;
        lock (_memory)
        {
            if (!_memory.TryGetValue(effect, out p))
            {
                // SND_MEMORY with SND_ASYNC needs the buffer to stay valid while playing: keep it for the process lifetime.
                var bytes = SoundCatalog.Bytes(effect);
                p = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, p, bytes.Length);
                _memory[effect] = p;
            }
        }
        PlaySound(p, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);
    }

    private void PlayLinux(SoundEffect effect)
    {
        var tool = new[] { "paplay", "aplay" }.FirstOrDefault(Launcher.OnPath);
        if (tool is null) return;
        string file;
        lock (_files)
        {
            if (!_files.TryGetValue(effect, out file!) || !File.Exists(file))
            {
                file = Path.Combine(Path.GetTempPath(), $"boosterai-{Environment.UserName}-{SoundCatalog.FileName(effect)}");
                File.WriteAllBytes(file, SoundCatalog.Bytes(effect));
                _files[effect] = file;
            }
        }
        var psi = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        if (tool == "aplay") psi.ArgumentList.Add("-q");
        psi.ArgumentList.Add(file);
        var proc = Process.Start(psi);
        if (proc is null) return;
        // Reap the short process so none is left behind.
        _ = Task.Run(() =>
        {
            try { if (!proc.WaitForExit(10_000)) proc.Kill(entireProcessTree: true); }
            catch (Exception) { /* gone already */ }
            finally { proc.Dispose(); }
        });
    }

    /// <summary>For <c>--play-sound</c>: plays and waits long enough for the asynchronous Windows playback to finish.</summary>
    public void PlayAndWait(SoundEffect effect)
    {
        Play(effect);
        Thread.Sleep(1500);
    }
}
