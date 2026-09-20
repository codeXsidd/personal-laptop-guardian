using LaptopGuardian.Agent.Backend;
using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Connectivity;
using LaptopGuardian.Agent.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaptopGuardian.Agent.Heartbeat;

public sealed class HeartbeatService : IHeartbeatService
{
    private readonly IBackendClient _backendClient;
    private readonly IDeviceIdentityService _identityService;
    private readonly IConnectivityTracker _connectivityTracker;
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
        ILogger<HeartbeatService> logger)
    {
        _backendClient = backendClient;
        _identityService = identityService;
        _connectivityTracker = connectivityTracker;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _heartbeatTask = HeartbeatLoopAsync(_cts.Token);
        _logger.LogInformation("Heartbeat service started (interval: {Interval}s)",
            _options.HeartbeatIntervalSeconds);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_heartbeatTask is not null)
        {
            try { await _heartbeatTask; }
            catch (OperationCanceledException) { }
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
            var response = await _backendClient.SendHeartbeatAsync(identity.ApiKey, ct);
            _logger.LogDebug("Heartbeat acknowledged, server time: {ServerTime}", response.ServerTime);

            if (!IsPaired)
            {
                IsPaired = true;
                identity.PairedAt = DateTimeOffset.UtcNow;
                await _identityService.SaveIdentityAsync(identity, ct);
                _logger.LogInformation("Device paired successfully! Server device ID: {DeviceId}",
                    response.DeviceId);
                DevicePaired?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (BackendAuthenticationException)
        {
            if (!IsPaired)
            {
                _logger.LogDebug(
                    "Heartbeat returned 401 — awaiting pairing. Code: {PairingCode}",
                    identity.PairingCode);
            }
            else
            {
                _logger.LogWarning("Heartbeat authentication failed — device may have been revoked");
                IsPaired = false;
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
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
