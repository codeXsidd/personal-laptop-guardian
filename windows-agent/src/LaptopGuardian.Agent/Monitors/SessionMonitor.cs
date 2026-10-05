using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace LaptopGuardian.Agent.Monitors;

[SupportedOSPlatform("windows")]
public sealed class SessionMonitor : IEventMonitor
{
    private readonly IEventStore _eventStore;
    private readonly IDeviceIdentityService _identityService;
    private readonly ILogger<SessionMonitor> _logger;
    private EventLogWatcher? _watcher;
    private string? _deviceId;
    private DeviceIdentity? _identity;
    private bool _sessionEventsRegistered;
    private readonly Dictionary<string, DateTimeOffset> _recentEvents = new();
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromSeconds(30);

    public string MonitorName => "Session";

    // Type 7 (Unlock) excluded — unlocks are tracked via SystemEvents.SessionSwitch
    private static readonly HashSet<int> InteractiveLogonTypes = [2, 10, 11];

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
        _identity = identity;

        // Primary lock/unlock detection via SystemEvents.SessionSwitch
        // Works without Security audit policy configuration
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _sessionEventsRegistered = true;
        _logger.LogInformation("SessionMonitor: Registered for SessionSwitch events (lock/unlock)");

        try
        {
            // 4624=Logon, 4647=User-initiated logoff
            // 4634 excluded: fires for internal session cleanup during lock/sleep/unlock
            // 4800/4801 kept as supplementary: only fire when audit policy is enabled
            var query = new EventLogQuery(
                "Security",
                PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-Security-Auditing'] and " +
                "(EventID=4624 or EventID=4647 or EventID=4800 or EventID=4801)]]");

            _watcher = new EventLogWatcher(query);
            _watcher.EventRecordWritten += OnSessionEvent;
            _watcher.Enabled = true;

            _logger.LogInformation("SessionMonitor started — watching Security log + SessionSwitch");
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning(
                "SessionMonitor: Cannot access Security event log. " +
                "Lock/unlock detection still works via SessionSwitch. " +
                "Add the service account to 'Event Log Readers' for login/logout monitoring");
        }
        catch (EventLogNotFoundException)
        {
            _logger.LogWarning("SessionMonitor: Security event log not found — using SessionSwitch only");
        }
        catch (EventLogException ex)
        {
            _logger.LogWarning(ex, "SessionMonitor: Failed to start Security log watcher — using SessionSwitch only");
        }
    }

    private async void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (_deviceId is null || _identity is null) return;

        try
        {
            string? eventType = null;
            string action = "";

            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    eventType = EventType.SessionLock;
                    action = "locked";
                    break;
                case SessionSwitchReason.SessionUnlock:
                    eventType = EventType.SessionUnlock;
                    action = "unlocked";
                    break;
                case SessionSwitchReason.SessionLogon:
                    eventType = EventType.SessionLogin;
                    action = "logged_in";
                    break;
                case SessionSwitchReason.SessionLogoff:
                    eventType = EventType.SessionLogout;
                    action = "logged_out";
                    break;
            }

            if (eventType is null) return;

            var dedupeKey = $"switch:{eventType}";
            var now = DateTimeOffset.UtcNow;

            lock (_recentEvents)
            {
                if (_recentEvents.TryGetValue(dedupeKey, out var lastTime)
                    && (now - lastTime) < DeduplicationWindow)
                {
                    return;
                }
                _recentEvents[dedupeKey] = now;
            }

            var payload = new Dictionary<string, object>
            {
                ["machine_name"] = _identity.MachineName,
                ["action"] = action,
                ["source"] = "SessionSwitch",
                ["reason"] = e.Reason.ToString()
            };

            var deviceEvent = DeviceEvent.Create(_deviceId, eventType, EventSeverity.Info, payload);
            await _eventStore.InsertEventAsync(deviceEvent);

            _logger.LogInformation("SessionSwitch event recorded: {EventType} ({Reason})",
                eventType, e.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SessionMonitor: Error processing SessionSwitch event");
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

            var username = payload.GetValueOrDefault("username")?.ToString() ?? "unknown";
            var dedupeKey = $"{eventType}:{username}";
            var now = DateTimeOffset.UtcNow;

            lock (_recentEvents)
            {
                // Also check against SessionSwitch events to prevent duplicate lock/unlock
                var switchKey = $"switch:{eventType}";
                if (_recentEvents.TryGetValue(switchKey, out var switchTime)
                    && (now - switchTime) < DeduplicationWindow)
                {
                    _logger.LogDebug(
                        "SessionMonitor: Skipping Security log {EventType} — already captured via SessionSwitch",
                        eventType);
                    return;
                }

                if (_recentEvents.TryGetValue(dedupeKey, out var lastTime)
                    && (now - lastTime) < DeduplicationWindow)
                {
                    return;
                }

                _recentEvents[dedupeKey] = now;
            }

            var deviceEvent = DeviceEvent.Create(_deviceId, eventType, EventSeverity.Info, payload);
            await _eventStore.InsertEventAsync(deviceEvent);

            _logger.LogDebug("Session event recorded: {EventType} for {Username}",
                eventType, username);
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

            case 4800: // Workstation locked (supplementary — primary is SessionSwitch)
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

            case 4801: // Workstation unlocked (supplementary — primary is SessionSwitch)
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
        if (_sessionEventsRegistered)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _sessionEventsRegistered = false;
        }

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
        if (_sessionEventsRegistered)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _sessionEventsRegistered = false;
        }
        _watcher?.Dispose();
    }
}
