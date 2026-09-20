namespace LaptopGuardian.Agent.Models;

public sealed class DeviceIdentity
{
    public required string DeviceId { get; init; }
    public string? ApiKey { get; set; }
    public DateTimeOffset? PairedAt { get; set; }
    public string MachineName { get; init; } = Environment.MachineName;
}
