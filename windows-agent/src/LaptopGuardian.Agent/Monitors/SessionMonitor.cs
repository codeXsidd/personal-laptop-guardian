using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Storage;
using Microsoft.Extensions.Logging;

namespace LaptopGuardian.Agent.Monitors;

[SupportedOSPlatform("windows")]
public sealed class SessionMonitor : IEventMonitor
{
    private readonly IEventStore _eventStore;
    private readonly IDeviceIdentityService _identityService;
    private readonly ILogger<SessionMonitor> _logger;
    private EventLogWatcher? _watcher;
    private string? _deviceId;

    public string MonitorName => "Session";

    private static readonly HashSet<int> InteractiveLogonTypes = [2, 7, 10, 11];

    public SessionMonitor(
        IEventStore eventStore,
        IDeviceIdentityService identityService,
        ILogger<SessionMonitor> logger)
    {
        _eventStore = eventStore;
        _identityService = identityService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var identity = await _identityService.GetOrCreateIdentityAsync(cancellationToken);
        _deviceId = identity.DeviceId;

        try
        {
            var query = new EventLogQuery(
                "Security",
                PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-Security-Auditing'] and " +
                "(EventID=4624 or EventID=4634 or EventID=4647 or EventID=4800 or EventID=4801)]]");

            _watcher = new EventLogWatcher(query);
            _watcher.EventRecordWritten += OnSessionEvent;
            _watcher.Enabled = true;

            _logger.LogInformation("SessionMonitor started — watching Security log");
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning(
                "SessionMonitor: Cannot access Security event log. " +
                "Add the service account to the 'Event Log Readers' group to enable session monitoring");
        }
        catch (EventLogNotFoundException)
        {
            _logger.LogWarning("SessionMonitor: Security event log not found on this system");
        }
        catch (EventLogException ex)
        {
            _logger.LogWarning(ex, "SessionMonitor: Failed to start Security log watcher");
        }
    }

    private async void OnSessionEvent(object? sender, EventRecordWrittenEventArgs e)
    {
        try
        {
            if (e.EventException is not null)
            {
                _logger.LogWarning(e.EventException, "SessionMonitor: Error reading event log entry");
                return;
            }

            if (e.EventRecord is null || _deviceId is null)
                return;

            var parsed = ParseSecurityEvent(e.EventRecord);
            if (parsed is null)
                return;

            var (eventType, payload) = parsed.Value;

            var deviceEvent = DeviceEvent.Create(_deviceId, eventType, EventSeverity.Info, payload);
            await _eventStore.InsertEventAsync(deviceEvent);

            _logger.LogDebug("Session event recorded: {EventType} for {Username}",
                eventType, payload.GetValueOrDefault("username"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SessionMonitor: Error processing event");
        }
    }

    internal static (string eventType, Dictionary<string, object> payload)? ParseSecurityEvent(
        EventRecord record)
    {
        var eventId = record.Id;
        var properties = record.Properties;

        switch (eventId)
        {
            case 4624: // Logon
            {
                if (properties.Count < 9)
                    return null;

                var logonType = Convert.ToInt32(properties[8].Value);
                if (!InteractiveLogonTypes.Contains(logonType))
                    return null;

                var username = properties[5].Value?.ToString() ?? "unknown";
                var domain = properties[6].Value?.ToString() ?? "";

                if (IsSystemAccount(username))
                    return null;

                return (EventType.SessionLogin, new Dictionary<string, object>
                {
                    ["username"] = FormatUsername(domain, username),
                    ["logon_type"] = logonType,
                    ["session_id"] = properties[7].Value?.ToString() ?? "",
                    ["event_id"] = eventId,
                    ["source"] = "Security"
                });
            }

            case 4634: // Logoff
            {
                if (properties.Count < 5)
                    return null;

                var logonType = Convert.ToInt32(properties[4].Value);
                if (!InteractiveLogonTypes.Contains(logonType))
                    return null;

                var username = properties[1].Value?.ToString() ?? "unknown";
                var domain = properties[2].Value?.ToString() ?? "";

                if (IsSystemAccount(username))
                    return null;

                return (EventType.SessionLogout, new Dictionary<string, object>
                {
                    ["username"] = FormatUsername(domain, username),
                    ["logon_type"] = logonType,
                    ["session_id"] = properties[3].Value?.ToString() ?? "",
                    ["event_id"] = eventId,
                    ["source"] = "Security"
                });
            }

            case 4647: // User initiated logoff
            {
                if (properties.Count < 4)
                    return null;

                var username = properties[1].Value?.ToString() ?? "unknown";
                var domain = properties[2].Value?.ToString() ?? "";

                if (IsSystemAccount(username))
                    return null;

                return (EventType.SessionLogout, new Dictionary<string, object>
                {
                    ["username"] = FormatUsername(domain, username),
                    ["session_id"] = properties[3].Value?.ToString() ?? "",
                    ["event_id"] = eventId,
                    ["source"] = "Security"
                });
            }

            case 4800: // Workstation locked
            {
                if (properties.Count < 4)
                    return null;

                var username = properties[1].Value?.ToString() ?? "unknown";
                var domain = properties[2].Value?.ToString() ?? "";

                return (EventType.SessionLock, new Dictionary<string, object>
                {
                    ["username"] = FormatUsername(domain, username),
                    ["session_id"] = properties[3].Value?.ToString() ?? "",
                    ["event_id"] = eventId,
                    ["source"] = "Security"
                });
            }

            case 4801: // Workstation unlocked
            {
                if (properties.Count < 4)
                    return null;

                var username = properties[1].Value?.ToString() ?? "unknown";
                var domain = properties[2].Value?.ToString() ?? "";

                return (EventType.SessionUnlock, new Dictionary<string, object>
                {
                    ["username"] = FormatUsername(domain, username),
                    ["session_id"] = properties[3].Value?.ToString() ?? "",
                    ["event_id"] = eventId,
                    ["source"] = "Security"
                });
            }

            default:
                return null;
        }
    }

    internal static bool IsSystemAccount(string username)
    {
        return string.Equals(username, "SYSTEM", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(username, "LOCAL SERVICE", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(username, "NETWORK SERVICE", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(username, "ANONYMOUS LOGON", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(username, "DWM-1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(username, "DWM-2", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(username, "DWM-3", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(username, "UMFD-0", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(username, "UMFD-1", StringComparison.OrdinalIgnoreCase) ||
               username.EndsWith("$", StringComparison.Ordinal);
    }

    internal static string FormatUsername(string domain, string username)
    {
        return string.IsNullOrWhiteSpace(domain) ? username : $"{domain}\\{username}";
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_watcher is not null)
        {
            _watcher.Enabled = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _logger.LogInformation("SessionMonitor stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
