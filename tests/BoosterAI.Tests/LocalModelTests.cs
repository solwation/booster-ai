using System.Net;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

public class LocalModelTests
{
    private const long Gb = LocalModelAdvice.GiB;
    private static readonly HardwareSnapshot Small = new(8 * Gb, 2 * Gb, "Example", 4, []);
    private static readonly OllamaModel Model = new("coder:7b-q4", "digest-a", 5 * Gb, false);

    [Theory]
    [InlineData("aider", "ollama_chat/coder:7b", "Ollama", "coder:7b")]
    [InlineData("aider", "ollama/coder", "Ollama", "coder")]
    [InlineData("interpreter", "ollama/coder", "Ollama", "coder")]
    [InlineData("openhands", "ollama/coder", "Ollama", "coder")]
    [InlineData("interpreter", "local", "Unknown", "")]
    [InlineData("openhands", "standard", "Unknown", "")]
    [InlineData("aider", "gpt-4o", "Api", "gpt-4o")]
    [InlineData("aider", "custom-model", "Unknown", "custom-model")]
    public void SelectionDoesNotClaimEndpointLocality(string cli, string model, string backend, string exact)
    {
        var selection = ModelSelection.From(cli, model);
        Assert.Equal(backend, selection.Backend.ToString());
        Assert.Equal(exact, selection.Model);
    }

    [Theory]
    [InlineData("coder", "coder:latest")]
    [InlineData("coder:7b-q4", "coder:7b-q4")]
    [InlineData("registry:5000/team/coder", "registry:5000/team/coder:latest")]
    public void ExactNamesRetainQuantizationAndRegistry(string input, string canonical)
    {
        Assert.Equal(canonical, LocalModelAdvice.Canonical(input));
        Assert.Equal(input == Model.Name, LocalModelAdvice.Find(input, [Model]) is not null);
    }

    [Fact]
    public void MissingGpuDoesNotMeanCpuOnlyOrInsufficientMemory()
    {
        Assert.Equal(ModelSuitability.Unknown, LocalModelAdvice.Assess(Model, null, Small, false).Suitability);
        Assert.Equal("ramAvailable", LocalModelAdvice.Assess(Model, null, Small, true).Reason);
        Assert.Equal("ramTotal", LocalModelAdvice.Assess(Model with { Bytes = 8 * Gb }, null, Small, true).Reason);
        Assert.Equal(ModelSuitability.Unknown, LocalModelAdvice.Assess(Model, null, HardwareSnapshot.Empty, true).Suitability);
    }

    [Fact]
    public void MemoryBoundaryIncludesOverheadAndNeverGuessesFromModelName()
    {
        Assert.Equal("ramAvailable", LocalModelAdvice.Assess(Model with { Bytes = 2 * Gb }, null, Small, true).Reason);
        Assert.Equal("cpuUnloaded", LocalModelAdvice.Assess(Model with { Bytes = Gb }, null, Small, true).Reason);
        Assert.Equal(ModelSuitability.Unknown, LocalModelAdvice.Assess(null, null, Small, true).Suitability);
        Assert.Equal(ModelSuitability.Unknown, LocalModelAdvice.Assess(Model with { Bytes = 0 }, null, Small, true).Suitability);
    }

    [Fact]
    public void CloudProxyNeverGetsHostResourceWarningsEvenAtZeroFileSize()
    {
        Assert.Equal(ModelSuitability.Remote, LocalModelAdvice.Assess(Model with { Bytes = 0, Remote = true }, null, Small, true).Suitability);
        var model = Assert.Single(LocalModelAdvice.ParseTags("""{"models":[{"name":"huge:cloud","size":0,"remote_host":"https://ollama.com","remote_model":"huge"}]}"""));
        Assert.True(model.Remote);
        Assert.True(Assert.Single(LocalModelAdvice.ParseTags("""{"models":[{"name":"gpt-oss:120b-cloud","size":0}]}""")).Remote);
    }

    [Fact]
    public void ActualFullGpuOverridesCpuClaimAndLowFreeHostRam()
    {
        var runtime = new OllamaRuntime(Model.Name, Model.Digest, 10 * Gb, 10 * Gb);
        Assert.Equal(ModelSuitability.Fits, LocalModelAdvice.Assess(Model with { Bytes = 9 * Gb }, runtime, Small, true).Suitability);
        Assert.Equal("splitLoaded", LocalModelAdvice.Assess(Model, runtime with { VramBytes = Gb }, Small, false).Reason);
        Assert.Equal("cpuLoaded", LocalModelAdvice.Assess(Model, runtime with { VramBytes = 0 }, Small, false).Reason);
    }

    [Fact]
    public void CpuAndSmallOffloadDoNotAssertPoorPerformanceWithoutMemoryEvidence()
    {
        var tiny = Model with { Bytes = Gb / 10 };
        var runtime = new OllamaRuntime(tiny.Name, tiny.Digest, Gb / 5, 0);
        Assert.Equal(ModelSuitability.Caution, LocalModelAdvice.Assess(tiny, runtime, Small, true).Suitability);
        Assert.Equal(ModelSuitability.Caution, LocalModelAdvice.Assess(tiny, runtime with { VramBytes = runtime.Bytes - 1 }, Small, false).Suitability);
    }

    [Fact]
    public void DifferentDigestAndInvalidRuntimeCannotSupplyPerformanceEvidence()
    {
        var runtime = new OllamaRuntime(Model.Name, "different-quantization", 5 * Gb, 5 * Gb);
        Assert.Equal(ModelSuitability.Unknown, LocalModelAdvice.Assess(Model, runtime, Small, false).Suitability);
        Assert.Equal(ModelSuitability.Unknown, LocalModelAdvice.Assess(Model, runtime with { Digest = Model.Digest, Name = "other:tag" }, Small, false).Suitability);
        Assert.Equal(ModelSuitability.Unknown, LocalModelAdvice.Assess(Model, runtime with { Digest = Model.Digest, VramBytes = 6 * Gb }, Small, false).Suitability);
        Assert.Equal(ModelSuitability.Unknown, LocalModelAdvice.Assess(Model, runtime with { Digest = Model.Digest, VramBytes = -1 }, Small, false).Suitability);
    }

    [Fact]
    public void MissingRuntimeVramStaysUnknown()
    {
        var parsed = Assert.Single(LocalModelAdvice.ParseRunning("""{"models":[{"name":"coder:latest","digest":"a","size":500}]}"""));
        Assert.Equal(-1, parsed.VramBytes);
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434", true)]
    [InlineData("http://[::1]:11434", true)]
    [InlineData("http://localhost:11434/", true)]
    [InlineData("https://example.com", false)]
    [InlineData("http://192.168.0.2:11434", false)]
    [InlineData("http://user:secret@localhost", false)]
    [InlineData("http://localhost/api", false)]
    [InlineData("http://localhost/?redirect=remote", false)]
    public void OnlyExplicitLoopbackRootHttpCanBeInspected(string value, bool valid) =>
        Assert.Equal(valid, LocalOllamaProbe.Endpoint(value) is not null);

    [Fact]
    public void OsParsersDoNotInventMissingRamOrGpu()
    {
        Assert.Equal((8L * Gb, 2L * Gb), LocalHardwareProbe.ParseMemInfo("MemTotal: 8388608 kB\nMemAvailable: 2097152 kB\n"));
        Assert.Null(LocalHardwareProbe.ParseMemInfo("MemTotal: 8388608 kB").Available);
        Assert.Equal(0, LocalHardwareProbe.ParseMemInfo("MemTotal: 8 kB\nMemAvailable: 0 kB").Available);
        Assert.Null(LocalHardwareProbe.ParseMemInfo("MemTotal: 1 kB\nMemAvailable: 2 kB").Available);
        var gpu = Assert.Single(LocalHardwareProbe.ParseNvidia("Example GPU, 24576, 22000\n"));
        Assert.Equal(24 * Gb, gpu.TotalBytes);
        Assert.Empty(LocalHardwareProbe.ParseNvidia("Example GPU, N/A, N/A"));
        Assert.Empty(LocalHardwareProbe.ParseNvidia("Example GPU, 1024, 2048"));
    }

    [Fact]
    public async Task MetadataUsesOnlyReadOnlyEndpointsAndPreservesPsFailure()
    {
        var calls = new List<string>();
        using var client = Client(request =>
        {
            calls.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(request.RequestUri!.AbsolutePath == "/api/tags" ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            { Content = new StringContent("""{"models":[{"name":"coder:latest","digest":"a","size":500}]}""") });
        });
        var result = await LocalOllamaProbe.ReadAsync(client, CancellationToken.None);
        Assert.Single(result.Models);
        Assert.False(result.RuntimeKnown);
        Assert.Equal(["GET /api/tags", "GET /api/ps"], calls);
    }

    [Fact]
    public async Task OversizedMetadataIsRejected()
    {
        using var client = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 1_048_577)) }));
        await Assert.ThrowsAsync<IOException>(() => LocalOllamaProbe.ReadAsync(client, CancellationToken.None));
    }

    [Fact]
    public async Task StalledBodyHonorsTotalDeadline()
    {
        using var client = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LocalOllamaProbe.ReadAsync(client, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(8)));
    }

    [Fact]
    public async Task StalledBodyHonorsDialogCancellation()
    {
        using var client = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) }));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LocalOllamaProbe.ReadAsync(client, cancellation.Token));
    }

    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) =>
        new(new Handler(handle)) { BaseAddress = new Uri("http://localhost:11434/") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    }
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
