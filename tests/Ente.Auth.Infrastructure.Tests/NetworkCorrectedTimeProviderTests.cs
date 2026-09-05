using System.Net;
using Ente.Auth.Infrastructure.Time;

namespace Ente.Auth.Infrastructure.Tests;

public sealed class NetworkCorrectedTimeProviderTests
{
    private static readonly DateTimeOffset LocalNow = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SynchronizeAsync_AppliesVerifiedServerCorrection()
    {
        var systemTime = new FixedTimeProvider(LocalNow);
        using var client = new HttpClient(new DateHeaderHandler(LocalNow.AddSeconds(45)));
        var clock = new NetworkCorrectedTimeProvider(client, new Uri("https://time.example/"), systemTime);

        var result = await clock.SynchronizeAsync();

        Assert.True(result.Success);
        Assert.True(clock.HasSynchronized);
        Assert.Equal(TimeSpan.FromSeconds(45), result.Correction);
        Assert.Equal(LocalNow.AddSeconds(45), clock.GetUtcNow());
    }

    [Fact]
    public async Task SynchronizeAsync_MissingDatePreservesLastVerifiedCorrection()
    {
        var systemTime = new FixedTimeProvider(LocalNow);
        var handler = new DateHeaderHandler(LocalNow.AddSeconds(-30));
        using var client = new HttpClient(handler);
        var clock = new NetworkCorrectedTimeProvider(client, new Uri("https://time.example/"), systemTime);
        Assert.True((await clock.SynchronizeAsync()).Success);

        handler.ServerTime = null;
        var result = await clock.SynchronizeAsync();

        Assert.False(result.Success);
        Assert.Equal(TimeSpan.FromSeconds(-30), clock.Correction);
        Assert.Equal(LocalNow.AddSeconds(-30), clock.GetUtcNow());
    }

    [Fact]
    public async Task SynchronizeAsync_RejectsImplausibleCorrection()
    {
        var systemTime = new FixedTimeProvider(LocalNow);
        using var client = new HttpClient(new DateHeaderHandler(LocalNow.AddHours(25)));
        var clock = new NetworkCorrectedTimeProvider(client, new Uri("https://time.example/"), systemTime);

        var result = await clock.SynchronizeAsync();

        Assert.False(result.Success);
        Assert.False(clock.HasSynchronized);
        Assert.Equal(TimeSpan.Zero, clock.Correction);
    }

    [Fact]
    public void Constructor_RejectsUnprotectedTimeEndpoint()
    {
        using var client = new HttpClient(new DateHeaderHandler(LocalNow));

        Assert.Throws<ArgumentException>(() =>
            new NetworkCorrectedTimeProvider(client, new Uri("http://time.example/")));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class DateHeaderHandler(DateTimeOffset? serverTime) : HttpMessageHandler
    {
        public DateTimeOffset? ServerTime { get; set; } = serverTime;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotFound);
            response.Headers.Date = ServerTime;
            return Task.FromResult(response);
        }
    }
}
