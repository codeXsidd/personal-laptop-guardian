using LaptopGuardian.Agent.Models;

namespace LaptopGuardian.Agent.Backend;

public interface IBackendClient
{
    Task<RegisterDeviceResponse> RegisterDeviceAsync(
        string machineName,
        string? osVersion,
        string? agentVersion,
        CancellationToken cancellationToken = default);

    Task<IngestEventsResponse> IngestEventsAsync(
        string apiKey,
        IReadOnlyList<DeviceEvent> events,
        CancellationToken cancellationToken = default);

    Task<HeartbeatResponse> SendHeartbeatAsync(
        string apiKey,
        Dictionary<string, object>? metrics = null,
        CancellationToken cancellationToken = default);
}
