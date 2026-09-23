using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MiLife.DeviceCollector;
using MiLife.DeviceCollector.Configuration;
using MiLife.DeviceCollector.Diagnostics;
using MiLife.DeviceCollector.Hardware;
using MiLife.DeviceCollector.Submission;
using MiLife.DeviceContracts;

namespace MiLife.DeviceCollector.Tests;

public sealed class SubmissionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "milife-tests-" + Guid.NewGuid().ToString("N"));
    private readonly DeviceInventory inventory = new()
    {
        CollectionId = Guid.NewGuid(), SerialNumber = "TEST-123", CollectorVersion = "1.0.0",
        ComputerName = "TEST-PC", CollectedAtUtc = DateTimeOffset.UtcNow
    };
    private readonly CollectorSettings settings = new() { ApiUrl = "https://intake.test/api/device-intake", EnrollmentToken = new string('t', 40) };
    private LocalFiles Files => new("1.0.0", directory);

    [Fact]
    public async Task SuccessSendsOnlyJsonAndEnrollmentHeader()
    {
        var handler = new FakeHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal(settings.EnrollmentToken, request.Headers.GetValues("X-Enrollment-Token").Single());
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            var data = await request.Content.ReadFromJsonAsync<DeviceInventory>(InventoryJson.Options);
            Assert.Equal(inventory.CollectionId, data!.CollectionId);
            return Success();
        });
        using var http = new HttpClient(handler);
        var result = await new SubmissionClient(http, settings, Files, new FakeDelay()).SubmitAsync(inventory);
        Assert.True(result.Success); Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(409)] [InlineData(413)] [InlineData(302)]
    public async Task PermanentErrorsAreNotRetried(int status)
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var http = new HttpClient(handler);
        var delay = new FakeDelay();
        var result = await new SubmissionClient(http, settings, Files, delay).SubmitAsync(inventory);
        Assert.False(result.Success); Assert.Equal(1, handler.Calls); Assert.Empty(delay.Delays);
    }

    [Theory]
    [InlineData(408)] [InlineData(429)] [InlineData(500)] [InlineData(503)]
    public async Task TransientErrorsUseThreeAttemptsAndIncreasingDelays(int status)
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var http = new HttpClient(handler);
        var delay = new FakeDelay();
        var result = await new SubmissionClient(http, settings, Files, delay).SubmitAsync(inventory);
        Assert.False(result.Success); Assert.Equal(3, handler.Calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }, delay.Delays);
    }

    [Fact]
    public async Task SuccessfulRetryKeepsSameCollectionId()
    {
        var ids = new List<Guid>();
        var handler = new FakeHandler(async request =>
        {
            ids.Add((await request.Content!.ReadFromJsonAsync<DeviceInventory>(InventoryJson.Options))!.CollectionId);
            return ids.Count == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Success();
        });
        using var http = new HttpClient(handler);
        Assert.True((await new SubmissionClient(http, settings, Files, new FakeDelay()).SubmitAsync(inventory)).Success);
        Assert.Equal(2, ids.Count); Assert.Single(ids.Distinct());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NetworkFailureAndTimeoutSaveRecovery(bool timeout)
    {
        var handler = new FakeHandler(_ => timeout ? throw new TaskCanceledException() : throw new HttpRequestException("DNS failure"));
        using var http = new HttpClient(handler);
        var app = new CollectorApplication(new FakeHardware(inventory), new SubmissionClient(http, settings, Files, new FakeDelay()), Files);
        Assert.Equal(1, await app.RunAsync("UNKNOWN"));
        Assert.Equal(3, handler.Calls);
        var path = Assert.Single(Directory.GetFiles(Path.Combine(directory, "Pending")));
        var recovered = JsonSerializer.Deserialize<DeviceInventory>(await File.ReadAllTextAsync(path), InventoryJson.Options);
        Assert.Equal(inventory.CollectionId, recovered!.CollectionId);
        Assert.DoesNotContain(settings.EnrollmentToken, await File.ReadAllTextAsync(Path.Combine(directory, "Logs", "collector.log")));
    }

    [Fact]
    public async Task MissingSerialSavesLocallyWithoutSending()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("Must not send"));
        using var http = new HttpClient(handler);
        var app = new CollectorApplication(new FakeHardware(inventory with { SerialNumber = null }), new SubmissionClient(http, settings, Files, new FakeDelay()), Files);
        Assert.Equal(1, await app.RunAsync("UNKNOWN")); Assert.Equal(0, handler.Calls);
        Assert.Single(Directory.GetFiles(Path.Combine(directory, "Pending")));
    }

    [Fact]
    public async Task RecoveryFilenamesCannotEscapePendingAndNeverOverwrite()
    {
        var data = inventory with { SerialNumber = "../../COM1:bad\\path" };
        var first = await Files.SavePendingAsync(data);
        var second = await Files.SavePendingAsync(data);
        Assert.NotEqual(first, second);
        Assert.Equal(Path.Combine(directory, "Pending"), Path.GetDirectoryName(first));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(directory, "Pending")).Length);
    }

    [Fact]
    public async Task LongRetryAfterStopsInsteadOfRetryingEarly()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(60)); return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        Assert.False((await new SubmissionClient(http, settings, Files, new FakeDelay()).SubmitAsync(inventory)).Success);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task InsecureConfigurationNeverSends()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException());
        using var http = new HttpClient(handler);
        var result = await new SubmissionClient(http, settings with { ApiUrl = "http://intake.test" }, Files, new FakeDelay()).SubmitAsync(inventory);
        Assert.False(result.Success); Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task UnexpectedSuccessResponseIsNotReportedAsSuccess()
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>gateway</html>") }));
        using var http = new HttpClient(handler);
        Assert.False((await new SubmissionClient(http, settings, Files, new FakeDelay()).SubmitAsync(inventory)).Success);
    }

    [Fact]
    public void LogRotationRemainsBounded()
    {
        var files = Files;
        for (var i = 0; i < 1600; i++) files.Log("Test", new string('x', 1000));
        var logs = Directory.GetFiles(Path.Combine(directory, "Logs"));
        Assert.InRange(logs.Length, 1, 4);
        Assert.All(logs, file => Assert.True(new FileInfo(file).Length < 260 * 1024));
    }

    private HttpResponseMessage Success() => new(HttpStatusCode.OK)
    { Content = JsonContent.Create(new IntakeReceipt(true, Guid.NewGuid(), inventory.SerialNumber!, "PendingReview", false), options: InventoryJson.Options) };
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private sealed class FakeHardware(DeviceInventory data) : IHardwareCollector { public DeviceInventory Collect(string branchCode) => data; }
    private sealed class FakeDelay : IRetryDelay
    {
        public List<TimeSpan> Delays { get; } = [];
        public Task WaitAsync(TimeSpan delay, CancellationToken ct) { Delays.Add(delay); return Task.CompletedTask; }
    }
    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return send(request); }
    }
}
