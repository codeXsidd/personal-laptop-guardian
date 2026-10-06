using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
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
    private DeviceIdentity? _identity;
    private bool _sessionEventsRegistered;
    private readonly Dictionary<string, DateTimeOffset> _recentEvents = new();
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromSeconds(30);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQuerySessionInformationW(
        IntPtr hServer, uint sessionId, int wtsInfoClass,
        out IntPtr ppBuffer, out uint pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    private const int WTSConnectState = 4;
    private const int WTSUserName = 5;
    private const int WTSDomainName = 7;

    private const int WTSActive = 0;
    private const int WTSDisconnected = 4;
    private const uint InvalidSessionId = 0xFFFFFFFF;

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

        // Detect the current interactive session state on startup
        await DetectInitialSessionStateAsync(cancellationToken);

        // SystemEvents.SessionSwitch does not fire in a headless Windows Service
        // (no message pump). Use SCM session change notifications via custom lifetime.
        SessionChangeLifetime.SessionChanged += OnScmSessionChange;
        _sessionEventsRegistered = true;
        _logger.LogInformation("SessionMonitor: Subscribed to SCM session change events (lock/unlock)");

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

    private async Task DetectInitialSessionStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == InvalidSessionId)
            {
                _logger.LogInformation("SessionMonitor: No active console session detected on startup");
                return;
            }

            string? username = QuerySessionString(sessionId, WTSUserName);
            string? domain = QuerySessionString(sessionId, WTSDomainName);
            int? connectState = QuerySessionInt(sessionId, WTSConnectState);

            if (string.IsNullOrWhiteSpace(username))
            {
                _logger.LogInformation(
                    "SessionMonitor: Console session {SessionId} has no user — no interactive logon",
                    sessionId);
                return;
            }

            if (IsSystemAccount(username))
            {
                _logger.LogInformation(
                    "SessionMonitor: Console session {SessionId} user is system account {User} — skipping",
                    sessionId, username);
                return;
            }

            var fullUser = FormatUsername(domain ?? "", username);
            string eventType;
            string action;

            if (connectState == WTSActive)
            {
                eventType = EventType.SessionLogin;
                action = "logged_in";
            }
            else if (connectState == WTSDisconnected)
            {
                eventType = EventType.SessionLock;
                action = "locked";
            }
            else
            {
                eventType = EventType.SessionLogin;
                action = "logged_in";
            }

            var payload = new Dictionary<string, object>
            {
                ["machine_name"] = _identity!.MachineName,
                ["username"] = fullUser,
                ["action"] = action,
                ["source"] = "WTS_InitialState",
                ["session_id"] = sessionId.ToString(),
                ["connect_state"] = connectState?.ToString() ?? "unknown"
            };

            var deviceEvent = DeviceEvent.Create(_deviceId!, eventType, EventSeverity.Info, payload);
            await _eventStore.InsertEventAsync(deviceEvent);

            _logger.LogInformation(
                "SessionMonitor: Initial session state detected — User: {User}, State: {Action}, SessionId: {SessionId}",
                fullUser, action, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SessionMonitor: Failed to detect initial session state via WTS");
        }
    }

    private static string? QuerySessionString(uint sessionId, int infoClass)
    {
        if (!WTSQuerySessionInformationW(IntPtr.Zero, sessionId, infoClass, out var buffer, out var bytes))
            return null;
        try
        {
            return bytes > 2 ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static int? QuerySessionInt(uint sessionId, int infoClass)
    {
        if (!WTSQuerySessionInformationW(IntPtr.Zero, sessionId, infoClass, out var buffer, out var bytes))
            return null;
        try
        {
            return bytes >= 4 ? Marshal.ReadInt32(buffer) : null;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private async void OnScmSessionChange(int reason)
    {
        if (_deviceId is null || _identity is null) return;

        try
        {
            // SessionSwitchReason enum values:
            // 7 = SessionLock, 8 = SessionUnlock, 5 = SessionLogon, 6 = SessionLogoff
            string? eventType = null;
            string action = "";

            switch (reason)
            {
                case 7: // SessionLock
                    eventType = EventType.SessionLock;
                    action = "locked";
                    break;
                case 8: // SessionUnlock
                    eventType = EventType.SessionUnlock;
                    action = "unlocked";
                    break;
                case 5: // SessionLogon
                    eventType = EventType.SessionLogin;
                    action = "logged_in";
                    break;
                case 6: // SessionLogoff
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
                ["source"] = "SCM_SessionChange",
                ["reason"] = reason.ToString()
            };

            var deviceEvent = DeviceEvent.Create(_deviceId, eventType, EventSeverity.Info, payload);
            await _eventStore.InsertEventAsync(deviceEvent);

            _logger.LogInformation("SCM SessionChange recorded: {EventType} (reason={Reason})",
                eventType, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SessionMonitor: Error processing SCM session change");
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
            SessionChangeLifetime.SessionChanged -= OnScmSessionChange;
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
            SessionChangeLifetime.SessionChanged -= OnScmSessionChange;
            _sessionEventsRegistered = false;
        }
        _watcher?.Dispose();
    }
}
