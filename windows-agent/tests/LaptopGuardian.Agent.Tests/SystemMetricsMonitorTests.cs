using LaptopGuardian.Agent.Monitors;

namespace LaptopGuardian.Agent.Tests;

public sealed class SystemMetricsMonitorTests
{
    [Fact]
    public void BuildPayload_NormalSnapshot_ContainsCoreFields()
    {
        var snapshot = CreateSnapshot();
        var payload = SystemMetricsMonitor.BuildPayload(snapshot);

        Assert.Equal("TESTPC", payload["hostname"]);
        Assert.Equal("Microsoft Windows NT 10.0.22000.0", payload["os_version"]);
        Assert.True(payload.ContainsKey("captured_at"));
    }

    [Fact]
    public void BuildPayload_WithCpu_IncludesCpuPercent()
    {
        var snapshot = CreateSnapshot(cpuPercent: 42.7);
        var payload = SystemMetricsMonitor.BuildPayload(snapshot);

        Assert.Equal(42.7, payload["cpu_percent"]);
    }

    [Fact]
    public void BuildPayload_CpuUnavailable_OmitsCpuPercent()
    {
        var snapshot = CreateSnapshot(cpuPercent: -1);
        var payload = SystemMetricsMonitor.BuildPayload(snapshot);

        Assert.False(payload.ContainsKey("cpu_percent"));
    }

    [Fact]
    public void BuildPayload_WithMemory_IncludesMemoryFields()
    {
        var snapshot = CreateSnapshot(
            memUsed: 8L * 1024 * 1024 * 1024,
            memTotal: 16L * 1024 * 1024 * 1024,
            memPercent: 50.0);
        var payload = SystemMetricsMonitor.BuildPayload(snapshot);

        Assert.Equal(8192.0, payload["memory_used_mb"]);
        Assert.Equal(16384.0, payload["memory_total_mb"]);
        Assert.Equal(50.0, payload["memory_percent"]);
    }

    [Fact]
    public void BuildPayload_ZeroMemory_OmitsMemoryFields()
    {
        var snapshot = CreateSnapshot(memUsed: 0, memTotal: 0, memPercent: 0);
        var payload = SystemMetricsMonitor.BuildPayload(snapshot);

        Assert.False(payload.ContainsKey("memory_used_mb"));
    }

    [Fact]
    public void BuildPayload_WithDisks_IncludesDiskArray()
    {
        var disks = new List<DiskInfo>
        {
            new("C:\\", "NTFS", 500_000_000_000, 200_000_000_000, 60.0),
            new("D:\\", "NTFS", 1_000_000_000_000, 800_000_000_000, 20.0)
        };
        var snapshot = CreateSnapshot(disks: disks);
        var payload = SystemMetricsMonitor.BuildPayload(snapshot);

        Assert.True(payload.ContainsKey("disks"));
        var diskList = (List<Dictionary<string, object>>)payload["disks"];
        Assert.Equal(2, diskList.Count);
        Assert.Equal("C:\\", diskList[0]["drive"]);
        Assert.Equal("NTFS", diskList[0]["format"]);
    }

    [Fact]
    public void BuildPayload_EmptyDisks_OmitsDiskArray()
    {
        var snapshot = CreateSnapshot(disks: []);
        var payload = SystemMetricsMonitor.BuildPayload(snapshot);

        Assert.False(payload.ContainsKey("disks"));
    }

    [Fact]
    public void BuildPayload_WithBattery_IncludesBatteryFields()
    {
        var snapshot = CreateSnapshot(batteryPercent: 75, batteryStatus: "discharging");
        var payload = SystemMetricsMonitor.BuildPayload(snapshot);

        Assert.Equal(75.0, payload["battery_percent"]);
        Assert.Equal("discharging", payload["battery_status"]);
    }

    [Fact]
    public void BuildPayload_NoBattery_OmitsBatteryFields()
    {
        var snapshot = CreateSnapshot(batteryPercent: null, batteryStatus: null);
        var payload = SystemMetricsMonitor.BuildPayload(snapshot);

        Assert.False(payload.ContainsKey("battery_percent"));
        Assert.False(payload.ContainsKey("battery_status"));
    }

    [Fact]
    public void BuildPayload_BatteryNullStatus_OmitsBatteryStatus()
    {
        var snapshot = CreateSnapshot(batteryPercent: 50, batteryStatus: null);
        var payload = SystemMetricsMonitor.BuildPayload(snapshot);

        Assert.Equal(50.0, payload["battery_percent"]);
        Assert.False(payload.ContainsKey("battery_status"));
    }

    [Fact]
    public void GetDiskInfo_ReturnsNonNull()
    {
        var disks = SystemMetricsMonitor.GetDiskInfo();
        Assert.NotNull(disks);
    }

    [Fact]
    public void GetDiskInfo_OnlyFixedDrives()
    {
        var disks = SystemMetricsMonitor.GetDiskInfo();
        foreach (var d in disks)
        {
            Assert.True(d.TotalBytes > 0);
            Assert.True(d.UsedPercent >= 0 && d.UsedPercent <= 100);
        }
    }

    [Fact]
    public void GetMemoryInfo_ReturnsReasonableValues()
    {
        var (usedBytes, totalBytes, percent) = SystemMetricsMonitor.GetMemoryInfo();
        Assert.True(totalBytes >= 0);
        Assert.True(percent >= 0 && percent <= 100);
    }

    [Fact]
    public void BuildPayload_MemoryPercentBoundary_ZeroPercent()
    {
        var snapshot = CreateSnapshot(memUsed: 0, memTotal: 1024 * 1024, memPercent: 0);
        var payload = SystemMetricsMonitor.BuildPayload(snapshot);

        Assert.Equal(0.0, payload["memory_percent"]);
    }

    [Fact]
    public void BuildPayload_MemoryPercentBoundary_HundredPercent()
    {
        long total = 16L * 1024 * 1024 * 1024;
        var snapshot = CreateSnapshot(memUsed: total, memTotal: total, memPercent: 100);
        var payload = SystemMetricsMonitor.BuildPayload(snapshot);

        Assert.Equal(100.0, payload["memory_percent"]);
    }

    private static SystemMetricsSnapshot CreateSnapshot(
        double cpuPercent = 25.0,
        double memUsed = 4L * 1024 * 1024 * 1024,
        double memTotal = 16L * 1024 * 1024 * 1024,
        double memPercent = 25.0,
        IReadOnlyList<DiskInfo>? disks = null,
        double? batteryPercent = 80,
        string? batteryStatus = "charging",
        string hostname = "TESTPC",
        string osVersion = "Microsoft Windows NT 10.0.22000.0")
    {
        return new SystemMetricsSnapshot(
            CpuPercent: cpuPercent,
            MemoryUsedBytes: memUsed,
            MemoryTotalBytes: memTotal,
            MemoryPercent: memPercent,
            Disks: disks ?? new List<DiskInfo>
            {
                new("C:\\", "NTFS", 500_000_000_000, 250_000_000_000, 50.0)
            },
            BatteryPercent: batteryPercent,
            BatteryStatus: batteryStatus,
            Hostname: hostname,
            OsVersion: osVersion,
            CapturedAt: DateTimeOffset.UtcNow);
    }
}
