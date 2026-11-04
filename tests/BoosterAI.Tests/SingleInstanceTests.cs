using System.IO.Pipes;
using Xunit;

namespace BoosterAI.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void SecondLaunchActivatesOwnerWithoutAnotherInstance()
    {
        using var owner = new Owner();
        var activated = new ManualResetEventSlim();
        owner.Instance.RegisterActivation(activated.Set);
        using var second = SingleInstance.Enter(true, owner.Key);
        Assert.Null(second);
        Assert.True(activated.Wait(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void EarlyLaunchIsQueuedUntilWindowReady()
    {
        using var owner = new Owner();
        using var second = SingleInstance.Enter(true, owner.Key);
        Assert.Null(second);
        var count = 0;
        owner.Instance.RegisterActivation(() => Interlocked.Increment(ref count));
        Assert.Equal(1, count);
    }

    [Fact]
    public void BackgroundLaunchDoesNotRestoreOwner()
    {
        using var owner = new Owner();
        using var second = SingleInstance.Enter(false, owner.Key);
        Assert.Null(second);
        var count = 0;
        owner.Instance.RegisterActivation(() => Interlocked.Increment(ref count));
        Assert.Equal(0, count);
    }

    [Fact]
    public void OwnerExitReleasesGuardAndStopsListener()
    {
        var key = Guid.NewGuid().ToString("N");
        using (var first = new Owner(key)) Assert.NotNull(first.Instance);
        using var next = new Owner(key);
        Assert.NotNull(next.Instance);
    }

    [Fact]
    public void AbandonedOwnerAllowsRecovery()
    {
        var key = Guid.NewGuid().ToString("N");
        Mutex? abandoned = null;
        var thread = new Thread(() =>
        {
            abandoned = new Mutex(false, SingleInstance.MutexName("BoosterAI.GUI." + key));
            Assert.True(abandoned.WaitOne(0));
            // Simulate a crash: the owning thread exits without releasing the mutex.
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        try
        {
            using var recovered = SingleInstance.Enter(false, key);
            Assert.NotNull(recovered);
        }
        finally { abandoned?.Dispose(); }
    }

    [Fact]
    public void SilentClientIsTimedOutAndNextLaunchCanActivate()
    {
        using var owner = new Owner();
        using var silent = new NamedPipeClientStream(".", "BoosterAI.GUI." + owner.Key, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        silent.Connect(3000);
        var pid = new byte[4];
        silent.ReadExactly(pid);
        using var second = SingleInstance.Enter(true, owner.Key);
        Assert.Null(second);
        var activated = false;
        owner.Instance.RegisterActivation(() => activated = true);
        Assert.True(activated);
    }

    [Fact]
    public async Task ConcurrentLaunchesAllNotifyTheSameOwner()
    {
        using var owner = new Owner();
        var count = 0;
        owner.Instance.RegisterActivation(() => Interlocked.Increment(ref count));
        var attempts = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            using var other = SingleInstance.Enter(true, owner.Key);
            Assert.Null(other);
        })).ToArray();
        await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(8, count);
    }

    [Fact]
    public void UnresponsiveLiveOwnerNeverPermitsAnotherGui()
    {
        var key = Guid.NewGuid().ToString("N");
        using var ready = new ManualResetEventSlim();
        using var stop = new ManualResetEventSlim();
        var owner = new Thread(() =>
        {
            using var mutex = new Mutex(false, SingleInstance.MutexName("BoosterAI.GUI." + key));
            mutex.WaitOne();
            try { ready.Set(); stop.Wait(); }
            finally { mutex.ReleaseMutex(); }
        });
        owner.Start();
        try
        {
            Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
            Assert.Throws<IOException>(() => SingleInstance.Enter(true, key));
        }
        finally { stop.Set(); Assert.True(owner.Join(TimeSpan.FromSeconds(5))); }
    }

    [Fact]
    public void UserIdentityIsStableAndDoesNotExposeItsContents()
    {
        Assert.Equal(SingleInstance.UserKey, SingleInstance.UserKey);
        Assert.Equal(64, SingleInstance.UserKey.Length);
        Assert.All(SingleInstance.UserKey, c => Assert.True(char.IsAsciiHexDigit(c)));
    }

    private sealed class Owner : IDisposable
    {
        private readonly ManualResetEventSlim _exit = new();
        private readonly ManualResetEventSlim _ready = new();
        private Exception? _error;
        private readonly Thread _thread;
        public string Key { get; }
        public SingleInstance Instance { get; private set; } = null!;

        public Owner(string? key = null)
        {
            Key = key ?? Guid.NewGuid().ToString("N");

            _thread = new Thread(() =>
            {
                try
                {
                    using var instance = SingleInstance.Enter(false, Key);
                    Instance = instance ?? throw new InvalidOperationException("The test owner did not acquire the guard.");
                    _ready.Set();
                    _exit.Wait();
                }
                catch (Exception e) { _error = e; _ready.Set(); }
            });
            _thread.IsBackground = true;
            _thread.Start();
            Assert.True(_ready.Wait(TimeSpan.FromSeconds(5)));
            if (_error is not null) throw _error;
        }

        public void Dispose()
        {
            _exit.Set();
            Assert.True(_thread.Join(TimeSpan.FromSeconds(5)), "Instance listener did not stop.");
            _exit.Dispose();
            _ready.Dispose();
            if (_error is not null) throw _error;
        }
    }
}
