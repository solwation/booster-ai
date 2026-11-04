using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace BoosterAI;

/// <summary>Owns the GUI lifetime; helper command-line modes do not enter this guard.</summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _listener;
    private readonly object _gate = new();
    private Action? _activate;
    private bool _pending;

    // A stable user identity lets portable and installed copies share one GUI instance.
    internal static string UserKey { get; } = CreateUserKey();

    private static string CreateUserKey()
    {
        string identity;
        if (OperatingSystem.IsWindows())
        {
            using var user = WindowsIdentity.GetCurrent();
            identity = user.User!.Value;
        }
        else identity = Environment.UserName + "\n" + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    internal static string MutexName(string name) => OperatingSystem.IsWindows() ? @"Global\" + name : name;

    private SingleInstance(Mutex mutex, string name)
    {
        _mutex = mutex;
        _pipeName = name;
        // Start listening before constructing Avalonia so a simultaneous launch can already contact us.
        _listener = Task.Run(Listen);
    }

    /// <summary>Null means another GUI received the request. Only the owning thread may dispose the result.</summary>
    internal static SingleInstance? Enter(bool activate, string? testKey = null)
    {
        var name = "BoosterAI.GUI." + (testKey ?? UserKey);
        var mutex = new Mutex(false, MutexName(name));
        try
        {
            if (TryOwn(mutex)) return new SingleInstance(mutex, name);
            try { Notify(name, activate).GetAwaiter().GetResult(); }
            catch (Exception e) when (e is IOException or OperationCanceledException or TimeoutException)
            {
                // If startup crashed, the abandoned mutex permits a fresh owner. Never open a second GUI
                // just because a live owner is slow or its IPC is temporarily unavailable.
                if (TryOwn(mutex)) return new SingleInstance(mutex, name);
                throw new IOException(Loc.Get("err.instanceUnavailable"), e);
            }
            mutex.Dispose();
            return null;
        }
        catch { mutex.Dispose(); throw; }
    }

    private static bool TryOwn(Mutex mutex)
    {
        try { return mutex.WaitOne(0); }
        catch (AbandonedMutexException) { return true; }
    }

    internal void RegisterActivation(Action activate)
    {
        bool pending;
        lock (_gate)
        {
            _activate = activate;
            pending = _pending;
            _pending = false;
        }
        if (pending) activate();
    }

    private void RequestActivation()
    {
        Action? activate;
        lock (_gate)
        {
            activate = _activate;
            if (activate is null) _pending = true;
        }
        activate?.Invoke();
    }

    private async Task Listen()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var clientTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                clientTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                var pid = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(pid, Environment.ProcessId);
                await pipe.WriteAsync(pid, clientTimeout.Token);
                var request = new byte[1];
                await pipe.ReadExactlyAsync(request, clientTimeout.Token);
                if (request[0] == 1) RequestActivation();
                // Acknowledge accepted requests; incomplete or unknown clients cannot keep the server occupied.
                if (request[0] <= 1) await pipe.WriteAsync(new byte[] { 1 }, clientTimeout.Token);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { await Task.Delay(50); }
        }
    }

    private static async Task Notify(string name, bool activate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            try
            {
                using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(timeout.Token);
                var pid = new byte[4];
                await pipe.ReadExactlyAsync(pid, timeout.Token);
                // The menu-launched process can transfer its foreground permission to the existing GUI.
                if (activate && OperatingSystem.IsWindows())
                    AllowSetForegroundWindow(BinaryPrimitives.ReadInt32LittleEndian(pid));
                await pipe.WriteAsync(new byte[] { activate ? (byte)1 : (byte)0 }, timeout.Token);
                var ack = new byte[1];
                await pipe.ReadExactlyAsync(ack, timeout.Token);
                if (ack[0] != 1) throw new IOException("Invalid BoosterAI activation response.");
                return;
            }
            catch (IOException)
            {
                // Simultaneous clients may connect while the previous one is being disposed (Unix pipes).
                // Retry within the shared deadline rather than opening another GUI or losing the request.
                await Task.Delay(50, timeout.Token);
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener.GetAwaiter().GetResult(); }
        finally
        {
            _stop.Dispose();
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
