using System.Diagnostics;
using System.Runtime.Versioning;
using LaptopGuardian.Agent.Configuration;
using LaptopGuardian.Agent.Identity;
using LaptopGuardian.Agent.Models;
using LaptopGuardian.Agent.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaptopGuardian.Agent.Monitors;

[SupportedOSPlatform("windows")]
public sealed class SystemMetricsMonitor : IEventMonitor, ISystemMetricsProvider
{
    private readonly IEventStore _eventStore;
    private readonly IDeviceIdentityService _identityService;
    private readonly AgentOptions _options;
    private readonly ILogger<SystemMetricsMonitor> _logger;
    private Timer? _pollTimer;
    private string? _deviceId;
    private PerformanceCounter? _cpuCounter;

    public string MonitorName => "SystemMetrics";

    public SystemMetricsSnapshot? LatestMetrics { get; private set; }

    public SystemMetricsMonitor(
        IEventStore eventStore,
        IDeviceIdentityService identityService,
        IOptions<AgentOptions> options,
        ILogger<SystemMetricsMonitor> logger)
    {
        _eventStore = eventStore;
        _identityService = identityService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.SystemMetrics.Enabled)
        {
            _logger.LogInformation("SystemMetricsMonitor: Disabled via configuration");
            return;
        }

        var identity = await _identityService.GetOrCreateIdentityAsync(cancellationToken);
        _deviceId = identity.DeviceId;

        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // first call always returns 0
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SystemMetricsMonitor: Cannot create CPU performance counter — CPU metrics unavailable");
        }

        var interval = TimeSpan.FromSeconds(_options.SystemMetrics.IntervalSeconds);
        _pollTimer = new Timer(OnPollTimerCallback, null, TimeSpan.FromSeconds(5), interval);

        _logger.LogInformation("SystemMetricsMonitor started (interval: {Interval}s)",
            _options.SystemMetrics.IntervalSeconds);
    }

    private async void OnPollTimerCallback(object? state)
    {
        try
        {
            await CollectMetricsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SystemMetricsMonitor: Error collecting metrics");
        }
    }

    internal async Task CollectMetricsAsync()
    {
        if (_deviceId is null)
            return;

        var snapshot = CaptureSnapshot();
        LatestMetrics = snapshot;

        var payload = BuildPayload(snapshot);
        var deviceEvent = DeviceEvent.Create(
            _deviceId, EventType.SystemMetrics, EventSeverity.Info, payload);
        await _eventStore.InsertEventAsync(deviceEvent);

        _logger.LogDebug(
            "System metrics: CPU={Cpu:F1}%, Memory={Mem:F1}%, Battery={Bat}",
            snapshot.CpuPercent, snapshot.MemoryPercent,
            snapshot.BatteryPercent?.ToString("F0") ?? "N/A");
    }

    internal SystemMetricsSnapshot CaptureSnapshot()
    {
        var cpu = GetCpuPercent();
        var (memUsed, memTotal, memPercent) = GetMemoryInfo();
        var disks = GetDiskInfo();
        var (batteryPercent, batteryStatus) = GetBatteryInfo();

        return new SystemMetricsSnapshot(
            CpuPercent: cpu,
            MemoryUsedBytes: memUsed,
            MemoryTotalBytes: memTotal,
            MemoryPercent: memPercent,
            Disks: disks,
            BatteryPercent: batteryPercent,
            BatteryStatus: batteryStatus,
            Hostname: Environment.MachineName,
            OsVersion: Environment.OSVersion.VersionString,
            CapturedAt: DateTimeOffset.UtcNow);
    }

    internal double GetCpuPercent()
    {
        try
        {
            return _cpuCounter?.NextValue() ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    internal static (double usedBytes, double totalBytes, double percent) GetMemoryInfo()
    {
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            var totalBytes = (double)gcInfo.TotalAvailableMemoryBytes;
            var availableBytes = GetAvailablePhysicalMemory();
            var usedBytes = totalBytes - availableBytes;
            var percent = totalBytes > 0 ? (usedBytes / totalBytes) * 100.0 : 0;
            return (usedBytes, totalBytes, Math.Clamp(percent, 0, 100));
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    private static double GetAvailablePhysicalMemory()
    {
        try
        {
            using var counter = new PerformanceCounter("Memory", "Available Bytes");
            return counter.NextValue();
        }
        catch
        {
            return 0;
        }
    }

    internal static List<DiskInfo> GetDiskInfo()
    {
        var disks = new List<DiskInfo>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    continue;

                var total = drive.TotalSize;
                var free = drive.AvailableFreeSpace;
                var used = total - free;
                var percent = total > 0 ? (double)used / total * 100.0 : 0;

                disks.Add(new DiskInfo(
                    drive.Name,
                    drive.DriveFormat,
                    total,
                    free,
                    Math.Round(percent, 1)));
            }
        }
        catch (Exception)
        {
            // DriveInfo can throw on inaccessible drives
        }

        return disks;
    }

    internal static (double? percent, string? status) GetBatteryInfo()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");

            foreach (var obj in searcher.Get())
            {
                var charge = obj["EstimatedChargeRemaining"];
                if (charge is null)
                    continue;

                var percent = Convert.ToDouble(charge);
                var batteryStatus = Convert.ToInt32(obj["BatteryStatus"] ?? 0);

                // WMI BatteryStatus: 1=Discharging, 2=AC, 3=Fully Charged, 4=Low, 5=Critical
                var status = batteryStatus switch
                {
                    1 => "discharging",
                    2 => "charging",
                    3 => "full",
                    4 => "low",
                    5 => "critical",
                    _ => "unknown"
                };

                return (percent, status);
            }

            return (null, "no_battery");
        }
        catch
        {
            return (null, null);
        }
    }

    internal static Dictionary<string, object> BuildPayload(SystemMetricsSnapshot snapshot)
    {
        var payload = new Dictionary<string, object>
        {
            ["hostname"] = snapshot.Hostname,
            ["os_version"] = snapshot.OsVersion,
            ["captured_at"] = snapshot.CapturedAt.ToString("O")
        };

        if (snapshot.CpuPercent >= 0)
            payload["cpu_percent"] = Math.Round(snapshot.CpuPercent, 1);

        if (snapshot.MemoryTotalBytes > 0)
        {
            payload["memory_used_mb"] = Math.Round(snapshot.MemoryUsedBytes / (1024 * 1024), 0);
            payload["memory_total_mb"] = Math.Round(snapshot.MemoryTotalBytes / (1024 * 1024), 0);
            payload["memory_percent"] = Math.Round(snapshot.MemoryPercent, 1);
        }

        if (snapshot.Disks.Count > 0)
        {
            payload["disks"] = snapshot.Disks.Select(d => new Dictionary<string, object>
            {
                ["drive"] = d.DriveName,
                ["format"] = d.DriveFormat,
                ["total_gb"] = Math.Round(d.TotalBytes / (1024.0 * 1024 * 1024), 1),
                ["free_gb"] = Math.Round(d.FreeBytes / (1024.0 * 1024 * 1024), 1),
                ["used_percent"] = d.UsedPercent
            }).ToList();
        }

        if (snapshot.BatteryPercent is not null)
        {
            payload["battery_percent"] = snapshot.BatteryPercent.Value;
            if (snapshot.BatteryStatus is not null)
                payload["battery_status"] = snapshot.BatteryStatus;
        }

        return payload;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _pollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _logger.LogInformation("SystemMetricsMonitor stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _cpuCounter?.Dispose();
    }
}
