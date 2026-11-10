using System.Net;
using System.Text;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

public class DownloadableModelTests
{
    private const long Gb = LocalModelAdvice.GiB;
    private static DownloadableModel Model => DownloadableModels.All[1];
    private static SettingsHardwareAdviceState Hardware(HardwareSnapshot? snapshot = null)
    {
        var state = new SettingsHardwareAdviceState(_ => throw new InvalidOperationException("Unexpected live hardware read"));
        state.SetSnapshot(snapshot ?? new(64 * Gb, 40 * Gb, "Example CPU", 16, []));
        return state;
    }

    [Fact]
    public void UnmeasuredHardwareDoesNotInventCapacityAndPlentyOfRamDoesNotRecommendHugeCpuModel()
    {
        Assert.Null(DownloadableModels.Recommend(HardwareSnapshot.Empty));
        Assert.Equal(PlanningFit.Unknown, DownloadableModels.Assess(Model, HardwareSnapshot.Empty));
        var hardware = new HardwareSnapshot(64 * Gb, 40 * Gb, "Example CPU", 16, []);
        Assert.Equal("qwen3.5:4b-q4_K_M", DownloadableModels.Recommend(hardware)!.Id);
        Assert.Equal(PlanningFit.CpuCandidate, DownloadableModels.Assess(Model, hardware));
        Assert.Equal(PlanningFit.Limited, DownloadableModels.Assess(Model, hardware with { AvailableRam = 0 }));
    }

    [Fact]
    public void PlanningUsesAllowanceFreeMemoryAndOneGpuRatherThanCombinedOrTotalVram()
    {
        var bytes = Model.PlanningBytes;
        Assert.True(bytes > Model.DownloadBytes);
        var hardware = new HardwareSnapshot(32 * Gb, 2 * Gb, "Example", 8,
            [new("GPU A", bytes * 2, bytes - 1), new("GPU B", bytes * 2, bytes - 1)]);
        Assert.Equal(PlanningFit.Limited, DownloadableModels.Assess(Model, hardware));
        Assert.Equal(PlanningFit.GpuCandidate, DownloadableModels.Assess(Model, hardware with { Gpus = [new("GPU A", bytes * 2, bytes)] }));
        Assert.Equal(PlanningFit.CpuCandidate, DownloadableModels.Assess(Model, hardware with { Gpus = [], AvailableRam = bytes }));
        Assert.Equal(PlanningFit.Limited, DownloadableModels.Assess(Model, hardware with { Gpus = [], AvailableRam = bytes - 1 }));
        Assert.Equal(PlanningFit.Unknown, DownloadableModels.Assess(Model, hardware with { AvailableRam = null }));
    }

    [Fact]
    public async Task PullUsesReviewedExactModelAndReportsLayerProgressBeforeExplicitSuccess()
    {
        var calls = new List<ModelDownloadProgress>();
        using var client = Client(async (request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/pull", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains(Model.Id, body);
            Assert.Contains("\"stream\":true", body);
            return Response("{\"status\":\"pulling layer\",\"total\":100,\"completed\":42}\n{\"status\":\"success\"}\n");
        });
        await OllamaModelDownload.PullAsync(client, Model, calls.Add, CancellationToken.None);
        Assert.Equal(42, calls[0].Percent);
        Assert.Equal("success", calls[^1].Status);
        await Assert.ThrowsAsync<ArgumentException>(() => OllamaModelDownload.PullAsync(client, Model with { Id = "other:cloud" }, calls.Add, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => OllamaModelDownload.PullAsync(new Uri("https://example.com"), Model, calls.Add, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"status\":\"pulling\"}\n")]
    [InlineData("{\"error\":\"not enough disk space\"}\n")]
    [InlineData("{\"status\":42}\n")]
    [InlineData("{\"status\":\"success\"")]
    public async Task ErrorMalformedAndAbruptStreamsCannotBecomeSuccess(string body)
    {
        using var client = Client((_, _) => Task.FromResult(Response(body)));
        var error = await Record.ExceptionAsync(() => OllamaModelDownload.PullAsync(client, Model, _ => { }, CancellationToken.None));
        Assert.True(error is IOException or System.Text.Json.JsonException, error?.ToString() ?? "Unexpected success");
    }

    [Fact]
    public async Task LargeStatusLinesAndHttpErrorsAreRejected()
    {
        using var large = Client((_, _) => Task.FromResult(Response(new string('x', 16_385))));
        await Assert.ThrowsAsync<IOException>(() => OllamaModelDownload.PullAsync(large, Model, _ => { }, CancellationToken.None));
        using var bad = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Content = new StringContent("redirect") }));
        await Assert.ThrowsAsync<HttpRequestException>(() => OllamaModelDownload.PullAsync(bad, Model, _ => { }, CancellationToken.None));
    }

    [Fact]
    public async Task StalledBodyCancelsAndHonorsInactivityTimeout()
    {
        using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) }));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OllamaModelDownload.PullAsync(client, Model, _ => { }, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OllamaModelDownload.PullAsync(client, Model, _ => { }, CancellationToken.None, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public async Task DownloadRequiresCheckedServerAndExplicitConsentThenRefreshesExactInstalledTag()
    {
        var reads = 0; var pulls = 0;
        var vm = new LocalModelsViewModel(Hardware(), (_, _) => Task.FromResult(new OllamaSnapshot(
            ++reads == 1 ? [] : [new(Model.Id, "sample-digest", Model.DownloadBytes, false)], [], true)),
            (_, model, progress, _) => { pulls++; Assert.Equal(Model.Id, model.Id); progress(new("pulling", 42, 100)); return Task.CompletedTask; });
        await vm.DownloadAsync(CancellationToken.None);
        Assert.Equal(0, pulls);
        await vm.RefreshAsync(CancellationToken.None);
        Assert.False(vm.CanDownload);
        vm.Confirmed = true;
        Assert.True(vm.CanDownload);
        await vm.DownloadAsync(CancellationToken.None);
        Assert.Equal(1, pulls);
        Assert.Equal(2, reads);
        Assert.True(vm.Selected.Installed);
        Assert.False(vm.CanDownload);
        vm.Endpoint = "http://127.0.0.1:11435";
        Assert.False(vm.Confirmed);
        Assert.False(vm.CanDownload);
        Assert.False(vm.Selected.Installed);
    }

    [Fact]
    public async Task BusyOperationFreezesEndpointModelAndConsentAndCancelReleasesIt()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new LocalModelsViewModel(Hardware(), (_, _) => Task.FromResult(new OllamaSnapshot([], [], true)),
            async (_, _, _, cancellation) => { started.SetResult(); await Task.Delay(Timeout.Infinite, cancellation); });
        await vm.RefreshAsync(CancellationToken.None);
        vm.Confirmed = true;
        var selected = vm.Selected; var endpoint = vm.Endpoint;
        var operation = vm.DownloadAsync(CancellationToken.None);
        await started.Task;
        vm.Endpoint = "http://localhost:1";
        vm.Selected = vm.Models[^1]; vm.Confirmed = false;
        Assert.Equal(endpoint, vm.Endpoint); Assert.Same(selected, vm.Selected); Assert.True(vm.Confirmed);
        Assert.False(vm.CanDownload); Assert.True(vm.CanCancel);
        await vm.DownloadAsync(CancellationToken.None);
        vm.Cancel();
        await operation.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(vm.Busy); Assert.True(vm.CanDownload);
        Assert.Contains(Loc.Get("models.downloadCancelled"), vm.DownloadStatus);
    }

    [Fact]
    public async Task RemoteInventoryDoesNotCountAsLocalInstallationAndFailedDownloadDoesNotClaimSuccess()
    {
        var vm = new LocalModelsViewModel(Hardware(), (_, _) => Task.FromResult(new OllamaSnapshot([new(Model.Id, "digest", 0, true)], [], true)),
            (_, _, _, _) => throw new IOException("disk full"));
        await vm.RefreshAsync(CancellationToken.None); vm.Confirmed = true;
        Assert.False(vm.Selected.Installed); Assert.True(vm.CanDownload);
        await vm.DownloadAsync(CancellationToken.None);
        Assert.Equal(Loc.Get("models.downloadFailed"), vm.DownloadStatus);
        Assert.False(vm.Busy);
    }

    [Fact]
    public async Task FailedInventoryRefreshInvalidatesOldInstallationEvidence()
    {
        var reads = 0;
        var vm = new LocalModelsViewModel(Hardware(), (_, _) => ++reads == 1
            ? Task.FromResult(new OllamaSnapshot([new(Model.Id, "sample", Model.DownloadBytes, false)], [], true))
            : throw new HttpRequestException("offline"));
        await vm.RefreshAsync(CancellationToken.None);
        Assert.True(vm.Selected.Installed);
        await vm.RefreshAsync(CancellationToken.None);
        Assert.False(vm.Selected.Installed);
        Assert.Equal(Loc.Get("models.unreachable"), vm.Status);
        vm.Confirmed = true;
        Assert.False(vm.CanDownload);
    }

    [Fact]
    public async Task SuccessfulPullAndFailedInventoryAreReportedSeparately()
    {
        var reads = 0;
        var vm = new LocalModelsViewModel(Hardware(), (_, _) => ++reads == 1
            ? Task.FromResult(new OllamaSnapshot([], [], true))
            : throw new HttpRequestException("offline"), (_, _, _, _) => Task.CompletedTask);
        await vm.RefreshAsync(CancellationToken.None); vm.Confirmed = true;
        await vm.DownloadAsync(CancellationToken.None);
        Assert.Equal(Loc.Format("models.downloaded", Model.Id), vm.DownloadStatus);
        Assert.Equal(Loc.Get("models.refreshAfterDownload"), vm.Status);
        Assert.False(vm.CanDownload);
    }

    [Fact]
    public async Task StatusRecordsCanSpanReadBuffersAndProgressIsBounded()
    {
        using var client = Client((_, _) => Task.FromResult(Response("{\"status\":\"" + new string('x', 5_000)
            + "\",\"total\":1,\"completed\":99}\n{\"status\":\"success\"}\n")));
        var updates = new List<ModelDownloadProgress>();
        await OllamaModelDownload.PullAsync(client, Model, updates.Add, CancellationToken.None);
        Assert.Equal(100, updates[0].Percent);
        Assert.Equal(5_000, updates[0].Status.Length);
    }

    [Fact]
    public void ClosingOneManagerUnsubscribesWithoutStoppingTheReopenedManagersHardwareUpdates()
    {
        var hardware = Hardware();
        var closed = new LocalModelsViewModel(hardware);
        using var reopened = new LocalModelsViewModel(hardware);
        var oldUpdates = 0; var newUpdates = 0;
        closed.PropertyChanged += (_, _) => oldUpdates++;
        reopened.PropertyChanged += (_, _) => newUpdates++;
        closed.Dispose(); closed.Dispose();
        hardware.SetSnapshot(HardwareSnapshot.Empty);
        Assert.Equal(0, oldUpdates);
        Assert.True(newUpdates > 0);
        Assert.False(closed.CanEdit);
        Assert.False(closed.CanDownload);
    }

    private static HttpResponseMessage Response(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/x-ndjson") };
    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) => new(new Handler(handle)) { BaseAddress = new("http://localhost:11434/") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request, cancellationToken);
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
