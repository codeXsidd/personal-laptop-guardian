using System.Timers;

namespace LaptopGuardian.Desktop.Services;

public sealed class AgentStatusService : IDisposable
{
    private readonly IpcClient _ipcClient = new();
    private readonly System.Timers.Timer _timer;
    private AgentStatus _currentStatus = AgentStatus.Disconnected;

    public event EventHandler<AgentStatus>? StatusChanged;

    public AgentStatus CurrentStatus => _currentStatus;

    public AgentStatusService()
    {
        _timer = new System.Timers.Timer(3000);
        _timer.Elapsed += OnTimerElapsed;
    }

    public void Start()
    {
        _ = RefreshAsync();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var response = await _ipcClient.SendAsync("status");

        AgentStatus status;
        if (!response.Success)
        {
            status = AgentStatus.Disconnected;
        }
        else
        {
            status = new AgentStatus
            {
                IsRunning = true,
                IsPaired = response.GetBool("paired"),
                IsRegistered = response.GetBool("registered"),
                IsOnline = response.GetBool("online"),
                DeviceId = response.GetString("device_id"),
                MachineName = response.GetString("machine_name"),
                PairingCode = response.GetString("pairing_code"),
                PairingCodeExpiresAt = ParseDateTimeOffset(response.GetString("pairing_code_expires_at")),
                PairedAt = ParseDateTimeOffset(response.GetString("paired_at")),
                PendingEvents = response.GetInt("pending_events"),
                TotalEvents = response.GetInt("total_events"),
                CpuPercent = response.GetDouble("cpu_percent"),
                MemoryPercent = response.GetDouble("memory_percent"),
                DiskPercent = response.GetDouble("disk_percent"),
                BatteryPercent = response.GetDouble("battery_percent"),
                BatteryStatus = response.GetString("battery_status"),
                Hostname = response.GetString("hostname"),
                OsVersion = response.GetString("os_version"),
                MetricsCapturedAt = ParseDateTimeOffset(response.GetString("metrics_captured_at")),
            };
        }

        _currentStatus = status;
        StatusChanged?.Invoke(this, status);
    }

    public async Task<(bool success, string? code, DateTimeOffset? expiresAt, string? error)> RefreshPairingCodeAsync()
    {
        var response = await _ipcClient.SendAsync("refresh-code");
        if (!response.Success)
            return (false, null, null, response.ErrorMessage ?? "Failed to refresh code");

        var code = response.GetString("pairing_code");
        var expiresAt = ParseDateTimeOffset(response.GetString("expires_at"));
        return (true, code, expiresAt, null);
    }

    public async Task<(bool success, string? error)> UnpairDeviceAsync()
    {
        var response = await _ipcClient.SendAsync("unpair");
        if (!response.Success)
            return (false, response.ErrorMessage ?? "Failed to unpair device");
        return (true, null);
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        return DateTimeOffset.TryParse(s, out var dt) ? dt : null;
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}

public sealed class AgentStatus
{
    public bool IsRunning { get; init; }
    public bool IsPaired { get; init; }
    public bool IsRegistered { get; init; }
    public bool IsOnline { get; init; }
    public string? DeviceId { get; init; }
    public string? MachineName { get; init; }
    public string? PairingCode { get; init; }
    public DateTimeOffset? PairingCodeExpiresAt { get; init; }
    public DateTimeOffset? PairedAt { get; init; }
    public int PendingEvents { get; init; }
    public int TotalEvents { get; init; }
    public double? CpuPercent { get; init; }
    public double? MemoryPercent { get; init; }
    public double? DiskPercent { get; init; }
    public double? BatteryPercent { get; init; }
    public string? BatteryStatus { get; init; }
    public string? Hostname { get; init; }
    public string? OsVersion { get; init; }
    public DateTimeOffset? MetricsCapturedAt { get; init; }

    public static AgentStatus Disconnected => new() { IsRunning = false };

    public string ProtectionStatus
    {
        get
        {
            if (!IsRunning) return "Service Offline";
            if (!IsRegistered) return "Not Registered";
            if (!IsPaired) return "Awaiting Pairing";
            if (!IsOnline) return "Offline";
            return "Protected";
        }
    }
}
