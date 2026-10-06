using LaptopGuardian.Agent.Backend;
using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Connectivity;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Monitors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaptopGuardian.Agent.Heartbeat;

public sealed class HeartbeatService : IHeartbeatService
{
    private readonly IBackendClient _backendClient;
    private readonly IDeviceIdentityService _identityService;
    private readonly IConnectivityTracker _connectivityTracker;
    private readonly ISystemMetricsProvider? _metricsProvider;
    private readonly AgentOptions _options;
    private readonly ILogger<HeartbeatService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _heartbeatTask;

    public bool IsPaired { get; private set; }
    public event EventHandler? DevicePaired;

    public HeartbeatService(
        IBackendClient backendClient,
        IDeviceIdentityService identityService,
        IConnectivityTracker connectivityTracker,
        IOptions<AgentOptions> options,
        ILogger<HeartbeatService> logger,
        ISystemMetricsProvider? metricsProvider = null)
    {
        _backendClient = backendClient;
        _identityService = identityService;
        _connectivityTracker = connectivityTracker;
        _options = options.Value;
        _logger = logger;
        _metricsProvider = metricsProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var identity = await _identityService.GetOrCreateIdentityAsync(cancellationToken);
        if (identity.IsPaired)
        {
            IsPaired = true;
            _logger.LogInformation("Restored paired state from stored identity (paired at: {PairedAt})",
                identity.PairedAt);
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _heartbeatTask = HeartbeatLoopAsync(_cts.Token);
        _logger.LogInformation("Heartbeat service started (interval: {Interval}s, paired: {IsPaired})",
            _options.HeartbeatIntervalSeconds, IsPaired);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_heartbeatTask is not null)
        {
            try
            {
                await _heartbeatTask.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException)
            {
                _logger.LogWarning("Heartbeat service did not stop within 30 seconds");
            }
        }
        _logger.LogInformation("Heartbeat service stopped");
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_connectivityTracker.IsOnline)
                {
                    await SendHeartbeatAsync(ct);
                }

                var interval = IsPaired
                    ? _options.HeartbeatIntervalSeconds
                    : Math.Min(_options.HeartbeatIntervalSeconds, 15);
                await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Heartbeat operation timed out, will retry");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in heartbeat loop");
                try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    internal async Task SendHeartbeatAsync(CancellationToken ct)
    {
        var identity = await _identityService.GetOrCreateIdentityAsync(ct);
        if (!identity.IsRegistered || identity.ApiKey is null)
        {
            _logger.LogDebug("Device not registered, skipping heartbeat");
            return;
        }

        try
        {
            var metrics = BuildMetricsPayload();
            var response = await _backendClient.SendHeartbeatAsync(identity.ApiKey, metrics, ct);

            if (response.IsPaired)
            {
                if (!IsPaired)
                {
                    IsPaired = true;
                    if (!identity.IsPaired)
                    {
                        identity.PairedAt = DateTimeOffset.UtcNow;
                        await _identityService.SaveIdentityAsync(identity, ct);
                    }
                    _logger.LogInformation("Device paired successfully! Server device ID: {DeviceId}",
                        response.DeviceId);
                    DevicePaired?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    _logger.LogDebug("Heartbeat acknowledged, server time: {ServerTime}", response.ServerTime);
                }
            }
            else
            {
                if (IsPaired)
                {
                    IsPaired = false;
                    identity.PairedAt = null;
                    await _identityService.SaveIdentityAsync(identity, ct);
                    _logger.LogWarning("Device has been unpaired remotely");
                }

                _logger.LogInformation(
                    "Heartbeat OK — awaiting pairing (code expires: {ExpiresAt})",
                    identity.PairingCodeExpiresAt?.ToLocalTime().ToString("HH:mm:ss") ?? "unknown");

                await TryRefreshPairingCodeAsync(identity, ct);
            }
        }
        catch (BackendAuthenticationException)
        {
            _logger.LogWarning("Heartbeat authentication failed — API key may be invalid or device revoked");
            if (IsPaired)
            {
                IsPaired = false;
                identity.PairedAt = null;
                try { await _identityService.SaveIdentityAsync(identity, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to save unpaired state"); }
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error during heartbeat");
        }
        catch (BackendException ex)
        {
            _logger.LogError(ex, "Backend error during heartbeat");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Heartbeat HTTP request timed out");
        }
    }

    private async Task TryRefreshPairingCodeAsync(DeviceIdentity identity, CancellationToken ct)
    {
        if (identity.PairingCodeExpiresAt is null || identity.PairingCodeExpiresAt > DateTimeOffset.UtcNow)
            return;

        if (identity.ApiKey is null)
            return;

        try
        {
            _logger.LogInformation("Pairing code expired — requesting new code from server");
            var response = await _backendClient.RefreshPairingCodeAsync(identity.ApiKey, ct);

            identity.PairingCode = response.PairingCode;
            identity.PairingCodeExpiresAt = response.ExpiresAt;
            await _identityService.SaveIdentityAsync(identity, ct);

            _logger.LogInformation(
                "Pairing code refreshed (expires: {ExpiresAt}). View code in Desktop app.",
                response.ExpiresAt.ToLocalTime().ToString("HH:mm:ss"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh pairing code — will retry on next heartbeat");
        }
    }

    private Dictionary<string, object>? BuildMetricsPayload()
    {
        var snapshot = _metricsProvider?.LatestMetrics;
        if (snapshot is null)
            return null;

        if (OperatingSystem.IsWindows())
            return SystemMetricsMonitor.BuildPayload(snapshot);

        return null;
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
