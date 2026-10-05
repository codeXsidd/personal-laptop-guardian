using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace LaptopGuardian.Agent.Monitors;

[SupportedOSPlatform("windows")]
public sealed class StartupMonitor : IEventMonitor
{
    private readonly IEventStore _eventStore;
    private readonly IDeviceIdentityService _identityService;
    private readonly ILogger<StartupMonitor> _logger;
    private DeviceIdentity? _identity;
    private bool _powerEventsRegistered;

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
        _identity = identity;
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

        // Emit system_startup with a stable boot-based ID so duplicate agent restarts
        // within the same boot don't create duplicate startup events.
        var bootTime = DateTimeOffset.UtcNow.AddMilliseconds(-Environment.TickCount64);
        var bootId = bootTime.ToString("yyyyMMddHHmm"); // minute precision for stability

        var startupPayload = new Dictionary<string, object>
        {
            ["machine_name"] = identity.MachineName,
            ["boot_time"] = bootTime.ToString("O"),
            ["boot_id"] = bootId,
            ["agent_version"] = agentVersion,
            ["os_version"] = Environment.OSVersion.VersionString
        };

        var startupPayloadJson = System.Text.Json.JsonSerializer.Serialize(startupPayload);

        // Generate a fixed ID based on device + boot so it's the same across agent restarts
        var stableInput = $"{identity.DeviceId}|system_startup|{bootId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(stableInput));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x40);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        var stableId = new Guid(hash.AsSpan(0, 16)).ToString();

        var startupEvent = new DeviceEvent
        {
            Id = stableId,
            EventType = Models.EventType.SystemStartup,
            Severity = EventSeverity.Info,
            Timestamp = bootTime,
            PayloadJson = startupPayloadJson
        };

        await _eventStore.InsertEventAsync(startupEvent, cancellationToken);

        _logger.LogInformation(
            "System startup event recorded. Device: {DeviceId}, BootId: {BootId}, Event: {EventId}",
            identity.DeviceId, bootId, startupEvent.Id);

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _powerEventsRegistered = true;
        _logger.LogInformation("StartupMonitor: Registered for PowerModeChanged events");
    }

    private async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (_identity is null) return;

        try
        {
            if (e.Mode == PowerModes.Suspend)
            {
                _logger.LogInformation("StartupMonitor: System entering sleep/hibernate");
                var payload = new Dictionary<string, object>
                {
                    ["machine_name"] = _identity.MachineName,
                    ["sleep_time"] = DateTimeOffset.UtcNow.ToString("O")
                };

                var sleepEvent = DeviceEvent.Create(
                    _identity.DeviceId,
                    EventType.SystemSleep,
                    EventSeverity.Info,
                    payload);

                await _eventStore.InsertEventAsync(sleepEvent);
                _logger.LogInformation("System sleep event recorded: {EventId}", sleepEvent.Id);
            }
            else if (e.Mode == PowerModes.Resume)
            {
                _logger.LogInformation("StartupMonitor: System waking from sleep/hibernate");
                var payload = new Dictionary<string, object>
                {
                    ["machine_name"] = _identity.MachineName,
                    ["wake_time"] = DateTimeOffset.UtcNow.ToString("O")
                };

                var wakeEvent = DeviceEvent.Create(
                    _identity.DeviceId,
                    EventType.SystemWake,
                    EventSeverity.Info,
                    payload);

                await _eventStore.InsertEventAsync(wakeEvent);
                _logger.LogInformation("System wake event recorded: {EventId}", wakeEvent.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartupMonitor: Failed to record power mode event");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_powerEventsRegistered)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _powerEventsRegistered = false;
        }

        _logger.LogInformation("StartupMonitor: Recording system shutdown event");

        if (_identity is not null)
        {
            try
            {
                var shutdownPayload = new Dictionary<string, object>
                {
                    ["machine_name"] = _identity.MachineName,
                    ["reason"] = "service_stopped",
                    ["shutdown_time"] = DateTimeOffset.UtcNow.ToString("O")
                };

                var shutdownEvent = DeviceEvent.Create(
                    _identity.DeviceId,
                    EventType.SystemShutdown,
                    EventSeverity.Info,
                    shutdownPayload);

                await _eventStore.InsertEventAsync(shutdownEvent);

                _logger.LogInformation(
                    "System shutdown event recorded. Device: {DeviceId}, Event: {EventId}",
                    _identity.DeviceId, shutdownEvent.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StartupMonitor: Failed to record shutdown event");
            }
        }

        _logger.LogInformation("StartupMonitor: Stopped");
    }

    public void Dispose()
    {
        if (_powerEventsRegistered)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _powerEventsRegistered = false;
        }
    }
}
