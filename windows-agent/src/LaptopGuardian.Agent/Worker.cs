using System.Reflection;
using LaptopGuardian.Agent.Backend;
using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Connectivity;
using LaptopGuardian.Agent.Heartbeat;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Monitors;
using LaptopGuardian.Agent.Storage;
using LaptopGuardian.Agent.Sync;
using Microsoft.Extensions.Options;

namespace LaptopGuardian.Agent;

public sealed class Worker : BackgroundService
{
    private readonly IEventStore _eventStore;
    private readonly IEnumerable<IEventMonitor> _monitors;
    private readonly IDeviceIdentityService _identityService;
    private readonly IBackendClient _backendClient;
    private readonly IConnectivityTracker _connectivityTracker;
    private readonly ISyncEngine _syncEngine;
    private readonly IHeartbeatService _heartbeatService;
    private readonly AgentOptions _options;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IEventStore eventStore,
        IEnumerable<IEventMonitor> monitors,
        IDeviceIdentityService identityService,
        IBackendClient backendClient,
        IConnectivityTracker connectivityTracker,
        ISyncEngine syncEngine,
        IHeartbeatService heartbeatService,
        IOptions<AgentOptions> options,
        ILogger<Worker> logger)
    {
        _eventStore = eventStore;
        _monitors = monitors;
        _identityService = identityService;
        _backendClient = backendClient;
        _connectivityTracker = connectivityTracker;
        _syncEngine = syncEngine;
        _heartbeatService = heartbeatService;
        _options = options.Value;
        _logger = logger;
    }

    private bool IsBackendConfigured =>
        !string.IsNullOrWhiteSpace(_options.SupabaseUrl) &&
        !string.IsNullOrWhiteSpace(_options.SupabaseAnonKey);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Laptop Guardian agent starting");

        await _eventStore.InitializeAsync(stoppingToken);
        _logger.LogInformation("Event store initialized");

        var identity = await _identityService.GetOrCreateIdentityAsync(stoppingToken);
        _logger.LogInformation("Device ID: {DeviceId}", identity.DeviceId);

        foreach (var monitor in _monitors)
        {
            try
            {
                _logger.LogInformation("Starting monitor: {MonitorName}", monitor.MonitorName);
                await monitor.StartAsync(stoppingToken);
                _logger.LogInformation("Monitor started: {MonitorName}", monitor.MonitorName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start monitor: {MonitorName}", monitor.MonitorName);
            }
        }

        if (IsBackendConfigured)
        {
            if (!identity.IsRegistered)
            {
                await RegisterDeviceAsync(identity, stoppingToken);
            }
            else
            {
                _logger.LogInformation("Device already registered. Paired: {IsPaired}", identity.IsPaired);
            }

            await _connectivityTracker.StartAsync(stoppingToken);
            _logger.LogInformation("Connectivity tracker started");

            _heartbeatService.DevicePaired += (_, _) => _syncEngine.ResetBackoff();

            await _heartbeatService.StartAsync(stoppingToken);
            _logger.LogInformation("Heartbeat service started");

            await _syncEngine.StartAsync(stoppingToken);
            _logger.LogInformation("Sync engine started");
        }
        else
        {
            _logger.LogWarning("Supabase not configured — running in offline-only mode. " +
                               "Set Agent:SupabaseUrl and Agent:SupabaseAnonKey to enable sync");
        }

        var pendingCount = await _eventStore.GetPendingCountAsync(stoppingToken);
        _logger.LogInformation("Agent running. Pending events: {PendingCount}", pendingCount);

        var lastCleanup = DateTimeOffset.UtcNow;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);

                if (DateTimeOffset.UtcNow - lastCleanup >= TimeSpan.FromHours(24))
                {
                    try
                    {
                        await _eventStore.CleanupOldEventsAsync(30, stoppingToken);
                        lastCleanup = DateTimeOffset.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during periodic event cleanup");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private async Task RegisterDeviceAsync(DeviceIdentity identity, CancellationToken ct)
    {
        var osVersion = Environment.OSVersion.VersionString;
        var agentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        try
        {
            _logger.LogInformation("Registering device with backend...");
            var response = await _backendClient.RegisterDeviceAsync(
                identity.MachineName, osVersion, agentVersion, ct);

            identity.ServerDeviceId = response.DeviceId;
            identity.ApiKey = response.ApiKey;
            identity.PairingCode = response.PairingCode;
            identity.PairingCodeExpiresAt = response.ExpiresAt;

            await _identityService.SaveIdentityAsync(identity, ct);

            _logger.LogInformation(
                "Device registered successfully. Pairing code available in Desktop app (expires: {ExpiresAt})",
                response.ExpiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register device — will retry on next startup");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Laptop Guardian agent stopping");

        if (IsBackendConfigured)
        {
            try { await _syncEngine.StopAsync(cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Error stopping sync engine"); }

            try { await _heartbeatService.StopAsync(cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Error stopping heartbeat service"); }

            try { await _connectivityTracker.StopAsync(cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Error stopping connectivity tracker"); }
        }

        foreach (var monitor in _monitors.Reverse())
        {
            try
            {
                _logger.LogInformation("Stopping monitor: {MonitorName}", monitor.MonitorName);
                await monitor.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping monitor: {MonitorName}", monitor.MonitorName);
            }
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("Laptop Guardian agent stopped");
    }
}
