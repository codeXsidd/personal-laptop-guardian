namespace LaptopGuardian.Agent.Models;

public sealed class DeviceIdentity
{
    public required string DeviceId { get; init; }
    public string? ServerDeviceId { get; set; }
    public string? ApiKey { get; set; }
    public string? PairingCode { get; set; }
    public DateTimeOffset? PairingCodeExpiresAt { get; set; }
    public DateTimeOffset? PairedAt { get; set; }
    public string MachineName { get; init; } = Environment.MachineName;

    public bool IsRegistered => ApiKey is not null;
    public bool IsPaired => PairedAt is not null;
}
