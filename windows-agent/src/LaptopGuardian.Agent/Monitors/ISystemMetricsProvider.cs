namespace LaptopGuardian.Agent.Monitors;

public interface ISystemMetricsProvider
{
    SystemMetricsSnapshot? LatestMetrics { get; }
}

public sealed record SystemMetricsSnapshot(
    double CpuPercent,
    double MemoryUsedBytes,
    double MemoryTotalBytes,
    double MemoryPercent,
    IReadOnlyList<DiskInfo> Disks,
    double? BatteryPercent,
    string? BatteryStatus,
    string Hostname,
    string OsVersion,
    DateTimeOffset CapturedAt);

public sealed record DiskInfo(
    string DriveName,
    string DriveFormat,
    long TotalBytes,
    long FreeBytes,
    double UsedPercent);
