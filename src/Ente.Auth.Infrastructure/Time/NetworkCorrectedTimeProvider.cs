namespace Ente.Auth.Infrastructure.Time;

public sealed record ClockSynchronizationResult(bool Success, TimeSpan Correction, string? Error = null);

/// <summary>
/// Uses an authenticated HTTPS Date header to correct modest device-clock drift.
/// The last verified correction remains usable offline; local time is the fallback
/// until the first successful synchronization.
/// </summary>
public sealed class NetworkCorrectedTimeProvider : TimeProvider
{
    private static readonly TimeSpan MaximumRoundTrip = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaximumCorrection = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient;
    private readonly Uri _timeEndpoint;
    private readonly TimeProvider _systemTime;
    private readonly SemaphoreSlim _synchronizationGate = new(1, 1);
    private long _correctionTicks;
    private int _hasSynchronized;

    public NetworkCorrectedTimeProvider(HttpClient httpClient, Uri timeEndpoint, TimeProvider? systemTime = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _timeEndpoint = timeEndpoint ?? throw new ArgumentNullException(nameof(timeEndpoint));
        _systemTime = systemTime ?? TimeProvider.System;

        if (_timeEndpoint.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The network time endpoint must use HTTPS.", nameof(timeEndpoint));
    }

    public bool HasSynchronized => Volatile.Read(ref _hasSynchronized) == 1;
    public TimeSpan Correction => TimeSpan.FromTicks(Interlocked.Read(ref _correctionTicks));

    public override DateTimeOffset GetUtcNow() => _systemTime.GetUtcNow() + Correction;

    public async Task<ClockSynchronizationResult> SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        await _synchronizationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sentAt = _systemTime.GetUtcNow();
            using var request = new HttpRequestMessage(HttpMethod.Head, _timeEndpoint);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            var receivedAt = _systemTime.GetUtcNow();

            if (response.Headers.Date is not { } serverTime)
                return new ClockSynchronizationResult(false, Correction, "The server did not provide a Date header.");

            var roundTrip = receivedAt - sentAt;
            if (roundTrip < TimeSpan.Zero || roundTrip > MaximumRoundTrip)
                return new ClockSynchronizationResult(false, Correction, "The network time request was not reliable.");

            var midpoint = sentAt + TimeSpan.FromTicks(roundTrip.Ticks / 2);
            var correction = serverTime.ToUniversalTime() - midpoint;
            if (correction.Duration() > MaximumCorrection)
                return new ClockSynchronizationResult(false, Correction, "The reported time correction was outside the safe range.");

            Interlocked.Exchange(ref _correctionTicks, correction.Ticks);
            Volatile.Write(ref _hasSynchronized, 1);
            return new ClockSynchronizationResult(true, correction);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ClockSynchronizationResult(false, Correction, "The network time request timed out.");
        }
        catch (HttpRequestException)
        {
            return new ClockSynchronizationResult(false, Correction, "Network time is unavailable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ClockSynchronizationResult(false, Correction, "Network time could not be verified.");
        }
        finally
        {
            _synchronizationGate.Release();
        }
    }
}
