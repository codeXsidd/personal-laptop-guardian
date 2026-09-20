using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaptopGuardian.Agent.Monitors;

[SupportedOSPlatform("windows")]
public sealed class FileAuditMonitor : IEventMonitor
{
    private readonly IEventStore _eventStore;
    private readonly IDeviceIdentityService _identityService;
    private readonly AgentOptions _options;
    private readonly ILogger<FileAuditMonitor> _logger;
    private EventLogWatcher? _watcher;
    private string? _deviceId;
    private readonly HashSet<string> _normalizedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _recentEvents = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _deduplicateLock = new();

    public string MonitorName => "FileAudit";

    // Windows Security audit EventIDs for file system object access
    private const int EventIdObjectAccess = 4663;
    private const int EventIdHandleRequest = 4656;

    private static readonly Dictionary<string, string> AccessMaskMap = new()
    {
        ["0x1"] = "read_data",
        ["0x2"] = "write_data",
        ["0x4"] = "append_data",
        ["0x20"] = "execute",
        ["0x80"] = "read_attributes",
        ["0x100"] = "write_attributes",
        ["0x10000"] = "delete",
        ["0x20000"] = "read_permissions",
        ["0x40000"] = "write_permissions"
    };

    public FileAuditMonitor(
        IEventStore eventStore,
        IDeviceIdentityService identityService,
        IOptions<AgentOptions> options,
        ILogger<FileAuditMonitor> logger)
    {
        _eventStore = eventStore;
        _identityService = identityService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.FileAudit.Enabled)
        {
            _logger.LogInformation("FileAuditMonitor: Disabled via configuration");
            return;
        }

        if (_options.FileAudit.Directories.Count == 0)
        {
            _logger.LogWarning("FileAuditMonitor: Enabled but no directories configured");
            return;
        }

        var identity = await _identityService.GetOrCreateIdentityAsync(cancellationToken);
        _deviceId = identity.DeviceId;

        ValidateAndLogDirectories();

        if (_normalizedDirectories.Count == 0)
        {
            _logger.LogWarning("FileAuditMonitor: No valid directories to monitor — monitor disabled");
            return;
        }

        try
        {
            var query = new EventLogQuery(
                "Security",
                PathType.LogName,
                $"*[System[Provider[@Name='Microsoft-Windows-Security-Auditing'] and (EventID={EventIdObjectAccess} or EventID={EventIdHandleRequest})]]");

            _watcher = new EventLogWatcher(query);
            _watcher.EventRecordWritten += OnAuditEvent;
            _watcher.Enabled = true;

            _logger.LogInformation("FileAuditMonitor started — watching {Count} directory(ies)",
                _normalizedDirectories.Count);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning(
                "FileAuditMonitor: Cannot access Security event log. " +
                "Add the service account to 'Event Log Readers' group. " +
                "Also ensure Object Access auditing is enabled: " +
                "auditpol /set /subcategory:\"File System\" /success:enable /failure:enable");
        }
        catch (EventLogNotFoundException)
        {
            _logger.LogWarning("FileAuditMonitor: Security event log not found");
        }
        catch (EventLogException ex)
        {
            _logger.LogWarning(ex, "FileAuditMonitor: Failed to start Security log watcher");
        }
    }

    private void ValidateAndLogDirectories()
    {
        foreach (var dir in _options.FileAudit.Directories)
        {
            var normalized = NormalizePath(dir);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                _logger.LogWarning("FileAuditMonitor: Invalid directory path configured: (empty)");
                continue;
            }

            if (!Directory.Exists(normalized))
            {
                _logger.LogWarning(
                    "FileAuditMonitor: Directory does not exist: {Directory} — will monitor if created later",
                    normalized);
            }

            _normalizedDirectories.Add(normalized);
            _logger.LogInformation("FileAuditMonitor: Monitoring directory: {Directory}", normalized);
        }
    }

    private async void OnAuditEvent(object? sender, EventRecordWrittenEventArgs e)
    {
        try
        {
            if (e.EventException is not null)
            {
                _logger.LogWarning(e.EventException, "FileAuditMonitor: Error reading audit event");
                return;
            }

            if (e.EventRecord is null || _deviceId is null)
                return;

            var parsed = ParseAuditEvent(e.EventRecord);
            if (parsed is null)
                return;

            var payload = parsed.Value;

            if (!IsInMonitoredDirectory(payload.objectPath))
                return;

            if (IsDuplicateEvent(payload.objectPath, payload.accessType))
                return;

            var eventPayload = new Dictionary<string, object>
            {
                ["file_path"] = payload.objectPath,
                ["access_type"] = payload.accessType,
                ["username"] = payload.username,
                ["event_id"] = payload.eventId,
                ["time_created"] = payload.timeCreated.ToString("O")
            };

            if (payload.processName is not null)
                eventPayload["process_name"] = payload.processName;
            if (payload.processId > 0)
                eventPayload["process_id"] = payload.processId;
            if (payload.accessMask is not null)
                eventPayload["access_mask"] = payload.accessMask;

            var deviceEvent = DeviceEvent.Create(
                _deviceId, EventType.FileAccess, EventSeverity.Info, eventPayload);
            await _eventStore.InsertEventAsync(deviceEvent);

            _logger.LogDebug("File access event: {AccessType} on {Path} by {User}",
                payload.accessType, payload.objectPath, payload.username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FileAuditMonitor: Error processing audit event");
        }
    }

    internal static (string objectPath, string accessType, string username,
        int eventId, DateTimeOffset timeCreated,
        string? processName, int processId, string? accessMask)?
        ParseAuditEvent(EventRecord record)
    {
        try
        {
            var props = record.Properties;
            if (props.Count < 6)
                return null;

            string objectPath;
            string username;
            string accessMask;
            string? processName;
            int processId;

            if (record.Id == EventIdObjectAccess)
            {
                // 4663: Properties: [0]=SID, [1]=Username, [2]=Domain, [3]=LogonID,
                //   [4]=ObjectServer, [5]=ObjectType, [6]=ObjectName, [7]=HandleID,
                //   [8]=AccessList, [9]=AccessMask, [10]=ProcessId, [11]=ProcessName
                if (props.Count < 9)
                    return null;

                username = props[1].Value?.ToString() ?? "unknown";
                objectPath = props[6].Value?.ToString() ?? "";
                accessMask = props.Count > 9 ? (props[9].Value?.ToString() ?? "") : "";
                processId = props.Count > 10 ? Convert.ToInt32(props[10].Value ?? 0) : 0;
                processName = props.Count > 11 ? props[11].Value?.ToString() : null;
            }
            else if (record.Id == EventIdHandleRequest)
            {
                // 4656: Similar layout
                if (props.Count < 9)
                    return null;

                username = props[1].Value?.ToString() ?? "unknown";
                objectPath = props[6].Value?.ToString() ?? "";
                accessMask = props.Count > 9 ? (props[9].Value?.ToString() ?? "") : "";
                processId = props.Count > 10 ? Convert.ToInt32(props[10].Value ?? 0) : 0;
                processName = props.Count > 11 ? props[11].Value?.ToString() : null;
            }
            else
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(objectPath))
                return null;

            var accessType = NormalizeAccessType(accessMask);
            var timeCreated = record.TimeCreated ?? DateTimeOffset.UtcNow;

            return (objectPath, accessType, username, record.Id, timeCreated,
                processName, processId, accessMask);
        }
        catch
        {
            return null;
        }
    }

    internal static string NormalizeAccessType(string? accessMask)
    {
        if (string.IsNullOrWhiteSpace(accessMask))
            return "unknown";

        var trimmed = accessMask.Trim();

        if (AccessMaskMap.TryGetValue(trimmed, out var mapped))
            return mapped;

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out var mask))
        {
            if ((mask & 0x2) != 0 || (mask & 0x4) != 0)
                return "write_data";
            if ((mask & 0x1) != 0)
                return "read_data";
            if ((mask & 0x20) != 0)
                return "execute";
            if ((mask & 0x10000) != 0)
                return "delete";
        }

        return "other";
    }

    internal bool IsInMonitoredDirectory(string filePath)
    {
        var normalizedFile = NormalizePath(filePath);
        if (normalizedFile is null)
            return false;

        foreach (var dir in _normalizedDirectories)
        {
            if (normalizedFile.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
            {
                if (normalizedFile.Length == dir.Length ||
                    normalizedFile[dir.Length] == '\\' ||
                    dir.EndsWith('\\'))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal bool IsDuplicateEvent(string objectPath, string accessType)
    {
        var key = $"{objectPath}|{accessType}";
        var now = DateTimeOffset.UtcNow;
        var window = TimeSpan.FromSeconds(_options.FileAudit.DuplicateWindowSeconds);

        lock (_deduplicateLock)
        {
            // Prune old entries
            var expired = _recentEvents
                .Where(kv => now - kv.Value > window)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var k in expired)
                _recentEvents.Remove(k);

            if (_recentEvents.TryGetValue(key, out var lastSeen) && now - lastSeen < window)
                return true;

            _recentEvents[key] = now;
            return false;
        }
    }

    internal static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var normalized = Path.GetFullPath(path).TrimEnd('\\');
            return normalized;
        }
        catch
        {
            return null;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_watcher is not null)
        {
            _watcher.Enabled = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _logger.LogInformation("FileAuditMonitor stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
