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
public sealed class EventLogMonitor : IEventMonitor
{
    private readonly IEventStore _eventStore;
    private readonly IDeviceIdentityService _identityService;
    private readonly AgentOptions _options;
    private readonly ILogger<EventLogMonitor> _logger;
    private readonly List<EventLogWatcher> _watchers = [];
    private string? _deviceId;

    public string MonitorName => "EventLog";

    private static readonly Dictionary<string, int> LevelMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Critical"] = 1,
        ["Error"] = 2,
        ["Warning"] = 3,
        ["Information"] = 4,
        ["Verbose"] = 5
    };

    public EventLogMonitor(
        IEventStore eventStore,
        IDeviceIdentityService identityService,
        IOptions<AgentOptions> options,
        ILogger<EventLogMonitor> logger)
    {
        _eventStore = eventStore;
        _identityService = identityService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.EventLogMonitor.Enabled)
        {
            _logger.LogInformation("EventLogMonitor: Disabled via configuration");
            return;
        }

        var identity = await _identityService.GetOrCreateIdentityAsync(cancellationToken);
        _deviceId = identity.DeviceId;

        foreach (var channel in _options.EventLogMonitor.Channels)
        {
            try
            {
                StartChannelWatcher(channel);
            }
            catch (EventLogNotFoundException)
            {
                _logger.LogWarning("EventLogMonitor: Channel '{Channel}' not found — skipping", channel.Name);
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning("EventLogMonitor: Access denied for channel '{Channel}' — skipping", channel.Name);
            }
            catch (EventLogException ex)
            {
                _logger.LogWarning(ex, "EventLogMonitor: Failed to start watcher for '{Channel}'", channel.Name);
            }
        }

        _logger.LogInformation("EventLogMonitor started — watching {Count} channel(s)", _watchers.Count);
    }

    private void StartChannelWatcher(EventLogChannelConfig channel)
    {
        var xpath = BuildXPathQuery(channel);
        var query = new EventLogQuery(channel.Name, PathType.LogName, xpath);
        var watcher = new EventLogWatcher(query);
        watcher.EventRecordWritten += OnEventRecordWritten;
        watcher.Enabled = true;
        _watchers.Add(watcher);

        _logger.LogInformation("EventLogMonitor: Watching channel '{Channel}' (query: {Query})",
            channel.Name, xpath);
    }

    internal static string BuildXPathQuery(EventLogChannelConfig channel)
    {
        var conditions = new List<string>();

        if (channel.Levels.Count > 0)
        {
            var levelConditions = new List<string>();
            foreach (var level in channel.Levels)
            {
                if (LevelMap.TryGetValue(level, out var num))
                    levelConditions.Add($"Level={num}");
            }
            if (levelConditions.Count > 0)
                conditions.Add($"({string.Join(" or ", levelConditions)})");
        }

        if (channel.EventIds is { Count: > 0 })
        {
            var idConditions = channel.EventIds.Select(id => $"EventID={id}");
            conditions.Add($"({string.Join(" or ", idConditions)})");
        }

        return conditions.Count > 0
            ? $"*[System[{string.Join(" and ", conditions)}]]"
            : "*";
    }

    private async void OnEventRecordWritten(object? sender, EventRecordWrittenEventArgs e)
    {
        try
        {
            if (e.EventException is not null)
            {
                _logger.LogWarning(e.EventException, "EventLogMonitor: Error reading event log entry");
                return;
            }

            if (e.EventRecord is null || _deviceId is null)
                return;

            var payload = ParseEventRecord(e.EventRecord);
            if (payload is null)
                return;

            var severity = MapLevelToSeverity(e.EventRecord.Level);
            var deviceEvent = DeviceEvent.Create(_deviceId, EventType.EventLogEntry, severity, payload);
            await _eventStore.InsertEventAsync(deviceEvent);

            _logger.LogDebug("EventLog entry recorded: {Channel}/{EventId} from {Provider}",
                e.EventRecord.LogName, e.EventRecord.Id, e.EventRecord.ProviderName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EventLogMonitor: Error processing event");
        }
    }

    internal static Dictionary<string, object>? ParseEventRecord(EventRecord record)
    {
        try
        {
            var payload = new Dictionary<string, object>
            {
                ["channel"] = record.LogName ?? "unknown",
                ["event_id"] = record.Id,
                ["provider"] = record.ProviderName ?? "unknown",
                ["level"] = record.LevelDisplayName ?? LevelNumberToName(record.Level),
                ["time_created"] = (record.TimeCreated ?? DateTimeOffset.UtcNow).ToString("O")
            };

            if (record.UserId is not null)
                payload["user_sid"] = record.UserId.Value;

            try
            {
                var message = record.FormatDescription();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    payload["message"] = message.Length > 1024
                        ? message[..1024]
                        : message;
                }
            }
            catch
            {
                // FormatDescription can throw if resources are missing
            }

            if (record.MachineName is not null)
                payload["machine"] = record.MachineName;

            if (record.TaskDisplayName is not null)
                payload["task"] = record.TaskDisplayName;

            return payload;
        }
        catch
        {
            return null;
        }
    }

    internal static string LevelNumberToName(byte? level)
    {
        return level switch
        {
            1 => "Critical",
            2 => "Error",
            3 => "Warning",
            4 => "Information",
            5 => "Verbose",
            _ => "Unknown"
        };
    }

    internal static string MapLevelToSeverity(byte? level)
    {
        return level switch
        {
            1 => EventSeverity.Critical,
            2 => EventSeverity.High,
            3 => EventSeverity.Medium,
            4 => EventSeverity.Info,
            5 => EventSeverity.Low,
            _ => EventSeverity.Info
        };
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.Enabled = false;
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EventLogMonitor: Error disposing watcher");
            }
        }
        _watchers.Clear();

        _logger.LogInformation("EventLogMonitor stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            try { watcher.Dispose(); }
            catch { /* cleanup */ }
        }
    }
}
