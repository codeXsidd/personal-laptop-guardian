using System.Diagnostics;
using System.Runtime.Versioning;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Storage;
using Microsoft.Extensions.Logging;

namespace LaptopGuardian.Agent.Monitors;

[SupportedOSPlatform("windows")]
public sealed class ProcessMonitor : IEventMonitor
{
    private readonly IEventStore _eventStore;
    private readonly IDeviceIdentityService _identityService;
    private readonly ILogger<ProcessMonitor> _logger;
    private Timer? _pollTimer;
    private string? _deviceId;
    private Dictionary<ProcessKey, ProcessSnapshot>? _previousSnapshot;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);

    public string MonitorName => "Process";

    private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Registry", "smss", "csrss", "wininit",
        "services", "lsass", "svchost", "fontdrvhost", "dwm",
        "conhost", "WmiPrvSE", "spoolsv", "SearchIndexer",
        "SecurityHealthService", "MsMpEng", "NisSrv", "SgrmBroker",
        "dasHost", "sihost", "ctfmon", "dllhost"
    };

    public ProcessMonitor(
        IEventStore eventStore,
        IDeviceIdentityService identityService,
        ILogger<ProcessMonitor> logger)
    {
        _eventStore = eventStore;
        _identityService = identityService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var identity = await _identityService.GetOrCreateIdentityAsync(cancellationToken);
        _deviceId = identity.DeviceId;

        _previousSnapshot = TakeSnapshot();
        _logger.LogInformation("ProcessMonitor started — baseline: {Count} processes", _previousSnapshot.Count);

        _pollTimer = new Timer(OnPollTimerCallback, null, _pollInterval, _pollInterval);
    }

    private async void OnPollTimerCallback(object? state)
    {
        try
        {
            await PollProcessesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcessMonitor: Error during poll cycle");
        }
    }

    internal async Task PollProcessesAsync()
    {
        if (_deviceId is null || _previousSnapshot is null)
            return;

        var current = TakeSnapshot();
        var (started, stopped) = CompareSnapshots(_previousSnapshot, current);

        foreach (var proc in started)
        {
            var payload = new Dictionary<string, object>
            {
                ["process_name"] = proc.Name,
                ["pid"] = proc.Pid,
                ["start_time"] = proc.StartTime.ToString("O")
            };

            if (proc.ExecutablePath is not null)
                payload["executable_path"] = proc.ExecutablePath;
            if (proc.SessionId is not null)
                payload["session_id"] = proc.SessionId.Value;
            if (proc.WindowTitle is not null)
                payload["window_title"] = proc.WindowTitle;

            var deviceEvent = DeviceEvent.Create(
                _deviceId, EventType.ProcessStart, EventSeverity.Info, payload);
            await _eventStore.InsertEventAsync(deviceEvent);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var proc in stopped)
        {
            var payload = new Dictionary<string, object>
            {
                ["process_name"] = proc.Name,
                ["pid"] = proc.Pid,
                ["start_time"] = proc.StartTime.ToString("O"),
                ["stop_time"] = now.ToString("O")
            };

            if (proc.StartTime > DateTimeOffset.MinValue)
            {
                var duration = (now - proc.StartTime).TotalSeconds;
                if (duration > 0)
                    payload["duration_seconds"] = Math.Round(duration, 1);
            }

            if (proc.ExecutablePath is not null)
                payload["executable_path"] = proc.ExecutablePath;
            if (proc.WindowTitle is not null)
                payload["window_title"] = proc.WindowTitle;

            var deviceEvent = DeviceEvent.Create(
                _deviceId, EventType.ProcessStop, EventSeverity.Info, payload);
            await _eventStore.InsertEventAsync(deviceEvent);
        }

        if (started.Count > 0 || stopped.Count > 0)
        {
            _logger.LogDebug("Process changes: {Started} started, {Stopped} stopped",
                started.Count, stopped.Count);
        }

        _previousSnapshot = current;
    }

    internal static Dictionary<ProcessKey, ProcessSnapshot> TakeSnapshot()
    {
        var snapshot = new Dictionary<ProcessKey, ProcessSnapshot>();

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id is 0 or 4)
                    continue;

                var name = proc.ProcessName;
                if (ExcludedProcesses.Contains(name))
                    continue;

                var startTime = GetProcessStartTime(proc);
                var key = new ProcessKey(proc.Id, startTime);
                var windowTitle = GetWindowTitle(proc);

                snapshot[key] = new ProcessSnapshot(
                    proc.Id,
                    name,
                    GetExecutablePath(proc),
                    startTime,
                    GetSessionId(proc),
                    windowTitle);
            }
            catch (InvalidOperationException)
            {
                // Process exited between enumeration and property access
            }
            finally
            {
                proc.Dispose();
            }
        }

        return snapshot;
    }

    internal static (List<ProcessSnapshot> started, List<ProcessSnapshot> stopped) CompareSnapshots(
        Dictionary<ProcessKey, ProcessSnapshot> previous,
        Dictionary<ProcessKey, ProcessSnapshot> current)
    {
        var started = new List<ProcessSnapshot>();
        var stopped = new List<ProcessSnapshot>();

        foreach (var (key, proc) in current)
        {
            if (!previous.ContainsKey(key))
                started.Add(proc);
        }

        foreach (var (key, proc) in previous)
        {
            if (!current.ContainsKey(key))
                stopped.Add(proc);
        }

        return (started, stopped);
    }

    private static DateTimeOffset GetProcessStartTime(Process proc)
    {
        try
        {
            return new DateTimeOffset(proc.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch
        {
            return DateTimeOffset.MinValue;
        }
    }

    private static string? GetExecutablePath(Process proc)
    {
        try
        {
            return proc.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static int? GetSessionId(Process proc)
    {
        try
        {
            return proc.SessionId;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetWindowTitle(Process proc)
    {
        try
        {
            var title = proc.MainWindowTitle;
            return string.IsNullOrWhiteSpace(title) ? null : title;
        }
        catch
        {
            return null;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _pollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _logger.LogInformation("ProcessMonitor stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
    }

    internal readonly record struct ProcessKey(int Pid, DateTimeOffset StartTime);

    internal sealed record ProcessSnapshot(
        int Pid,
        string Name,
        string? ExecutablePath,
        DateTimeOffset StartTime,
        int? SessionId,
        string? WindowTitle = null);
}
