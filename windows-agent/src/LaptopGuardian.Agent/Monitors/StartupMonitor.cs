using System.Reflection;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Storage;
using Microsoft.Extensions.Logging;

namespace LaptopGuardian.Agent.Monitors;

public sealed class StartupMonitor : IEventMonitor
{
    private readonly IEventStore _eventStore;
    private readonly IDeviceIdentityService _identityService;
    private readonly ILogger<StartupMonitor> _logger;

    public string MonitorName => "Startup";

    public StartupMonitor(
        IEventStore eventStore,
        IDeviceIdentityService identityService,
        ILogger<StartupMonitor> logger)
    {
        _eventStore = eventStore;
        _identityService = identityService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("StartupMonitor: Recording agent started event");

        var identity = await _identityService.GetOrCreateIdentityAsync(cancellationToken);
        var agentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        var payload = new Dictionary<string, object>
        {
            ["machine_name"] = identity.MachineName,
            ["agent_version"] = agentVersion,
            ["os_version"] = Environment.OSVersion.VersionString,
            ["start_time"] = DateTimeOffset.UtcNow.ToString("O")
        };

        var deviceEvent = DeviceEvent.Create(
            identity.DeviceId,
            EventType.AgentStarted,
            EventSeverity.Info,
            payload);

        await _eventStore.InsertEventAsync(deviceEvent, cancellationToken);

        _logger.LogInformation(
            "Agent started event recorded. Device: {DeviceId}, Event: {EventId}",
            identity.DeviceId, deviceEvent.Id);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("StartupMonitor: Stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // No resources to dispose
    }
}
