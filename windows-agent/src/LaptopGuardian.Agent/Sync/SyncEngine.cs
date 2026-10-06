using LaptopGuardian.Agent.Backend;
using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Connectivity;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaptopGuardian.Agent.Sync;

public sealed class SyncEngine : ISyncEngine
{
    private readonly IEventStore _eventStore;
    private readonly IBackendClient _backendClient;
    private readonly IConnectivityTracker _connectivityTracker;
    private readonly IDeviceIdentityService _identityService;
    private readonly AgentOptions _options;
    private readonly ILogger<SyncEngine> _logger;
    private CancellationTokenSource? _cts;
    private Task? _syncTask;
    private int _consecutiveFailures;

    public SyncEngine(
        IEventStore eventStore,
        IBackendClient backendClient,
        IConnectivityTracker connectivityTracker,
        IDeviceIdentityService identityService,
        IOptions<AgentOptions> options,
        ILogger<SyncEngine> logger)
    {
        _eventStore = eventStore;
        _backendClient = backendClient;
        _connectivityTracker = connectivityTracker;
        _identityService = identityService;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _syncTask = SyncLoopAsync(_cts.Token);
        _logger.LogInformation("Sync engine started (interval: {Interval}s, batch: {Batch})",
            _options.SyncIntervalSeconds, _options.SyncBatchSize);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_syncTask is not null)
        {
            try
            {
                await _syncTask.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException)
            {
                _logger.LogWarning("Sync engine did not stop within 30 seconds");
            }
        }
        _logger.LogInformation("Sync engine stopped");
    }

    private async Task SyncLoopAsync(CancellationToken ct)
    {
        await _eventStore.ResetFailedEventsAsync(_options.MaxRetryCount, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_connectivityTracker.IsOnline)
                {
                    await SyncBatchAsync(ct);
                }

                var delay = GetBackoffDelay();
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // HttpClient.Timeout throws TaskCanceledException (subclass of
                // OperationCanceledException) — do NOT break the loop for timeouts.
                _logger.LogWarning("Sync operation timed out, will retry");
                _consecutiveFailures++;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in sync loop");
                try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public void ResetBackoff()
    {
        _consecutiveFailures = 0;
        _logger.LogInformation("Sync backoff reset — will attempt sync immediately");
    }

    internal async Task SyncBatchAsync(CancellationToken ct)
    {
        var identity = await _identityService.GetOrCreateIdentityAsync(ct);
        if (!identity.IsRegistered || identity.ApiKey is null)
        {
            _logger.LogDebug("Device not registered, skipping sync");
            return;
        }

        if (!identity.IsPaired)
        {
            _logger.LogDebug("Device not paired yet, skipping sync");
            return;
        }

        var events = await _eventStore.GetPendingEventsAsync(_options.SyncBatchSize, ct);
        if (events.Count == 0) return;

        _logger.LogInformation("Syncing {Count} pending events", events.Count);

        try
        {
            var result = await _backendClient.IngestEventsAsync(identity.ApiKey, events, ct);
            await _eventStore.MarkSyncedAsync(events.Select(e => e.Id), ct);
            _consecutiveFailures = 0;

            _logger.LogInformation("Synced batch: {Inserted} inserted, {Duplicates} duplicates",
                result.Inserted, result.Duplicates);
        }
        catch (BackendAuthenticationException)
        {
            _logger.LogWarning("Authentication failed during sync — device may not be paired yet");
            _consecutiveFailures++;
        }
        catch (BackendRateLimitException ex)
        {
            _logger.LogWarning("Rate limited during sync, retry after: {RetryAfter}", ex.RetryAfter);
            _consecutiveFailures++;
        }
        catch (BackendException ex)
        {
            _logger.LogError(ex, "Backend error during sync");
            _consecutiveFailures++;
            foreach (var evt in events)
            {
                await _eventStore.MarkFailedAsync(evt.Id, ct);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error during sync");
            _consecutiveFailures++;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Sync HTTP request timed out");
            _consecutiveFailures++;
        }
    }

    internal TimeSpan GetBackoffDelay()
    {
        if (_consecutiveFailures == 0)
            return TimeSpan.FromSeconds(_options.SyncIntervalSeconds);

        var backoffSeconds = _options.SyncIntervalSeconds * Math.Pow(2, Math.Min(_consecutiveFailures, 8));
        var maxSeconds = 300.0;
        return TimeSpan.FromSeconds(Math.Min(backoffSeconds, maxSeconds));
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
